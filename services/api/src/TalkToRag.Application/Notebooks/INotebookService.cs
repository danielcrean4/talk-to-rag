namespace TalkToRag.Application.Notebooks;

public interface INotebookService
{
    Task<NotebookDto> CreateAsync(CreateNotebookRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotebookDto>> ListAsync(CancellationToken cancellationToken);
    Task<NotebookDetailsDto> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<NotebookDto> UpdateAsync(Guid id, UpdateNotebookRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task AddSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken);
    Task RemoveSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken);
}
