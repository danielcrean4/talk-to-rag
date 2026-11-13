using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Notebooks;

public sealed class NotebookService(
    INotebookRepository notebookRepository,
    ISourceRepository sourceRepository,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : INotebookService
{
    public async Task<NotebookDto> CreateAsync(CreateNotebookRequest request, CancellationToken cancellationToken)
    {
        var trimmedName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new RequestValidationException("Notebook name cannot be empty.");
        }

        var userId = currentUser.UserId;
        if (await notebookRepository.ExistsNameForUserAsync(trimmedName, userId, null, cancellationToken))
        {
            throw new ConflictException($"Notebook '{trimmedName}' already exists.");
        }

        var notebook = new Notebook
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = trimmedName,
            Description = request.Description?.Trim(),
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        await notebookRepository.AddAsync(notebook, cancellationToken);

        return new NotebookDto(notebook.Id, notebook.Name, notebook.Description, 0, notebook.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<NotebookDto>> ListAsync(CancellationToken cancellationToken)
    {
        var notebooks = await notebookRepository.ListForUserAsync(currentUser.UserId, cancellationToken);
        return notebooks.Select(n => new NotebookDto(
            n.Id,
            n.Name,
            n.Description,
            n.Sources.Count,
            n.CreatedAtUtc)).ToList();
    }

    public async Task<NotebookDetailsDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var notebook = await RequireNotebookAsync(id, cancellationToken);
        var sourceIds = await notebookRepository.GetSourceIdsForNotebookAsync(id, cancellationToken);
        return new NotebookDetailsDto(notebook.Id, notebook.Name, notebook.Description, sourceIds, notebook.CreatedAtUtc);
    }

    public async Task<NotebookDto> UpdateAsync(Guid id, UpdateNotebookRequest request, CancellationToken cancellationToken)
    {
        var notebook = await RequireNotebookAsync(id, cancellationToken);
        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                throw new RequestValidationException("Notebook name cannot be empty.");
            }

            if (!string.Equals(notebook.Name, trimmedName, StringComparison.Ordinal))
            {
                if (await notebookRepository.ExistsNameForUserAsync(trimmedName, currentUser.UserId, id, cancellationToken))
                {
                    throw new ConflictException($"Notebook '{trimmedName}' already exists.");
                }
                notebook.Name = trimmedName;
            }
        }

        if (request.Description is not null)
        {
            notebook.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        }

        await notebookRepository.UpdateAsync(notebook, cancellationToken);
        var sourceIds = await notebookRepository.GetSourceIdsForNotebookAsync(id, cancellationToken);

        return new NotebookDto(notebook.Id, notebook.Name, notebook.Description, sourceIds.Count, notebook.CreatedAtUtc);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var notebook = await RequireNotebookAsync(id, cancellationToken);
        await notebookRepository.DeleteAsync(notebook, cancellationToken);
    }

    public async Task AddSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
    {
        await RequireNotebookAsync(notebookId, cancellationToken);
        if (await sourceRepository.FindEntityByIdForUserAsync(sourceId, currentUser.UserId, cancellationToken) is null)
        {
            throw new NotFoundException($"Source '{sourceId}' not found.");
        }

        if (!await notebookRepository.IsSourceInNotebookAsync(notebookId, sourceId, cancellationToken))
        {
            await notebookRepository.AddSourceAsync(notebookId, sourceId, cancellationToken);
        }
    }

    public async Task RemoveSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
    {
        await RequireNotebookAsync(notebookId, cancellationToken);
        await notebookRepository.RemoveSourceAsync(notebookId, sourceId, cancellationToken);
    }

    private async Task<Notebook> RequireNotebookAsync(Guid id, CancellationToken cancellationToken) =>
        await notebookRepository.FindByIdForUserAsync(id, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Notebook '{id}' not found.");
}
