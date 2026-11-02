using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public interface ITagRepository
{
    Task<Tag?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Tag>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> ExistsNameForUserAsync(string name, Guid userId, CancellationToken cancellationToken);
    Task AddAsync(Tag tag, CancellationToken cancellationToken);
    Task DeleteAsync(Tag tag, CancellationToken cancellationToken);
    Task<bool> HasTagAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken);
    Task AddTagToSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken);
    Task RemoveTagFromSourceAsync(Guid sourceId, Guid tagId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetTagIdsForSourceAsync(Guid sourceId, CancellationToken cancellationToken);
}
