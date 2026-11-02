namespace TalkToRag.Application.Common.Interfaces;

/// <summary>
/// Queue of source IDs waiting to be ingested (parsed, chunked, embedded) by the background worker.
/// </summary>
public interface ISourceIngestionQueue
{
    ValueTask EnqueueAsync(Guid sourceId, CancellationToken cancellationToken = default);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default);
}
