using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Tags;

public sealed class TagService(
    ITagRepository tagRepository,
    ISourceRepository sourceRepository,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : ITagService
{
    public async Task<TagDto> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken)
    {
        var trimmedName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new RequestValidationException("Tag name cannot be empty.");
        }

        var userId = currentUser.UserId;
        var exists = await tagRepository.ExistsNameForUserAsync(trimmedName, userId, cancellationToken);
        if (exists)
        {
            throw new ConflictException($"Tag '{trimmedName}' already exists.");
        }

        var tag = new Tag
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Name = trimmedName,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        await tagRepository.AddAsync(tag, cancellationToken);

        return new TagDto(tag.Id, tag.Name, tag.CreatedAtUtc);
    }

    public async Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var tags = await tagRepository.ListForUserAsync(userId, cancellationToken);
        return tags.Select(t => new TagDto(t.Id, t.Name, t.CreatedAtUtc)).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var tag = await tagRepository.FindByIdForUserAsync(id, userId, cancellationToken);
        if (tag is null)
        {
            throw new NotFoundException($"Tag '{id}' not found.");
        }

        await tagRepository.DeleteAsync(tag, cancellationToken);
    }

    public async Task AddTagToSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var source = await sourceRepository.FindEntityByIdForUserAsync(sourceId, userId, cancellationToken);
        if (source is null)
        {
            throw new NotFoundException($"Source '{sourceId}' not found.");
        }

        var tag = await tagRepository.FindByIdForUserAsync(tagId, userId, cancellationToken);
        if (tag is null)
        {
            throw new NotFoundException($"Tag '{tagId}' not found.");
        }

        var hasTag = await tagRepository.HasTagAsync(sourceId, tagId, cancellationToken);
        if (!hasTag)
        {
            await tagRepository.AddTagToSourceAsync(sourceId, tagId, cancellationToken);
        }
    }

    public async Task RemoveTagFromSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var source = await sourceRepository.FindEntityByIdForUserAsync(sourceId, userId, cancellationToken);
        if (source is null)
        {
            throw new NotFoundException($"Source '{sourceId}' not found.");
        }

        await tagRepository.RemoveTagFromSourceAsync(sourceId, tagId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetTagIdsForSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var source = await sourceRepository.FindEntityByIdForUserAsync(sourceId, userId, cancellationToken);
        if (source is null)
        {
            throw new NotFoundException($"Source '{sourceId}' not found.");
        }

        return await tagRepository.GetTagIdsForSourceAsync(sourceId, cancellationToken);
    }
}
