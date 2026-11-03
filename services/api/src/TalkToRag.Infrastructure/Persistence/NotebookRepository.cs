using Microsoft.EntityFrameworkCore;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class NotebookRepository(TalkToRagDbContext db) : INotebookRepository
{
    public Task<Notebook?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Notebooks
            .Include(n => n.Sources)
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Notebook>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Notebooks
            .Include(n => n.Sources)
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.Name)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsNameForUserAsync(string name, Guid userId, Guid? excludeId, CancellationToken cancellationToken) =>
        db.Notebooks.AnyAsync(n => n.UserId == userId && n.Name == name && (!excludeId.HasValue || n.Id != excludeId.Value), cancellationToken);

    public async Task AddAsync(Notebook notebook, CancellationToken cancellationToken)
    {
        db.Notebooks.Add(notebook);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Notebook notebook, CancellationToken cancellationToken)
    {
        db.Notebooks.Update(notebook);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Notebook notebook, CancellationToken cancellationToken)
    {
        db.Notebooks.Remove(notebook);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsSourceInNotebookAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken) =>
        db.NotebookSources.AnyAsync(ns => ns.NotebookId == notebookId && ns.SourceId == sourceId, cancellationToken);

    public async Task AddSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
    {
        db.NotebookSources.Add(new NotebookSource { NotebookId = notebookId, SourceId = sourceId });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
    {
        await db.NotebookSources
            .Where(ns => ns.NotebookId == notebookId && ns.SourceId == sourceId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetSourceIdsForNotebookAsync(Guid notebookId, CancellationToken cancellationToken) =>
        await db.NotebookSources
            .Where(ns => ns.NotebookId == notebookId)
            .Select(ns => ns.SourceId)
            .ToListAsync(cancellationToken);
}
