using System.Threading.Channels;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Infrastructure.Ingestion;

public sealed class ChannelSourceIngestionQueue : ISourceIngestionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true
    });

    public ValueTask EnqueueAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(sourceId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
