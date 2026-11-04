namespace TalkToRag.Application.Common.Interfaces;

public sealed record IngestSourceRequest(
    Guid SourceId,
    string BlobKey,
    string ContentType);

public sealed record IngestedChunkDto(
    int PageNumber,
    string? PageLabel,
    string? Section,
    int ChunkIndex,
    string Content,
    float[] Embedding);

public sealed record IngestSourceResponse(
    IReadOnlyList<IngestedChunkDto> Chunks);

public sealed record RecentMessageDto(string Role, string Content);

public sealed record ExpandQueryRequest(
    string Question,
    IReadOnlyList<RecentMessageDto> RecentMessages,
    bool Expand);

public sealed record QueryItemDto(
    string Text,
    float[] Embedding);

public sealed record ExpandQueryResponse(
    IReadOnlyList<QueryItemDto> Queries);

public sealed record PassageItemDto(
    int N,
    string Content,
    string? SourceTitle = null,
    int? PageNumber = null,
    string? PageLabel = null,
    string? Section = null);

public sealed record AnswerRequest(
    string Question,
    IReadOnlyList<PassageItemDto> Passages);

public sealed record AnswerResponse(
    string Answer,
    IReadOnlyList<int> Cited);

public sealed record ScreenContextDto(
    Guid? SourceId = null,
    string? SourceTitle = null,
    int? CurrentPage = null,
    int? TotalPages = null,
    string? SelectedText = null,
    string? ActiveView = null);

public sealed record RouteIntentRequestDto(
    string Transcript,
    ScreenContextDto? ScreenContext = null);

public sealed record RouteIntentResponseDto(
    string Intent,
    string? Topic,
    string? Query,
    string Route,
    double Confidence);

public interface IAiClient
{
    Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default);
    Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default);
    Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default);
    Task<string> TranscribeAsync(string blobKey, CancellationToken cancellationToken = default);
    Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken = default);
    Task<RouteIntentResponseDto> RouteIntentAsync(RouteIntentRequestDto request, CancellationToken cancellationToken = default);
}
