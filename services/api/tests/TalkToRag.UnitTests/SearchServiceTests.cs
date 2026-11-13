using Microsoft.Extensions.Options;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Search;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.UnitTests;

public class SearchServiceTests
{
    private readonly InMemorySearchRepo _searchRepo = new();
    private readonly InMemorySourceRepo _sourceRepo = new();
    private readonly InMemoryConversationRepo _conversationRepo = new();
    private readonly FakeAiClient _aiClient = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };
    private readonly FixedClock _clock = new();
    private readonly IOptions<SearchOptions> _options = Options.Create(new SearchOptions());

    private SearchService CreateService() =>
        new(_searchRepo, _sourceRepo, _conversationRepo, _aiClient, _currentUser, _options, _clock);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_ThrowsValidation_WhenQueryEmpty(string query)
    {
        var service = CreateService();
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.SearchAsync(new SearchRequest(query), default));
    }

    [Fact]
    public async Task SearchAsync_UsesConfiguredCutoff_WhenRequestOmitsOne()
    {
        await CreateService().SearchAsync(new SearchRequest("TD learning"), default);

        Assert.Equal(new SearchOptions().MaxCosineDistance, _searchRepo.LastParameters!.MaxCosineDistance);
    }

    [Theory]
    [InlineData(0.4f, 0.4f)]
    [InlineData(0.0f, 0.05f)]
    [InlineData(9.0f, 1.5f)]
    public async Task SearchAsync_PassesRequestedCutoff_ClampedToSaneRange(float requested, float expected)
    {
        await CreateService().SearchAsync(
            new SearchRequest("TD learning", MaxCosineDistance: requested), default);

        Assert.Equal(expected, _searchRepo.LastParameters!.MaxCosineDistance);
    }

    [Fact]
    public async Task SearchAsync_InAnotherUsersConversation_IsNotFound_BeforeItsMessagesReachTheAiService()
    {
        var service = CreateService();
        var theirs = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Title = "Private",
            CreatedAtUtc = _clock.GetUtcNow(),
            UpdatedAtUtc = _clock.GetUtcNow()
        };
        theirs.Messages.Add(new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = theirs.Id,
            Role = MessageRole.User,
            Content = "something private",
            CreatedAtUtc = _clock.GetUtcNow()
        });
        _conversationRepo.Conversations.Add(theirs);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.SearchAsync(new SearchRequest("and then?", ConversationId: theirs.Id), default));

        Assert.Null(_aiClient.LastExpandRequest);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyGroups_WhenScopeResolvesToZeroSources()
    {
        var service = CreateService();
        _sourceRepo.ScopeResult = []; // 0 sources in scope

        var response = await service.SearchAsync(
            new SearchRequest("TD learning", NotebookIds: [Guid.NewGuid()]),
            default);

        Assert.Empty(response.Groups);
        Assert.NotEqual(Guid.Empty, response.ConversationId);
    }

    [Fact]
    public async Task SearchAsync_GroupsHitsBySource_AndSavesConversation()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        _searchRepo.Hits = [
            new SearchHit(
                chunkId,
                sourceId,
                "Reinforcement Learning: An Introduction",
                "application/pdf",
                120,
                "120",
                "6.1 TD Prediction",
                0,
                "Temporal-difference (TD) learning is a combination of Monte Carlo ideas and dynamic programming ideas.",
                "<mark>Temporal-difference</mark> (TD) learning...",
                0.032,
                0.15f,
                ["keyword", "semantic"])
        ];

        var response = await service.SearchAsync(new SearchRequest("TD learning"), default);

        var group = Assert.Single(response.Groups);
        Assert.Equal(sourceId, group.Source.Id);
        Assert.Equal("Reinforcement Learning: An Introduction", group.Source.Title);

        var passage = Assert.Single(group.Passages);
        Assert.Equal(1, passage.Number);
        Assert.Equal(chunkId, passage.ChunkId);
        Assert.Equal(120, passage.PageNumber);
        Assert.Equal("120", passage.PageLabel);
        Assert.Equal("<mark>Temporal-difference</mark> (TD) learning...", passage.Snippet);

        var conversation = await _conversationRepo.FindByIdForUserAsync(response.ConversationId, _currentUser.UserId, default);
        Assert.NotNull(conversation);
        var message = Assert.Single(conversation.Messages);
        Assert.Equal(MessageRole.User, message.Role);
        Assert.Equal("TD learning", message.Content);
        Assert.Single(message.Citations);
    }

    [Fact]
    public async Task SearchAsync_PreservesHtmlContentInSnippet_ForClientSanitization()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        _searchRepo.Hits = [
            new SearchHit(
                chunkId,
                sourceId,
                "Web Notes",
                "text/markdown",
                1,
                null,
                "Security",
                0,
                "Note containing <img src=x onerror=alert(1)> and <b>bold text</b>.",
                "Note containing <img src=x onerror=alert(1)> and <mark><b>bold</mark> text</b>.",
                0.045,
                0.1f,
                ["keyword"])
        ];

        var response = await service.SearchAsync(new SearchRequest("bold"), default);

        var group = Assert.Single(response.Groups);
        var passage = Assert.Single(group.Passages);
        Assert.Equal("Note containing <img src=x onerror=alert(1)> and <mark><b>bold</mark> text</b>.", passage.Snippet);
    }

    [Fact]
    public async Task SearchAsync_InterleavedSourceHits_PreservesGlobalCitationNumbersAcrossGroups()
    {
        var service = CreateService();
        var sourceA = Guid.NewGuid();
        var sourceB = Guid.NewGuid();

        var chunkA1 = Guid.NewGuid();
        var chunkB1 = Guid.NewGuid();
        var chunkA2 = Guid.NewGuid();

        // 3 hits interleaved: Source A (rank 1), Source B (rank 2), Source A (rank 3)
        _searchRepo.Hits = [
            new SearchHit(chunkA1, sourceA, "Source A", "text/plain", 1, null, null, 0, "Content A1", null, 0.05, 0.1f, ["semantic"]),
            new SearchHit(chunkB1, sourceB, "Source B", "text/plain", 1, null, null, 0, "Content B1", null, 0.04, 0.2f, ["semantic"]),
            new SearchHit(chunkA2, sourceA, "Source A", "text/plain", 2, null, null, 1, "Content A2", null, 0.03, 0.3f, ["semantic"])
        ];

        var response = await service.SearchAsync(new SearchRequest("test"), default);

        Assert.Equal(2, response.Groups.Count);

        var groupA = response.Groups.Single(g => g.Source.Id == sourceA);
        var groupB = response.Groups.Single(g => g.Source.Id == sourceB);

        // Group A passages should preserve their global citation ranks: 1 and 3
        Assert.Equal(2, groupA.Passages.Count);
        Assert.Equal(1, groupA.Passages[0].Number);
        Assert.Equal(chunkA1, groupA.Passages[0].ChunkId);
        Assert.Equal(3, groupA.Passages[1].Number);
        Assert.Equal(chunkA2, groupA.Passages[1].ChunkId);

        // Group B passage should preserve its global citation rank: 2
        var passageB = Assert.Single(groupB.Passages);
        Assert.Equal(2, passageB.Number);
        Assert.Equal(chunkB1, passageB.ChunkId);

        // Conversation citations should match exactly 1, 2, 3 in global rank order
        var conversation = await _conversationRepo.FindByIdForUserAsync(response.ConversationId, _currentUser.UserId, default);
        Assert.NotNull(conversation);
        var message = Assert.Single(conversation.Messages);
        var orderedCitations = message.Citations.OrderBy(c => c.Number).ToList();
        Assert.Equal(3, orderedCitations.Count);
        Assert.Equal(1, orderedCitations[0].Number);
        Assert.Equal(chunkA1, orderedCitations[0].SourceChunkId);
        Assert.Equal(2, orderedCitations[1].Number);
        Assert.Equal(chunkB1, orderedCitations[1].SourceChunkId);
        Assert.Equal(3, orderedCitations[2].Number);
        Assert.Equal(chunkA2, orderedCitations[2].SourceChunkId);
        Assert.Equal(sourceA, orderedCitations[2].SourceId);
        Assert.Equal(2, orderedCitations[2].PageNumber);
        Assert.Equal("Content A2", orderedCitations[2].Content);
    }

    [Fact]
    public async Task GenerateSummaryAsync_ReturnsCitedAnswer_AndStoresAssistantMessage()
    {
        var service = CreateService();
        var convId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var conv = new Conversation
        {
            Id = convId,
            UserId = _currentUser.UserId,
            Title = "TD learning",
            CreatedAtUtc = _clock.GetUtcNow(),
            UpdatedAtUtc = _clock.GetUtcNow()
        };

        var userMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "What is TD learning?",
            CreatedAtUtc = _clock.GetUtcNow()
        };
        userMessage.Citations.Add(new MessageCitation
        {
            MessageId = userMessage.Id,
            Number = 1,
            SourceChunkId = chunkId,
            SourceId = Guid.NewGuid(),
            PageNumber = 120,
            Content = "TD learning updates immediately."
        });
        conv.Messages.Add(userMessage);
        _conversationRepo.Conversations.Add(conv);

        _aiClient.AnswerToReturn = new AnswerResponse(
            "TD learning updates without waiting [1].",
            [1]);

        var result = await service.GenerateSummaryAsync(convId, default);

        Assert.Equal("TD learning updates without waiting [1].", result.Answer);
        Assert.Equal([1], result.CitedPassages);
        var sent = Assert.Single(_aiClient.LastAnswerRequest!.Passages);
        Assert.Equal("TD learning updates immediately.", sent.Content);
        Assert.Equal(120, sent.PageNumber);

        var messages = await _conversationRepo.GetMessagesForConversationAsync(convId, default);
        Assert.Equal(2, messages.Count);
        var assistantMsg = messages.Last();
        Assert.Equal(MessageRole.Assistant, assistantMsg.Role);
        var assistantCitation = Assert.Single(assistantMsg.Citations);
        Assert.Equal(1, assistantCitation.Number);
        Assert.Equal(120, assistantCitation.PageNumber);
        Assert.Equal("TD learning updates immediately.", assistantCitation.Content);
    }

    [Fact]
    public async Task StreamSummaryAsync_YieldsSseLines_AndSavesTheAnswerWithItsCitations()
    {
        var service = CreateService();
        var convId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        _conversationRepo.Conversations.Add(new Conversation
        {
            Id = convId,
            UserId = _currentUser.UserId,
            Title = "TD Learning Query",
            CreatedAtUtc = _clock.GetUtcNow(),
            UpdatedAtUtc = _clock.GetUtcNow()
        });

        var userMsgId = Guid.NewGuid();
        var userMsg = new Message
        {
            Id = userMsgId,
            ConversationId = convId,
            Role = MessageRole.User,
            Content = "What is TD learning?",
            CreatedAtUtc = _clock.GetUtcNow()
        };
        userMsg.Citations.Add(new MessageCitation
        {
            MessageId = userMsgId,
            Number = 1,
            SourceChunkId = chunkId,
            SourceId = Guid.NewGuid(),
            PageNumber = 42,
            Content = "Temporal difference learning is an approach to reinforcement learning."
        });
        _conversationRepo.Conversations.First(c => c.Id == convId).Messages.Add(userMsg);

        var events = new List<string>();
        await foreach (var evt in service.StreamSummaryAsync(convId, default))
        {
            events.Add(evt);
        }

        Assert.Equal(3, events.Count);
        Assert.Contains("TD learning", events[0]);
        Assert.Contains("bootstraps [1].", events[1]);
        Assert.Contains("\"done\": true", events[2]);

        var messages = await _conversationRepo.GetMessagesForConversationAsync(convId, default);
        Assert.Equal(2, messages.Count);
        Assert.Equal("TD learning bootstraps [1].", messages.Last().Content);
        var citation = Assert.Single(messages.Last().Citations);
        Assert.Equal(1, citation.Number);
        Assert.Equal(42, citation.PageNumber);
        Assert.Equal("Temporal difference learning is an approach to reinforcement learning.", citation.Content);
    }

    private sealed class InMemorySearchRepo : ISearchRepository
    {
        public List<SearchHit> Hits { get; set; } = [];
        public SearchQueryParameters? LastParameters { get; private set; }

        public Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQueryParameters parameters, CancellationToken cancellationToken)
        {
            LastParameters = parameters;
            return Task.FromResult<IReadOnlyList<SearchHit>>(Hits);
        }
    }

    private sealed class InMemorySourceRepo : ISourceRepository
    {
        public IReadOnlyList<Guid>? ScopeResult { get; set; }

        public Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken) =>
            Task.FromResult(ScopeResult);

        public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> SetDetailsForUserAsync(Guid id, Guid userId, string? license, string? sourceUrl, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class InMemoryConversationRepo : IConversationRepository
    {
        public List<Conversation> Conversations { get; } = [];

        public Task<Conversation?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Conversations.FirstOrDefault(c => c.Id == id && c.UserId == userId));

        public Task<IReadOnlyList<Conversation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>(Conversations.Where(c => c.UserId == userId).ToList());

        public Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
        {
            Conversations.Add(conversation);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AddMessageAsync(Message message, CancellationToken cancellationToken)
        {
            var conv = Conversations.FirstOrDefault(c => c.Id == message.ConversationId);
            conv?.Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task AddCitationsAsync(IEnumerable<MessageCitation> citations, CancellationToken cancellationToken)
        {
            foreach (var cite in citations)
            {
                var msg = Conversations.SelectMany(c => c.Messages).FirstOrDefault(m => m.Id == cite.MessageId);
                msg?.Citations.Add(cite);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Message>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken cancellationToken)
        {
            var conv = Conversations.FirstOrDefault(c => c.Id == conversationId);
            return Task.FromResult<IReadOnlyList<Message>>(conv?.Messages ?? []);
        }
    }

    private sealed class FakeAiClient : IAiClient
    {
        public AnswerResponse AnswerToReturn { get; set; } = new("", []);
        public AnswerRequest? LastAnswerRequest { get; private set; }

        public ExpandQueryRequest? LastExpandRequest { get; private set; }

        public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default)
        {
            LastExpandRequest = request;
            return Task.FromResult(new ExpandQueryResponse([new QueryItemDto(request.Question, [0.1f, 0.2f])]));
        }

        public Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default)
        {
            LastAnswerRequest = request;
            return Task.FromResult(AnswerToReturn);
        }

        public async IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return "data: {\"token\": \"TD learning \"}";
            yield return "data: {\"token\": \"bootstraps [1].\"}";
            yield return "data: {\"done\": true, \"cited\": [1]}";
        }

        public Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<SynthesizeResponse> SynthesizeAsync(SynthesizeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
