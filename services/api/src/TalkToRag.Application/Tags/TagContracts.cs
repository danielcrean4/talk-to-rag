namespace TalkToRag.Application.Tags;

public sealed record CreateTagRequest(string Name);

public sealed record TagDto(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAtUtc);
