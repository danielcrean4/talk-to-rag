using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.Application.Search;

public sealed class SearchService(
    ISearchRepository searchRepository,
    ISourceRepository sourceRepository,
    IConversationRepository conversationRepository,
    IAiClient aiClient,
    ICurrentUser currentUser,
    IOptions<SearchOptions> options,
    TimeProvider timeProvider) : ISearchService
{
    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var trimmedQuery = request.Query?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            throw new RequestValidationException("Search query cannot be empty.");
        }

        var userId = currentUser.UserId;
        var limit = Math.Clamp(request.Limit, 1, 50);
        var mode = request.Mode?.ToLowerInvariant() switch
        {
            "keyword" => "keyword",
            "semantic" => "semantic",
            _ => "hybrid"
        };

        var allowedSourceIds = await sourceRepository.FindReadySourceIdsForScopeAsync(
            userId,
            request.NotebookIds ?? [],
            request.TagIds ?? [],
            request.SourceIds ?? [],
            cancellationToken);

        if (allowedSourceIds is not null && allowedSourceIds.Count == 0)
        {
            var emptyConversation = await GetOrCreateConversationAsync(userId, request.ConversationId, trimmedQuery, cancellationToken);
            await AddUserMessageAsync(emptyConversation, trimmedQuery, cancellationToken);
            return new SearchResponse(emptyConversation.Id, trimmedQuery, []);
        }

        // Checked before the earlier messages are read: they are sent to the AI service as context.
        if (request.ConversationId is { } followedUp && followedUp != Guid.Empty
            && await conversationRepository.FindByIdForUserAsync(followedUp, userId, cancellationToken) is null)
        {
            throw new NotFoundException($"Conversation '{followedUp}' not found.");
        }

        var recentMessages = request.ConversationId is { } conversationId && conversationId != Guid.Empty
            ? (await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken))
                .TakeLast(6)
                .Select(m => new RecentMessageDto(m.Role.ToString().ToLowerInvariant(), m.Content))
                .ToList()
            : [];

        var expandResponse = await aiClient.ExpandQueryAsync(
            new ExpandQueryRequest(trimmedQuery, recentMessages, request.Expand),
            cancellationToken);

        var embeddings = expandResponse.Queries.Select(q => q.Embedding).ToList();
        var maxDistance = request.MaxCosineDistance is { } requested
            ? Math.Clamp(requested, 0.05f, 1.5f)
            : options.Value.MaxCosineDistance;

        var searchHits = await searchRepository.SearchAsync(
            new SearchQueryParameters(
                UserId: userId,
                QueryText: trimmedQuery,
                QueryEmbeddings: embeddings,
                AllowedSourceIds: allowedSourceIds,
                Mode: mode,
                MaxCosineDistance: maxDistance,
                Limit: limit),
            cancellationToken);

        var conversation = await GetOrCreateConversationAsync(userId, request.ConversationId, trimmedQuery, cancellationToken);
        var userMessage = await AddUserMessageAsync(conversation, trimmedQuery, cancellationToken);

        var numberedHits = searchHits.Select((hit, index) => (Hit: hit, Number: index + 1)).ToList();

        var groups = numberedHits
            .GroupBy(h => (h.Hit.SourceId, h.Hit.SourceTitle, h.Hit.SourceContentType))
            .Select(g => new SourcePassageGroupDto(
                new SourceSummaryDto(g.Key.SourceId, g.Key.SourceTitle, g.Key.SourceContentType),
                g.Select(p => new PassageHitDto(
                    p.Number,
                    p.Hit.ChunkId,
                    p.Hit.PageNumber,
                    p.Hit.PageLabel,
                    p.Hit.Section,
                    p.Hit.Headline ?? CutAtWordBoundary(p.Hit.Content, 240),
                    p.Hit.Score,
                    p.Hit.MatchedBy)).ToList()))
            .ToList();

        var citations = numberedHits.Select(h => new MessageCitation
        {
            MessageId = userMessage.Id,
            Number = h.Number,
            SourceChunkId = h.Hit.ChunkId,
            SourceId = h.Hit.SourceId,
            PageNumber = h.Hit.PageNumber,
            PageLabel = h.Hit.PageLabel,
            Section = h.Hit.Section,
            ChunkIndex = h.Hit.ChunkIndex,
            Content = h.Hit.Content
        }).ToList();

        if (citations.Count > 0)
        {
            await conversationRepository.AddCitationsAsync(citations, cancellationToken);
        }

        return new SearchResponse(conversation.Id, trimmedQuery, groups);
    }

    public async Task<SummaryResponse> GenerateSummaryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var (conversation, lastUserMessage) = await LoadLastQuestionAsync(conversationId, cancellationToken);

        if (lastUserMessage.Citations.Count == 0)
        {
            return new SummaryResponse(conversationId, lastUserMessage.Content, "No passages were found for this query to summarize.", []);
        }

        var answerResponse = await aiClient.GenerateAnswerAsync(
            new AnswerRequest(lastUserMessage.Content, PassagesToSummarize(lastUserMessage)),
            cancellationToken);

        await SaveSummaryAsync(conversation, lastUserMessage, answerResponse.Answer, answerResponse.Cited, cancellationToken);

        return new SummaryResponse(conversationId, lastUserMessage.Content, answerResponse.Answer, answerResponse.Cited);
    }

    public async IAsyncEnumerable<string> StreamSummaryAsync(Guid conversationId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (conversation, lastUserMessage) = await LoadLastQuestionAsync(conversationId, cancellationToken);

        if (lastUserMessage.Citations.Count == 0)
        {
            yield return "data: {\"token\": \"No passages were found for this query to summarize.\"}";
            yield return "data: {\"done\": true, \"cited\": []}";
            yield break;
        }

        var textBuffer = new StringBuilder();
        IReadOnlyList<int> cited = [];

        await foreach (var line in aiClient.StreamAnswerAsync(
            new AnswerRequest(lastUserMessage.Content, PassagesToSummarize(lastUserMessage)),
            cancellationToken))
        {
            // The trace is for the audit log, which the HTTP client's handler has already captured.
            if (line.StartsWith("data: {\"trace\"", StringComparison.Ordinal))
            {
                continue;
            }

            yield return line;

            if (!line.StartsWith("data: "))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line[6..]);
                if (doc.RootElement.TryGetProperty("token", out var tokenProp))
                {
                    textBuffer.Append(tokenProp.GetString());
                }
                else if (doc.RootElement.TryGetProperty("cited", out var citedProp))
                {
                    cited = citedProp.EnumerateArray().Select(n => n.GetInt32()).ToList();
                }
            }
            catch (JsonException) { }
        }

        if (textBuffer.Length > 0)
        {
            await SaveSummaryAsync(conversation, lastUserMessage, textBuffer.ToString().Trim(), cited, cancellationToken);
        }
    }

    private async Task<(Conversation Conversation, Message LastUserMessage)> LoadLastQuestionAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversationRepository.FindByIdForUserAsync(conversationId, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Conversation '{conversationId}' not found.");
        var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
        var lastUserMessage = messages.LastOrDefault(m => m.Role == MessageRole.User)
            ?? throw new RequestValidationException("No user query found to summarize.");
        return (conversation, lastUserMessage);
    }

    private async Task<Message> AddUserMessageAsync(Conversation conversation, string query, CancellationToken cancellationToken)
    {
        var message = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = query,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        await conversationRepository.AddMessageAsync(message, cancellationToken);
        return message;
    }

    private async Task SaveSummaryAsync(Conversation conversation, Message userMessage, string answer, IReadOnlyList<int> cited, CancellationToken cancellationToken)
    {
        var assistantMessage = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = answer,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        await conversationRepository.AddMessageAsync(assistantMessage, cancellationToken);

        var passages = userMessage.Citations.ToDictionary(c => c.Number);
        var citations = cited
            .Distinct()
            .Where(passages.ContainsKey)
            .Select(number => passages[number])
            .Select(passage => new MessageCitation
            {
                MessageId = assistantMessage.Id,
                Number = passage.Number,
                SourceChunkId = passage.SourceChunkId,
                SourceId = passage.SourceId,
                PageNumber = passage.PageNumber,
                PageLabel = passage.PageLabel,
                Section = passage.Section,
                ChunkIndex = passage.ChunkIndex,
                Content = passage.Content
            })
            .ToList();

        if (citations.Count > 0)
        {
            await conversationRepository.AddCitationsAsync(citations, cancellationToken);
        }

        conversation.UpdatedAtUtc = timeProvider.GetUtcNow();
        await conversationRepository.UpdateAsync(conversation, cancellationToken);
    }

    private async Task<Conversation> GetOrCreateConversationAsync(Guid userId, Guid? conversationId, string query, CancellationToken cancellationToken)
    {
        if (conversationId.HasValue && conversationId.Value != Guid.Empty)
        {
            var existing = await conversationRepository.FindByIdForUserAsync(conversationId.Value, userId, cancellationToken)
                ?? throw new NotFoundException($"Conversation '{conversationId}' not found.");
            existing.UpdatedAtUtc = timeProvider.GetUtcNow();
            await conversationRepository.UpdateAsync(existing, cancellationToken);
            return existing;
        }

        var title = query.Length > 80 ? query[..80].Trim() + "..." : query;
        var now = timeProvider.GetUtcNow();
        var conversation = new Conversation
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Title = title,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        await conversationRepository.AddAsync(conversation, cancellationToken);
        return conversation;
    }

    private static List<PassageItemDto> PassagesToSummarize(Message userMessage) =>
        userMessage.Citations
            .OrderBy(c => c.Number)
            .Take(10)
            .Where(c => !string.IsNullOrEmpty(c.Content))
            .Select(c => new PassageItemDto(
                N: c.Number,
                Content: c.Content,
                PageNumber: c.PageNumber,
                PageLabel: c.PageLabel,
                Section: c.Section))
            .ToList();

    private static string CutAtWordBoundary(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxChars)
        {
            return text.Trim();
        }

        var cutoff = text.LastIndexOf(' ', maxChars);
        if (cutoff > 0)
        {
            return text[..cutoff].Trim() + "...";
        }

        return text[..maxChars].Trim() + "...";
    }
}
