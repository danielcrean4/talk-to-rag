using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public interface INotebookRepository
{
    Task<Notebook?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Notebook>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> ExistsNameForUserAsync(string name, Guid userId, Guid? excludeId, CancellationToken cancellationToken);
    Task AddAsync(Notebook notebook, CancellationToken cancellationToken);
    Task UpdateAsync(Notebook notebook, CancellationToken cancellationToken);
    Task DeleteAsync(Notebook notebook, CancellationToken cancellationToken);
    Task<bool> IsSourceInNotebookAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken);
    Task AddSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken);
    Task RemoveSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetSourceIdsForNotebookAsync(Guid notebookId, CancellationToken cancellationToken);
}
