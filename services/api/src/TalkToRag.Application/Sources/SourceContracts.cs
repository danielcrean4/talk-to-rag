namespace TalkToRag.Application.Sources;

public sealed record CreateSourceResponse(
    Guid Id,
    string Title,
    string Status);

public sealed record SourceDto(
    Guid Id,
    string Title,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Status,
    string? FailureReason,
    int ChunkCount,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<Guid> TagIds);

public sealed record SourceFileResponse(
    Stream Stream,
    string ContentType,
    string FileName);
