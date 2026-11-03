namespace TalkToRag.Application.Notebooks;

public sealed record CreateNotebookRequest(string Name, string? Description);

public sealed record UpdateNotebookRequest(string? Name, string? Description);

public sealed record NotebookDto(
    Guid Id,
    string Name,
    string? Description,
    int SourceCount,
    DateTimeOffset CreatedAtUtc);

public sealed record NotebookDetailsDto(
    Guid Id,
    string Name,
    string? Description,
    IReadOnlyList<Guid> SourceIds,
    DateTimeOffset CreatedAtUtc);
