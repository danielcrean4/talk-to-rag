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
        if (await tagRepository.ExistsNameForUserAsync(trimmedName, userId, cancellationToken))
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
        var tags = await tagRepository.ListForUserAsync(currentUser.UserId, cancellationToken);
        return tags.Select(t => new TagDto(t.Id, t.Name, t.CreatedAtUtc)).ToList();
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var tag = await RequireTagAsync(id, cancellationToken);
        await tagRepository.DeleteAsync(tag, cancellationToken);
    }

    public async Task AddTagToSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        await RequireTagAsync(tagId, cancellationToken);
        if (!await tagRepository.HasTagAsync(sourceId, tagId, cancellationToken))
        {
            await tagRepository.AddTagToSourceAsync(sourceId, tagId, cancellationToken);
        }
    }

    public async Task RemoveTagFromSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        await tagRepository.RemoveTagFromSourceAsync(sourceId, tagId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetTagIdsForSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        await RequireSourceAsync(sourceId, cancellationToken);
        return await tagRepository.GetTagIdsForSourceAsync(sourceId, cancellationToken);
    }

    private async Task RequireSourceAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        if (await sourceRepository.FindEntityByIdForUserAsync(sourceId, currentUser.UserId, cancellationToken) is null)
        {
            throw new NotFoundException($"Source '{sourceId}' not found.");
        }
    }

    private async Task<Tag> RequireTagAsync(Guid id, CancellationToken cancellationToken) =>
        await tagRepository.FindByIdForUserAsync(id, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Tag '{id}' not found.");
}
