using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class SourceRepository(TalkToRagDbContext db) : ISourceRepository
{
    private static readonly Expression<Func<Source, SourceDto>> ToDto = s => new SourceDto(
        s.Id,
        s.Title,
        s.FileName,
        s.ContentType,
        s.SizeBytes,
        s.Status.ToString(),
        s.FailureReason,
        s.Chunks.Count,
        s.CreatedAtUtc,
        s.Tags.Select(t => t.TagId).ToList(),
        s.License,
        s.SourceUrl);

    public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Sources
            .Where(s => s.Id == id && s.UserId == userId)
            .Select(ToDto)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Sources
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);

    public Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Sources
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(ToDto)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) =>
        await db.Sources
            .Where(s => s.Status == SourceStatus.Pending || s.Status == SourceStatus.Processing)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken)
    {
        if (notebookIds.Count == 0 && tagIds.Count == 0 && sourceIds.Count == 0)
        {
            // No scope: every Ready source of the user may be searched.
            return null;
        }

        return await db.Sources
            .Where(s => s.UserId == userId && s.Status == SourceStatus.Ready)
            // Each kind of scope that is given narrows the search; within a kind, any match is enough.
            .Where(s =>
                (sourceIds.Count == 0 || sourceIds.Contains(s.Id)) &&
                (notebookIds.Count == 0 || s.Notebooks.Any(n => notebookIds.Contains(n.NotebookId))) &&
                (tagIds.Count == 0 || s.Tags.Any(t => tagIds.Contains(t.TagId))))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SourceChunks.Where(c => c.SourceId == sourceId).ExecuteDeleteAsync(cancellationToken);
        if (chunks.Count > 0)
        {
            await db.SourceChunks.AddRangeAsync(chunks, cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Rejected chunks must not stay tracked, or the caller's next save (marking the source Failed) fails too.
                foreach (var chunk in chunks)
                {
                    db.Entry(chunk).State = EntityState.Detached;
                }

                throw;
            }
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

    public async Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken) =>
        await db.Sources
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.Status, status)
                .SetProperty(s => s.FailureReason, failureReason), cancellationToken) > 0;

    public async Task<bool> SetDetailsForUserAsync(Guid id, Guid userId, string? license, string? sourceUrl, CancellationToken cancellationToken) =>
        await db.Sources
            .Where(s => s.Id == id && s.UserId == userId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.License, license)
                .SetProperty(s => s.SourceUrl, sourceUrl), cancellationToken) > 0;

    public Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken) =>
        db.SourceChunks.AnyAsync(c => c.SourceId == id, cancellationToken);

    public async Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        await db.Sources
            .Where(s => s.Id == id && s.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken) > 0;
}
