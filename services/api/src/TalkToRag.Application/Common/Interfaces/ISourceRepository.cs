using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.Application.Common.Interfaces;

public interface ISourceRepository
{
    Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken);
    Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken);
    Task AddAsync(Source source, CancellationToken cancellationToken);
    Task UpdateAsync(Source source, CancellationToken cancellationToken);
    Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken);
    Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);
}
