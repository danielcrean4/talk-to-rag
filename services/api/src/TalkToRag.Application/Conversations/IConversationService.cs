namespace TalkToRag.Application.Conversations;

public interface IConversationService
{
    Task<IReadOnlyList<ConversationSummaryDto>> ListAsync(CancellationToken cancellationToken);
    Task<ConversationDetailsDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}
