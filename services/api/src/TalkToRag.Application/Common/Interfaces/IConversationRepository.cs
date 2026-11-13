using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public interface IConversationRepository
{
    Task<Conversation?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Conversation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task AddAsync(Conversation conversation, CancellationToken cancellationToken);
    Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken);
    Task AddMessageAsync(Message message, CancellationToken cancellationToken);
    Task AddCitationsAsync(IEnumerable<MessageCitation> citations, CancellationToken cancellationToken);
    Task<IReadOnlyList<Message>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken cancellationToken);
}
