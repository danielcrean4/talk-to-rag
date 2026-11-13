using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Application.Conversations;

public sealed class ConversationService(
    IConversationRepository conversationRepository,
    ICurrentUser currentUser) : IConversationService
{
    public async Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(CancellationToken cancellationToken)
    {
        var conversations = await conversationRepository.ListForUserAsync(currentUser.UserId, cancellationToken);
        return conversations.Select(c => new ConversationSummaryDto(
            c.Id,
            c.Title,
            c.CreatedAtUtc,
            c.UpdatedAtUtc)).ToList();
    }

    public async Task<ConversationDetailsDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var conversation = await conversationRepository.FindByIdForUserAsync(id, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Conversation '{id}' not found.");

        var messages = await conversationRepository.GetMessagesForConversationAsync(id, cancellationToken);
        var messageDtos = messages.Select(m => new MessageDto(
            m.Id,
            m.Role.ToString().ToLowerInvariant(),
            m.Content,
            m.CreatedAtUtc,
            m.Citations.OrderBy(c => c.Number).Select(c => new MessageCitationDto(
                c.Number,
                c.SourceChunkId,
                c.SourceId,
                c.PageNumber,
                c.PageLabel,
                c.Section,
                c.Content)).ToList())).ToList();

        return new ConversationDetailsDto(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAtUtc,
            conversation.UpdatedAtUtc,
            messageDtos);
    }
}
