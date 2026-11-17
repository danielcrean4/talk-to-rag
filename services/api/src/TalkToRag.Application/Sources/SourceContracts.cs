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
    IReadOnlyList<Guid> TagIds,
    string? License = null,
    string? SourceUrl = null);

/// <summary>A field left null keeps its value; an empty or blank one clears it.</summary>
public sealed record UpdateSourceRequest(string? License, string? SourceUrl);

/// <summary>A link to the file that works without a sign-in header until <paramref name="ExpiresAtUtc"/>.</summary>
public sealed record SourceFileLinkResponse(string Url, DateTimeOffset ExpiresAtUtc);

public sealed record SourceFileResponse(
    Stream Stream,
    string ContentType,
    string FileName);
