using System.Runtime.CompilerServices;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Practice;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.UnitTests;

public class PracticeServiceTests
{
    private readonly InMemoryPractice _practice = new();
    private readonly Concepts _concepts = new();
    private readonly FakeAiClient _aiClient = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };
    private readonly AdvancingClock _clock = new();
    private readonly Guid _sourceId = Guid.NewGuid();

    private PracticeService CreateService() =>
        new(_practice, _concepts, new Titles(_sourceId), _aiClient, _currentUser, _clock);

    private Concept AddConcept(string name, bool withPassage = false)
    {
        var concept = new Concept { Id = Guid.NewGuid(), UserId = _currentUser.UserId, Name = name, NormalizedName = name.ToLowerInvariant(), State = ProposalState.Accepted };
        if (withPassage)
        {
            concept.Passages.Add(new ConceptPassage { Id = Guid.NewGuid(), ConceptId = concept.Id, SourceId = _sourceId, PageNumber = 258, PageLabel = "240", Section = null, Snippet = "snippet" });
        }

        _concepts.All.Add(concept);
        return concept;
    }

    [Fact]
    public async Task Mastery_StartsNew_BecomesLearning_ThenSolidOnceRecentAttemptsAreMostlyRight()
    {
        var concept = AddConcept("Pooling");
        var service = CreateService();
        var question = await service.AddQuestionAsync(concept.Id, new CreateQuestionRequest("What does pooling do?", "Downsamples."), default);

        Assert.Equal("New", MasteryDto.From([]).Level);
        var first = await service.RecordAttemptAsync(new RecordAttemptRequest(question.Id, null, "flashcards", 0), default);
        Assert.Equal("Learning", first.Mastery.Level);

        AttemptResultDto last = first;
        for (var i = 0; i < 5; i++)
        {
            last = await service.RecordAttemptAsync(new RecordAttemptRequest(question.Id, null, "flashcards", 1), default);
        }

        Assert.Equal("Solid", last.Mastery.Level);
        Assert.Equal(6, last.Mastery.Attempts);
        Assert.Equal(1, last.Mastery.RecentScore);
    }

    [Fact]
    public async Task RecordAttemptAsync_AcceptsAGameAttemptWithOnlyAConcept_AndRejectsBadInput()
    {
        var concept = AddConcept("Vectors");
        var service = CreateService();

        var result = await service.RecordAttemptAsync(new RecordAttemptRequest(null, concept.Id, "arcade: vector duel", 0.5), default);

        Assert.Equal(concept.Id, result.ConceptId);
        Assert.Equal("arcade: vector duel", Assert.Single(_practice.Attempts).Platform);
        await Assert.ThrowsAsync<RequestValidationException>(() => service.RecordAttemptAsync(new RecordAttemptRequest(null, concept.Id, "game", 1.5), default));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.RecordAttemptAsync(new RecordAttemptRequest(null, concept.Id, " ", 1), default));
        await Assert.ThrowsAsync<RequestValidationException>(() => service.RecordAttemptAsync(new RecordAttemptRequest(null, null, "game", 1), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.RecordAttemptAsync(new RecordAttemptRequest(Guid.NewGuid(), null, "game", 1), default));
    }

    [Fact]
    public async Task GenerateQuestionsAsync_SendsTheFullPassageText_TagsCardsWithTheirPlace_AndSkipsRepeats()
    {
        var concept = AddConcept("Pooling", withPassage: true);
        _practice.TextAt = "Pooling layers downsample feature maps.";
        _aiClient.Questions = [new("What does pooling do?", "It downsamples.", 1), new("what does  pooling do?", "Again.", 1), new("Loose card", "No place.", 7)];
        var service = CreateService();

        var cards = await service.GenerateQuestionsAsync(concept.Id, default);

        Assert.Equal("Pooling layers downsample feature maps.", Assert.Single(_aiClient.LastRequest!.Passages).Text);
        Assert.Equal(2, cards.Count);
        Assert.All(cards, card => Assert.True(card.GeneratedByModel));
        Assert.Equal((_sourceId, 258, "240", "Deep Learning Book"), (cards[0].SourceId!.Value, cards[0].PageNumber!.Value, cards[0].PageLabel!, cards[0].SourceTitle!));
        Assert.Null(cards[1].SourceId);

        Assert.Empty(await service.GenerateQuestionsAsync(concept.Id, default));
    }

    [Fact]
    public async Task GenerateQuestionsAsync_NeedsALinkedPassage()
    {
        var concept = AddConcept("Pooling");

        await Assert.ThrowsAsync<RequestValidationException>(() => CreateService().GenerateQuestionsAsync(concept.Id, default));
    }

    [Fact]
    public async Task Questions_BelongToTheirOwner()
    {
        var concept = AddConcept("Pooling");
        var service = CreateService();
        var question = await service.AddQuestionAsync(concept.Id, new CreateQuestionRequest("Q", "A"), default);
        _currentUser.UserId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => service.ListQuestionsAsync(concept.Id, default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteQuestionAsync(question.Id, default));
    }

    [Fact]
    public async Task ListDueAsync_ListsDueQuestionsEarliestFirst_ThenNewOnes_AndCountsThemPerConcept()
    {
        var pooling = AddConcept("Pooling");
        var vectors = AddConcept("Vectors");
        var service = CreateService();
        var known = await service.AddQuestionAsync(pooling.Id, new CreateQuestionRequest("Known", "A"), default);
        var missed = await service.AddQuestionAsync(pooling.Id, new CreateQuestionRequest("Missed", "A"), default);
        var fresh = await service.AddQuestionAsync(vectors.Id, new CreateQuestionRequest("Fresh", "A"), default);

        var gotIt = await service.RecordAttemptAsync(new RecordAttemptRequest(known.Id, null, "flashcards", 1), default);
        var miss = await service.RecordAttemptAsync(new RecordAttemptRequest(missed.Id, null, "flashcards", 0), default);
        var game = await service.RecordAttemptAsync(new RecordAttemptRequest(null, vectors.Id, "game", 1), default);
        Assert.Equal(_practice.Attempts[0].AnsweredAtUtc.AddDays(3), gotIt.DueAtUtc);
        Assert.Equal(_practice.Attempts[1].AnsweredAtUtc.AddDays(1), miss.DueAtUtc);
        Assert.Null(game.DueAtUtc);

        var today = await service.ListDueAsync(default);
        Assert.Equal(["Fresh"], today.Questions.Select(q => q.Prompt));
        Assert.Null(today.Questions[0].DueAtUtc);
        Assert.Equal((0, 1), (today.DueCount, today.NewCount));
        Assert.Equal([new DueConceptDto(vectors.Id, "Vectors", 0, 1)], today.Concepts);

        _clock.Skip(TimeSpan.FromDays(3));
        var later = await service.ListDueAsync(default);
        Assert.Equal(["Missed", "Known", "Fresh"], later.Questions.Select(q => q.Prompt));
        Assert.Equal(miss.DueAtUtc, later.Questions[0].DueAtUtc);
        Assert.Equal(
            [new DueConceptDto(pooling.Id, "Pooling", 2, 0), new DueConceptDto(vectors.Id, "Vectors", 0, 1)],
            later.Concepts);
        Assert.Equal(fresh.Id, later.Questions[2].Id);
    }

    [Fact]
    public async Task ListDueAsync_OffersAtMostTenNewQuestions_ButCountsThemAll()
    {
        var concept = AddConcept("Pooling");
        var service = CreateService();
        for (var i = 0; i < PracticeService.NewPerReview + 2; i++)
        {
            await service.AddQuestionAsync(concept.Id, new CreateQuestionRequest($"Q{i}", "A"), default);
        }

        var due = await service.ListDueAsync(default);

        Assert.Equal(PracticeService.NewPerReview, due.Questions.Count);
        Assert.Equal("Q0", due.Questions[0].Prompt);
        Assert.Equal(PracticeService.NewPerReview + 2, due.NewCount);
        Assert.Equal(PracticeService.NewPerReview + 2, Assert.Single(due.Concepts).New);
    }

    [Fact]
    public async Task ListDueAsync_LeavesOutOtherUsersAndRejectedConcepts()
    {
        var kept = AddConcept("Pooling");
        var rejected = AddConcept("Dropped");
        rejected.State = ProposalState.Rejected;
        var service = CreateService();
        await service.AddQuestionAsync(kept.Id, new CreateQuestionRequest("Mine", "A"), default);
        _practice.Questions.Add(new Question { Id = Guid.NewGuid(), UserId = _currentUser.UserId, ConceptId = rejected.Id, Prompt = "Hidden", Answer = "A" });

        Assert.Equal(["Mine"], (await service.ListDueAsync(default)).Questions.Select(q => q.Prompt));

        _currentUser.UserId = Guid.NewGuid();
        var other = await service.ListDueAsync(default);
        Assert.Empty(other.Questions);
        Assert.Empty(other.Concepts);
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now = _now.AddMinutes(1);

        public void Skip(TimeSpan span) => _now += span;
    }

    private sealed class InMemoryPractice : IPracticeRepository
    {
        public List<Question> Questions { get; } = [];
        public List<Attempt> Attempts { get; } = [];
        public string TextAt { get; set; } = "";

        public Task<List<Question>> ListQuestionsAsync(Guid conceptId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Questions.Where(q => q.ConceptId == conceptId && q.UserId == userId).ToList());

        public Task<List<Question>> ListQuestionsForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Questions.Where(q => q.UserId == userId).ToList());

        public Task<Question?> FindQuestionAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Questions.FirstOrDefault(q => q.Id == id && q.UserId == userId));

        public Task<List<Attempt>> ListAttemptsAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Attempts.Where(a => a.UserId == userId).ToList());

        public Task<string> GetTextAtAsync(Guid sourceId, int pageNumber, string? section, int maxLength, CancellationToken cancellationToken) =>
            Task.FromResult(TextAt);

        public void Add(Question question) => Questions.Add(question);
        public void Add(Attempt attempt) => Attempts.Add(attempt);
        public void Remove(Question question) => Questions.Remove(question);
        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Concepts : IConceptRepository
    {
        public List<Concept> All { get; } = [];

        public Task<List<Concept>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(All.Where(c => c.UserId == userId).ToList());

        public Task<List<ConceptPrerequisite>> ListEdgesForUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public void Add(Concept concept) => throw new NotImplementedException();
        public void Add(ConceptPrerequisite edge) => throw new NotImplementedException();
        public void Add(ConceptPassage passage) => throw new NotImplementedException();
        public void Remove(Concept concept) => throw new NotImplementedException();
        public void Remove(ConceptPassage passage) => throw new NotImplementedException();
        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class Titles(Guid knownSourceId) : ISourceRepository
    {
        public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(id == knownSourceId ? new SourceDto(id, "Deep Learning Book", "d2l.pdf", "application/pdf", 1, "Ready", null, 1, DateTimeOffset.UnixEpoch, []) : null);

        public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task AddAsync(Source source, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task UpdateAsync(Source source, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> SetDetailsForUserAsync(Guid id, Guid userId, string? license, string? sourceUrl, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeAiClient : IAiClient
    {
        public IReadOnlyList<GeneratedQuestionDto> Questions { get; set; } = [];
        public GenerateQuestionsRequest? LastRequest { get; private set; }

        public Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new GenerateQuestionsResponse(Questions));
        }

        public Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
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
