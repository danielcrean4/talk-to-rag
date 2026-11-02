using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public interface ISourceRepository
{
    Task<Source?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken);
    Task AddAsync(Source source, CancellationToken cancellationToken);
    Task UpdateAsync(Source source, CancellationToken cancellationToken);
}
