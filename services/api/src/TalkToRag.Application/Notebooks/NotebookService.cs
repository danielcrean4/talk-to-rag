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
        var exists = await notebookRepository.ExistsNameForUserAsync(trimmedName, userId, null, cancellationToken);
        if (exists)
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
        var userId = currentUser.UserId;
        var notebooks = await notebookRepository.ListForUserAsync(userId, cancellationToken);
        return notebooks.Select(n => new NotebookDto(
            n.Id,
            n.Name,
            n.Description,
            n.Sources.Count,
            n.CreatedAtUtc)).ToList();
    }

    public async Task<NotebookDetailsDto> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var notebook = await notebookRepository.FindByIdForUserAsync(id, userId, cancellationToken);
        if (notebook is null)
        {
            throw new NotFoundException($"Notebook '{id}' not found.");
        }

        var sourceIds = await notebookRepository.GetSourceIdsForNotebookAsync(id, cancellationToken);
        return new NotebookDetailsDto(notebook.Id, notebook.Name, notebook.Description, sourceIds, notebook.CreatedAtUtc);
    }

    public async Task<NotebookDto> UpdateAsync(Guid id, UpdateNotebookRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var notebook = await notebookRepository.FindByIdForUserAsync(id, userId, cancellationToken);
        if (notebook is null)
        {
            throw new NotFoundException($"Notebook '{id}' not found.");
        }

        if (request.Name is not null)
        {
            var trimmedName = request.Name.Trim();
            if (string.IsNullOrWhiteSpace(trimmedName))
            {
                throw new RequestValidationException("Notebook name cannot be empty.");
            }

            if (!string.Equals(notebook.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
            {
                var clash = await notebookRepository.ExistsNameForUserAsync(trimmedName, userId, id, cancellationToken);
                if (clash)
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
        var userId = currentUser.UserId;
        var notebook = await notebookRepository.FindByIdForUserAsync(id, userId, cancellationToken);
        if (notebook is null)
        {
            throw new NotFoundException($"Notebook '{id}' not found.");
        }

        await notebookRepository.DeleteAsync(notebook, cancellationToken);
    }

    public async Task AddSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var notebook = await notebookRepository.FindByIdForUserAsync(notebookId, userId, cancellationToken);
        if (notebook is null)
        {
            throw new NotFoundException($"Notebook '{notebookId}' not found.");
        }

        var source = await sourceRepository.FindEntityByIdForUserAsync(sourceId, userId, cancellationToken);
        if (source is null)
        {
            throw new NotFoundException($"Source '{sourceId}' not found.");
        }

        var alreadyIn = await notebookRepository.IsSourceInNotebookAsync(notebookId, sourceId, cancellationToken);
        if (!alreadyIn)
        {
            await notebookRepository.AddSourceAsync(notebookId, sourceId, cancellationToken);
        }
    }

    public async Task RemoveSourceAsync(Guid notebookId, Guid sourceId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var notebook = await notebookRepository.FindByIdForUserAsync(notebookId, userId, cancellationToken);
        if (notebook is null)
        {
            throw new NotFoundException($"Notebook '{notebookId}' not found.");
        }

        await notebookRepository.RemoveSourceAsync(notebookId, sourceId, cancellationToken);
    }
}
