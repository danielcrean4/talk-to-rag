using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Application.Conversations;

public sealed class ConversationService(
    IConversationRepository conversationRepository,
    ICurrentUser currentUser) : IConversationService
{
    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var conversations = await conversationRepository.ListForUserAsync(userId, cancellationToken);
        return conversations.Select(c => new ConversationSummaryDto(
            c.Id,
            c.Title,
            c.CreatedAtUtc,
            c.UpdatedAtUtc)).ToList();
    }

    public async Task<ConversationDetailsDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var conversation = await conversationRepository.FindByIdForUserAsync(id, userId, cancellationToken);
        if (conversation is null)
        {
            throw new NotFoundException($"Conversation '{id}' not found.");
        }

        var messages = await conversationRepository.GetMessagesForConversationAsync(id, cancellationToken);
        var allChunkIds = messages.SelectMany(m => m.Citations.Select(c => c.SourceChunkId)).Distinct().ToList();
        var chunks = await conversationRepository.GetChunksByIdsAsync(allChunkIds, cancellationToken);
        var chunkMap = chunks.ToDictionary(c => c.Id);

        var messageDtos = messages.Select(m => new MessageDto(
            m.Id,
            m.Role.ToString().ToLowerInvariant(),
            m.Content,
            m.CreatedAtUtc,
            m.Citations.OrderBy(c => c.Number).Select(c =>
            {
                chunkMap.TryGetValue(c.SourceChunkId, out var chunk);
                return new MessageCitationDto(
                    c.Number,
                    c.SourceChunkId,
                    chunk?.PageNumber,
                    chunk?.PageLabel,
                    chunk?.Section,
                    chunk?.Content ?? "");
            }).ToList())).ToList();

        return new ConversationDetailsDto(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAtUtc,
            conversation.UpdatedAtUtc,
            messageDtos);
    }
}
