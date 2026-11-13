namespace TalkToRag.Application.Practice;

public sealed record QuestionDto(
    Guid Id,
    Guid ConceptId,
    string Prompt,
    string Answer,
    bool GeneratedByModel,
    Guid? SourceId,
    string? SourceTitle,
    int? PageNumber,
    string? PageLabel,
    string? Section,
    int Attempts,
    double? LastScore);

public sealed record CreateQuestionRequest(string Prompt, string Answer);

/// <summary>
/// An answer from anywhere that tests the user. Give the question when there is one;
/// a game that has no stored question gives the concept instead.
/// </summary>
public sealed record RecordAttemptRequest(Guid? QuestionId, Guid? ConceptId, string Platform, double Score);

public sealed record AttemptResultDto(Guid ConceptId, MasteryDto Mastery);
