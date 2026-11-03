namespace TalkToRag.Application.Conversations;

public sealed record ConversationSummaryDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record MessageCitationDto(
    int Number,
    Guid SourceChunkId,
    int? PageNumber,
    string? PageLabel,
    string? Section,
    string Content);

public sealed record MessageDto(
    Guid Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<MessageCitationDto> Citations);

public sealed record ConversationDetailsDto(
    Guid Id,
    string Title,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<MessageDto> Messages);
