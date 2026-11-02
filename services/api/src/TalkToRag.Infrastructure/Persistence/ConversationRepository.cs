using Microsoft.EntityFrameworkCore;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class ConversationRepository(TalkToRagDbContext db) : IConversationRepository
{
    public Task<Conversation?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Conversations
            .Include(c => c.Notebooks)
            .Include(c => c.Tags)
            .Include(c => c.Messages)
                .ThenInclude(m => m.Citations)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Conversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        db.Conversations.Update(conversation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddMessageAsync(Message message, CancellationToken cancellationToken)
    {
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddCitationsAsync(IEnumerable<MessageCitation> citations, CancellationToken cancellationToken)
    {
        db.MessageCitations.AddRange(citations);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Message>> GetMessagesForConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        await db.Messages
            .Include(m => m.Citations)
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<SourceChunk>> GetChunksByIdsAsync(IEnumerable<Guid> chunkIds, CancellationToken cancellationToken)
    {
        var ids = chunkIds.ToList();
        return await db.SourceChunks
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
    }
}
