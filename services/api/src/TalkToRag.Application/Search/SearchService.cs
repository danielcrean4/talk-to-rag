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
            var emptyConv = await GetOrCreateConversationAsync(userId, request.ConversationId, trimmedQuery, cancellationToken);
            var emptyUserMsg = new Message
            {
                Id = Guid.CreateVersion7(),
                ConversationId = emptyConv.Id,
                Role = MessageRole.User,
                Content = trimmedQuery,
                CreatedAtUtc = timeProvider.GetUtcNow()
            };
            await conversationRepository.AddMessageAsync(emptyUserMsg, cancellationToken);
            return new SearchResponse(emptyConv.Id, trimmedQuery, []);
        }

        var recentMessages = request.ConversationId.HasValue
            ? (await conversationRepository.GetMessagesForConversationAsync(request.ConversationId.Value, cancellationToken))
                .TakeLast(6)
                .Select(m => new RecentMessageDto(m.Role.ToString().ToLowerInvariant(), m.Content))
                .ToList()
            : [];

        var expandResponse = await aiClient.ExpandQueryAsync(
            new ExpandQueryRequest(trimmedQuery, recentMessages, request.Expand),
            cancellationToken);

        var embeddings = expandResponse.Queries.Select(q => q.Embedding).ToList();
        var maxDistance = options.Value.MaxCosineDistance;

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

        var userMessage = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = trimmedQuery,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        await conversationRepository.AddMessageAsync(userMessage, cancellationToken);

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
            SourceChunkId = h.Hit.ChunkId,
            Number = h.Number
        }).ToList();

        if (citations.Count > 0)
        {
            await conversationRepository.AddCitationsAsync(citations, cancellationToken);
        }

        return new SearchResponse(conversation.Id, trimmedQuery, groups);
    }

    public async Task<SummaryResponse> GenerateSummaryAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var conversation = await conversationRepository.FindByIdForUserAsync(conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            throw new NotFoundException($"Conversation '{conversationId}' not found.");
        }

        var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
        var lastUserMessage = messages.LastOrDefault(m => m.Role == MessageRole.User);
        if (lastUserMessage is null)
        {
            throw new RequestValidationException("No user query found to summarize.");
        }

        if (lastUserMessage.Citations.Count == 0)
        {
            return new SummaryResponse(conversationId, lastUserMessage.Content, "No passages were found for this query to summarize.", []);
        }

        var chunkIds = lastUserMessage.Citations.Select(c => c.SourceChunkId).ToList();
        var chunks = await conversationRepository.GetChunksByIdsAsync(chunkIds, cancellationToken);
        var chunkMap = chunks.ToDictionary(c => c.Id);

        var passageItems = lastUserMessage.Citations
            .OrderBy(c => c.Number)
            .Take(10)
            .Select(c =>
            {
                chunkMap.TryGetValue(c.SourceChunkId, out var chunk);
                return new PassageItemDto(
                    N: c.Number,
                    Content: chunk?.Content ?? "",
                    PageNumber: chunk?.PageNumber,
                    PageLabel: chunk?.PageLabel,
                    Section: chunk?.Section);
            })
            .Where(p => !string.IsNullOrEmpty(p.Content))
            .ToList();

        var answerResponse = await aiClient.GenerateAnswerAsync(
            new AnswerRequest(lastUserMessage.Content, passageItems),
            cancellationToken);

        var assistantMessage = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversationId,
            Role = MessageRole.Assistant,
            Content = answerResponse.Answer,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        await conversationRepository.AddMessageAsync(assistantMessage, cancellationToken);

        var citationsMap = lastUserMessage.Citations.ToDictionary(c => c.Number);
        var citations = new List<MessageCitation>();
        foreach (var num in answerResponse.Cited)
        {
            if (citationsMap.TryGetValue(num, out var match))
            {
                citations.Add(new MessageCitation
                {
                    MessageId = assistantMessage.Id,
                    SourceChunkId = match.SourceChunkId,
                    Number = num
                });
            }
        }

        if (citations.Count > 0)
        {
            await conversationRepository.AddCitationsAsync(citations, cancellationToken);
        }

        conversation.UpdatedAtUtc = timeProvider.GetUtcNow();
        await conversationRepository.UpdateAsync(conversation, cancellationToken);

        return new SummaryResponse(conversationId, lastUserMessage.Content, answerResponse.Answer, answerResponse.Cited);
    }

    public async IAsyncEnumerable<string> StreamSummaryAsync(Guid conversationId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var conversation = await conversationRepository.FindByIdForUserAsync(conversationId, userId, cancellationToken);
        if (conversation is null)
        {
            throw new NotFoundException($"Conversation '{conversationId}' not found.");
        }

        var messages = await conversationRepository.GetMessagesForConversationAsync(conversationId, cancellationToken);
        var lastUserMessage = messages.LastOrDefault(m => m.Role == MessageRole.User);
        if (lastUserMessage is null)
        {
            throw new RequestValidationException("No user query found to summarize.");
        }

        if (lastUserMessage.Citations.Count == 0)
        {
            yield return "data: {\"token\": \"No passages were found for this query to summarize.\"}";
            yield return "data: {\"done\": true, \"cited\": []}";
            yield break;
        }

        var chunkIds = lastUserMessage.Citations.Select(c => c.SourceChunkId).ToList();
        var chunks = await conversationRepository.GetChunksByIdsAsync(chunkIds, cancellationToken);
        var chunkMap = chunks.ToDictionary(c => c.Id);

        var passageItems = lastUserMessage.Citations
            .OrderBy(c => c.Number)
            .Take(10)
            .Select(c =>
            {
                chunkMap.TryGetValue(c.SourceChunkId, out var chunk);
                return new PassageItemDto(
                    N: c.Number,
                    Content: chunk?.Content ?? "",
                    PageNumber: chunk?.PageNumber,
                    PageLabel: chunk?.PageLabel,
                    Section: chunk?.Section);
            })
            .Where(p => !string.IsNullOrEmpty(p.Content))
            .ToList();

        var textBuffer = new System.Text.StringBuilder();

        await foreach (var line in aiClient.StreamAnswerAsync(
            new AnswerRequest(lastUserMessage.Content, passageItems),
            cancellationToken))
        {
            yield return line;

            if (line.StartsWith("data: ") && !line.Contains("\"done\": true"))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(line[6..]);
                    if (doc.RootElement.TryGetProperty("token", out var tokenProp))
                    {
                        textBuffer.Append(tokenProp.GetString());
                    }
                }
                catch { }
            }
        }

        if (textBuffer.Length > 0)
        {
            var assistantMessage = new Message
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversationId,
                Role = MessageRole.Assistant,
                Content = textBuffer.ToString().Trim(),
                CreatedAtUtc = timeProvider.GetUtcNow()
            };
            await conversationRepository.AddMessageAsync(assistantMessage, cancellationToken);
        }
    }

    private async Task<Conversation> GetOrCreateConversationAsync(Guid userId, Guid? conversationId, string query, CancellationToken cancellationToken)
    {
        if (conversationId.HasValue && conversationId.Value != Guid.Empty)
        {
            var existing = await conversationRepository.FindByIdForUserAsync(conversationId.Value, userId, cancellationToken);
            if (existing is null)
            {
                throw new NotFoundException($"Conversation '{conversationId}' not found.");
            }
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
