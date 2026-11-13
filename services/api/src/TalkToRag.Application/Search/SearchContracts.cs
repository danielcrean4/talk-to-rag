namespace TalkToRag.Application.Search;

public sealed record SearchRequest(
    string Query,
    IReadOnlyList<Guid>? NotebookIds = null,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<Guid>? SourceIds = null,
    string Mode = "hybrid",
    bool Expand = false,
    int Limit = 20,
    Guid? ConversationId = null,
    float? MaxCosineDistance = null);

public sealed record SourceSummaryDto(
    Guid Id,
    string Title,
    string ContentType);

public sealed record PassageHitDto(
    int Number,
    Guid ChunkId,
    int PageNumber,
    string? PageLabel,
    string? Section,
    string Snippet,
    double Score,
    IReadOnlyList<string> MatchedBy);

public sealed record SourcePassageGroupDto(
    SourceSummaryDto Source,
    IReadOnlyList<PassageHitDto> Passages);

public sealed record SearchResponse(
    Guid ConversationId,
    string Query,
    IReadOnlyList<SourcePassageGroupDto> Groups);

public sealed record SummaryResponse(
    Guid ConversationId,
    string Question,
    string Answer,
    IReadOnlyList<int> CitedPassages);
