using System.Runtime.CompilerServices;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Concepts;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.UnitTests;

public class ConceptServiceTests
{
    private readonly InMemoryConceptRepository _repository = new();
    private readonly StubSourceRepository _sources = new();
    private readonly StubSearchRepository _search = new();
    private readonly FakeAiClient _aiClient = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };

    private ConceptService CreateService() =>
        new(_repository, new NoAttempts(), _sources, _search, _aiClient, _currentUser, new FixedClock());

    private async Task<ConceptDto> Create(string name) =>
        await CreateService().CreateAsync(new CreateConceptRequest(name), default);

    [Fact]
    public async Task CreateAsync_TidiesTheName_AndRefusesTheSameNameInAnotherCase()
    {
        var created = await Create("  Chain   rule ");

        Assert.Equal("Chain rule", created.Name);
        Assert.Equal("Accepted", created.State);
        await Assert.ThrowsAsync<ConflictException>(() => Create("chain RULE"));
        await Assert.ThrowsAsync<RequestValidationException>(() => Create("   "));
    }

    [Fact]
    public async Task Graph_IsPerUser()
    {
        await Create("Gradients");
        _currentUser.UserId = Guid.NewGuid();

        var graph = await CreateService().GetGraphAsync(default);

        Assert.Empty(graph.Concepts);
    }

    [Fact]
    public async Task AddPrerequisiteAsync_RefusesSelfLinks_AndLoops()
    {
        var cnn = await Create("Convolutional neural networks");
        var convolution = await Create("Convolution");
        var matrices = await Create("Matrix multiplication");
        var service = CreateService();
        await service.AddPrerequisiteAsync(cnn.Id, convolution.Id, default);
        var graph = await service.AddPrerequisiteAsync(convolution.Id, matrices.Id, default);

        Assert.Equal(2, graph.Prerequisites.Count);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.AddPrerequisiteAsync(cnn.Id, cnn.Id, default));
        await Assert.ThrowsAsync<ConflictException>(() => service.AddPrerequisiteAsync(matrices.Id, cnn.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.AddPrerequisiteAsync(cnn.Id, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ProposePrerequisitesAsync_AddsProposals_ReusesKnownConcepts_AndSendsTheirNames()
    {
        var cnn = await Create("Convolutional neural networks");
        var matrices = await Create("Matrix multiplication");
        _aiClient.Proposals = [new("matrix  multiplication", null), new("Convolution", "Sliding a kernel over an input.")];

        var graph = await CreateService().ProposePrerequisitesAsync(cnn.Id, default);

        Assert.Equal(["Matrix multiplication"], _aiClient.LastRequest!.KnownConcepts);
        Assert.Equal(3, graph.Concepts.Count);
        var convolution = Assert.Single(graph.Concepts, c => c.Name == "Convolution");
        Assert.Equal("Proposed", convolution.State);
        Assert.True(convolution.ProposedByModel);
        Assert.Contains(graph.Prerequisites, e => e.PrerequisiteId == matrices.Id && e.State == "Proposed");
        Assert.Contains(graph.Prerequisites, e => e.PrerequisiteId == convolution.Id && e.State == "Proposed");
    }

    [Fact]
    public async Task ProposePrerequisitesAsync_DoesNotBringBackWhatTheUserRejectedOrRemoved()
    {
        var cnn = await Create("Convolutional neural networks");
        var matrices = await Create("Matrix multiplication");
        _aiClient.Proposals = [new("Convolution", null), new("Matrix multiplication", null)];
        var service = CreateService();
        var first = await service.ProposePrerequisitesAsync(cnn.Id, default);
        await service.RejectAsync(first.Concepts.Single(c => c.Name == "Convolution").Id, default);
        await service.RemovePrerequisiteAsync(cnn.Id, matrices.Id, default);

        var second = await service.ProposePrerequisitesAsync(cnn.Id, default);

        Assert.DoesNotContain(second.Concepts, c => c.Name == "Convolution");
        Assert.Empty(second.Prerequisites);
    }

    [Fact]
    public async Task ProposePrerequisitesAsync_SkipsProposalsThatWouldLoop()
    {
        var cnn = await Create("Convolutional neural networks");
        var deepLearning = await Create("Deep learning");
        var service = CreateService();
        await service.AddPrerequisiteAsync(deepLearning.Id, cnn.Id, default);
        _aiClient.Proposals = [new("Deep learning", null)];

        var graph = await service.ProposePrerequisitesAsync(cnn.Id, default);

        Assert.Single(graph.Prerequisites);
    }

    [Fact]
    public async Task AcceptAsync_AcceptsTheConcept_AndItsLinksToAcceptedConcepts()
    {
        var cnn = await Create("Convolutional neural networks");
        _aiClient.Proposals = [new("Convolution", null)];
        var service = CreateService();
        var proposed = await service.ProposePrerequisitesAsync(cnn.Id, default);
        var convolution = proposed.Concepts.Single(c => c.Name == "Convolution");

        var graph = await service.AcceptAsync(convolution.Id, default);

        Assert.All(graph.Concepts, c => Assert.Equal("Accepted", c.State));
        Assert.Equal("Accepted", Assert.Single(graph.Prerequisites).State);
    }

    [Fact]
    public async Task SplitAsync_KeepsTheOriginal_AndMakesThePartsItsPrerequisites()
    {
        var calculus = await Create("Calculus");
        var limits = await Create("Limits");

        var graph = await CreateService().SplitAsync(calculus.Id, new SplitConceptRequest(["Limits", "Derivatives", " "]), default);

        Assert.Equal(3, graph.Concepts.Count);
        Assert.Equal(2, graph.Prerequisites.Count);
        Assert.All(graph.Prerequisites, e => Assert.Equal(calculus.Id, e.ConceptId));
        Assert.Contains(graph.Prerequisites, e => e.PrerequisiteId == limits.Id);
    }

    [Fact]
    public async Task Passages_AreLinkedByPlace_NotTwice_AndOnlyFromTheUsersSources()
    {
        var concept = await Create("Normal maps");
        var sourceId = _sources.AddOwned(_currentUser.UserId, "Blender Manual");
        var service = CreateService();
        var request = new AddPassageRequest(sourceId, 1, null, "Materials > Normal Map Node", "Converts a  normal map\ninto a normal.");

        var passage = await service.AddPassageAsync(concept.Id, request, default);

        Assert.Equal("Blender Manual", passage.SourceTitle);
        Assert.Equal("Converts a normal map into a normal.", passage.Snippet);
        await Assert.ThrowsAsync<ConflictException>(() => service.AddPassageAsync(concept.Id, request, default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => service.AddPassageAsync(concept.Id, request with { SourceId = Guid.NewGuid() }, default));

        var details = await service.GetDetailsAsync(concept.Id, default);
        Assert.Equal(1, details.Concept.PassageCount);

        await service.RemovePassageAsync(concept.Id, passage.Id, default);
        Assert.Empty((await service.GetDetailsAsync(concept.Id, default)).Passages);
    }

    [Fact]
    public async Task SuggestPassagesAsync_SearchesWithoutAConversation_AndMarksLinkedPlaces()
    {
        var concept = await Create("Normal maps");
        var sourceId = _sources.AddOwned(_currentUser.UserId, "Blender Manual");
        _search.Hits =
        [
            Hit(sourceId, 1, "Normal Map Node"),
            Hit(sourceId, 1, "Bump Node"),
        ];
        var service = CreateService();
        await service.AddPassageAsync(concept.Id, new AddPassageRequest(sourceId, 1, null, "Normal Map Node", "text"), default);

        var suggestions = await service.SuggestPassagesAsync(concept.Id, default);

        Assert.Equal("Normal maps", _search.LastParameters!.QueryText);
        Assert.Equal(_currentUser.UserId, _search.LastParameters.UserId);
        Assert.Equal([true, false], suggestions.Select(s => s.Linked));
    }

    private static SearchHit Hit(Guid sourceId, int page, string section) =>
        new(Guid.NewGuid(), sourceId, "Blender Manual", "text/markdown", page, null, section, 0, "content", null, 1, 0.2f, ["semantic"]);

    private sealed class InMemoryConceptRepository : IConceptRepository
    {
        private readonly List<Concept> _concepts = [];
        private readonly List<ConceptPrerequisite> _edges = [];

        public Task<List<Concept>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_concepts.Where(c => c.UserId == userId).ToList());

        public Task<List<ConceptPrerequisite>> ListEdgesForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_edges.Where(e => _concepts.Any(c => c.Id == e.ConceptId && c.UserId == userId)).ToList());

        public void Add(Concept concept) => _concepts.Add(concept);

        public void Add(ConceptPrerequisite edge) => _edges.Add(edge);

        public void Add(ConceptPassage passage) => _concepts.Single(c => c.Id == passage.ConceptId).Passages.Add(passage);

        public void Remove(Concept concept) => _concepts.Remove(concept);

        public void Remove(ConceptPassage passage) => _concepts.Single(c => c.Id == passage.ConceptId).Passages.Remove(passage);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubSourceRepository : ISourceRepository
    {
        private readonly Dictionary<Guid, (Guid UserId, string Title)> _owned = [];

        public Guid AddOwned(Guid userId, string title)
        {
            var id = Guid.NewGuid();
            _owned[id] = (userId, title);
            return id;
        }

        public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_owned.TryGetValue(id, out var source) && source.UserId == userId
                ? new SourceDto(id, source.Title, "file.md", "text/markdown", 1, "Ready", null, 1, DateTimeOffset.UnixEpoch, [])
                : null);

        public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddAsync(Source source, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateAsync(Source source, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class NoAttempts : IPracticeRepository
    {
        public Task<List<Attempt>> ListAttemptsAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(new List<Attempt>());
        public Task<List<Question>> ListQuestionsAsync(Guid conceptId, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Question?> FindQuestionAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<string> GetTextAtAsync(Guid sourceId, int pageNumber, string? section, int maxLength, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Add(Question question) => throw new NotImplementedException();
        public void Add(Attempt attempt) => throw new NotImplementedException();
        public void Remove(Question question) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class StubSearchRepository : ISearchRepository
    {
        public IReadOnlyList<SearchHit> Hits { get; set; } = [];
        public SearchQueryParameters? LastParameters { get; private set; }

        public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQueryParameters parameters, CancellationToken cancellationToken)
        {
            LastParameters = parameters;
            return Task.FromResult(Hits);
        }
    }

    private sealed class FakeAiClient : IAiClient
    {
        public IReadOnlyList<ProposedConceptDto> Proposals { get; set; } = [];
        public ProposePrerequisitesRequest? LastRequest { get; private set; }

        public Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ProposePrerequisitesResponse(Proposals));
        }

        public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExpandQueryResponse([new QueryItemDto(request.Question, [1f])]));

        public Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<SynthesizeResponse> SynthesizeAsync(SynthesizeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
