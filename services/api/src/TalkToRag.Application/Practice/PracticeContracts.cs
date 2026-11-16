namespace TalkToRag.Application.Practice;

/// <summary>A flashcard. <see cref="DueAtUtc"/> is when FSRS says to ask it again; null until it is first answered.</summary>
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
    double? LastScore,
    DateTimeOffset? DueAtUtc);

public sealed record CreateQuestionRequest(string Prompt, string Answer);

/// <summary>
/// An answer from anywhere that tests the user. Give the question when there is one;
/// a game that has no stored question gives the concept instead.
/// </summary>
public sealed record RecordAttemptRequest(Guid? QuestionId, Guid? ConceptId, string Platform, double Score);

/// <summary>The concept's mastery after the attempt, and when the question is due again (null for an attempt without a question).</summary>
public sealed record AttemptResultDto(Guid ConceptId, MasteryDto Mastery, DateTimeOffset? DueAtUtc);

/// <summary>How many of a concept's questions are due now, and how many have never been answered.</summary>
public sealed record DueConceptDto(Guid ConceptId, string ConceptName, int Due, int New);

/// <summary>
/// What to review now: every question that is due, earliest first, then at most
/// <c>NewPerReview</c> questions never answered, oldest first. The counts cover all of them.
/// </summary>
public sealed record DueCardsDto(IReadOnlyList<QuestionDto> Questions, int DueCount, int NewCount, IReadOnlyList<DueConceptDto> Concepts);
