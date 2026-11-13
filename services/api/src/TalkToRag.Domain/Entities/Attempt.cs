namespace TalkToRag.Domain.Entities;

/// <summary>
/// One answer the user gave, from any place that tests them: flashcards, a quiz, a game.
/// How well a concept is understood is worked out from these, never set by hand.
/// </summary>
public class Attempt
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConceptId { get; set; }

    /// <summary>Null when the attempt came from outside (a game) or its question was later deleted.</summary>
    public Guid? QuestionId { get; set; }

    /// <summary>Where the attempt happened, such as "flashcards" or a game's name.</summary>
    public required string Platform { get; set; }

    /// <summary>From 0 (wrong) to 1 (right).</summary>
    public double Score { get; set; }
    public DateTimeOffset AnsweredAtUtc { get; set; }
}
