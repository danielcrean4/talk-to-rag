using Microsoft.EntityFrameworkCore;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class SourceRepository(TalkToRagDbContext db) : ISourceRepository
{
    public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Sources
            .Where(s => s.Id == id && s.UserId == userId)
            .Select(s => new SourceDto(
                s.Id,
                s.Title,
                s.FileName,
                s.ContentType,
                s.SizeBytes,
                s.Status.ToString(),
                s.FailureReason,
                s.Chunks.Count,
                s.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Sources
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);

    public Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await db.Sources
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SourceDto(
                s.Id,
                s.Title,
                s.FileName,
                s.ContentType,
                s.SizeBytes,
                s.Status.ToString(),
                s.FailureReason,
                s.Chunks.Count,
                s.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) =>
        await db.Sources
            .Where(s => s.Status == SourceStatus.Pending || s.Status == SourceStatus.Processing)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken)
    {
        if (notebookIds.Count == 0 && tagIds.Count == 0 && sourceIds.Count == 0)
        {
            return null; // All ready sources for user are allowed
        }

        var query = db.Sources
            .Where(s => s.UserId == userId && s.Status == SourceStatus.Ready);

        query = query.Where(s =>
            (sourceIds.Count > 0 && sourceIds.Contains(s.Id)) ||
            (notebookIds.Count > 0 && s.Notebooks.Any(n => notebookIds.Contains(n.NotebookId))) ||
            (tagIds.Count > 0 && s.Tags.Any(t => tagIds.Contains(t.TagId))));

        return await query.Select(s => s.Id).ToListAsync(cancellationToken);
    }

    public async Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SourceChunks.Where(c => c.SourceId == sourceId).ExecuteDeleteAsync(cancellationToken);
        if (chunks.Count > 0)
        {
            await db.SourceChunks.AddRangeAsync(chunks, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddAsync(Source source, CancellationToken cancellationToken)
    {
        db.Sources.Add(source);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Source source, CancellationToken cancellationToken)
    {
        db.Sources.Update(source);
        await db.SaveChangesAsync(cancellationToken);
    }
}
