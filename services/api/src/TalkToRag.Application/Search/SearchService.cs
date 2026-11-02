using Microsoft.Extensions.Configuration;
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
    IConfiguration configuration,
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

        // Resolve scope
        var allowedSourceIds = await sourceRepository.FindReadySourceIdsForScopeAsync(
            userId,
            request.NotebookIds ?? [],
            request.TagIds ?? [],
            request.SourceIds ?? [],
            cancellationToken);

        // Get or create conversation
        var conversation = await GetOrCreateConversationAsync(userId, request.ConversationId, trimmedQuery, cancellationToken);

        // Save user query message
        var userMessage = new Message
        {
            Id = Guid.CreateVersion7(),
            ConversationId = conversation.Id,
            Role = MessageRole.User,
            Content = trimmedQuery,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        await conversationRepository.AddMessageAsync(userMessage, cancellationToken);

        // If specific scope was requested and matched 0 sources, return empty results
        if (allowedSourceIds is not null && allowedSourceIds.Count == 0)
        {
            return new SearchResponse(conversation.Id, trimmedQuery, []);
        }

        // Get query embedding(s) from AI service
        var recentMessages = (await conversationRepository.GetMessagesForConversationAsync(conversation.Id, cancellationToken))
            .TakeLast(6)
            .Select(m => new RecentMessageDto(m.Role.ToString().ToLowerInvariant(), m.Content))
            .ToList();

        var expandResponse = await aiClient.ExpandQueryAsync(
            new ExpandQueryRequest(trimmedQuery, recentMessages, request.Expand),
            cancellationToken);

        var embeddings = expandResponse.Queries.Select(q => q.Embedding).ToList();
        float? maxDistance = float.TryParse(configuration["Search:MaxCosineDistance"], out var parsedDist)
            ? parsedDist
            : 0.6f;

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

        // Group hits by source
        var groups = searchHits
            .GroupBy(h => (h.SourceId, h.SourceTitle, h.SourceContentType))
            .Select(g => new SourcePassageGroupDto(
                new SourceSummaryDto(g.Key.SourceId, g.Key.SourceTitle, g.Key.SourceContentType),
                g.Select(p => new PassageHitDto(
                    p.ChunkId,
                    p.PageNumber,
                    p.PageLabel,
                    p.Section,
                    p.Headline ?? CutAtWordBoundary(p.Content, 240),
                    p.Score,
                    p.MatchedBy)).ToList()))
            .ToList();

        // Save search hits as citations for this search turn
        var citations = searchHits.Select((hit, index) => new MessageCitation
        {
            MessageId = userMessage.Id,
            SourceChunkId = hit.ChunkId,
            Number = index + 1
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

        var citations = answerResponse.Cited.Select(num =>
        {
            var match = lastUserMessage.Citations.FirstOrDefault(c => c.Number == num);
            return match != null ? new MessageCitation { MessageId = assistantMessage.Id, SourceChunkId = match.SourceChunkId, Number = num } : null;
        }).Where(c => c != null).Select(c => c!).ToList();

        if (citations.Count > 0)
        {
            await conversationRepository.AddCitationsAsync(citations, cancellationToken);
        }

        conversation.UpdatedAtUtc = timeProvider.GetUtcNow();
        await conversationRepository.UpdateAsync(conversation, cancellationToken);

        return new SummaryResponse(conversationId, lastUserMessage.Content, answerResponse.Answer, answerResponse.Cited);
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
