namespace TalkToRag.Application.Common.Interfaces;

public sealed record IngestSourceRequest(
    Guid SourceId,
    string BlobKey,
    string ContentType);

public sealed record IngestedChunkDto(
    int PageNumber,
    string? Section,
    int ChunkIndex,
    string Content,
    float[] Embedding);

public sealed record IngestSourceResponse(
    IReadOnlyList<IngestedChunkDto> Chunks);

public interface IAiClient
{
    Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default);
}
