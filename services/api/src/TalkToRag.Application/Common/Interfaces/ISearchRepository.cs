namespace TalkToRag.Application.Common.Interfaces;

public sealed record SearchQueryParameters(
    Guid UserId,
    string QueryText,
    IReadOnlyList<float[]> QueryEmbeddings,
    IReadOnlyList<Guid>? AllowedSourceIds,
    string Mode,
    float? MaxCosineDistance,
    int Limit);

public sealed record SearchHit(
    Guid ChunkId,
    Guid SourceId,
    string SourceTitle,
    string SourceContentType,
    int PageNumber,
    string? PageLabel,
    string? Section,
    int ChunkIndex,
    string Content,
    string? Headline,
    double Score,
    float CosineDistance,
    IReadOnlyList<string> MatchedBy);

public interface ISearchRepository
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQueryParameters parameters, CancellationToken cancellationToken);
}
