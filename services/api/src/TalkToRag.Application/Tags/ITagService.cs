namespace TalkToRag.Application.Tags;

public interface ITagService
{
    Task<TagDto> CreateAsync(CreateTagRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<TagDto>> ListAsync(CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task AddTagToSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken);
    Task RemoveTagFromSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetTagIdsForSourceAsync(Guid sourceId, CancellationToken cancellationToken);
}
