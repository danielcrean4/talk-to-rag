using Microsoft.EntityFrameworkCore;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class TagRepository(TalkToRagDbContext db) : ITagRepository
{
    public Task<Tag?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Tags.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Tag>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Tags
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsNameForUserAsync(string name, Guid userId, CancellationToken cancellationToken) =>
        db.Tags.AnyAsync(t => t.UserId == userId && t.Name == name, cancellationToken);

    public async Task AddAsync(Tag tag, CancellationToken cancellationToken)
    {
        db.Tags.Add(tag);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Tag tag, CancellationToken cancellationToken)
    {
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasTagAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken) =>
        db.SourceTags.AnyAsync(st => st.SourceId == sourceId && st.TagId == tagId, cancellationToken);

    public async Task AddTagToSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        db.SourceTags.Add(new SourceTag { SourceId = sourceId, TagId = tagId });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveTagFromSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        await db.SourceTags
            .Where(st => st.SourceId == sourceId && st.TagId == tagId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetTagIdsForSourceAsync(Guid sourceId, CancellationToken cancellationToken) =>
        await db.SourceTags
            .Where(st => st.SourceId == sourceId)
            .Select(st => st.TagId)
            .ToListAsync(cancellationToken);
}
