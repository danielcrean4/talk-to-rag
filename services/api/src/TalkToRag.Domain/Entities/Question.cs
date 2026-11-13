namespace TalkToRag.Domain.Entities;

/// <summary>Something to answer about a concept, such as a flashcard, optionally tied to the place in a source it came from.</summary>
public class Question
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConceptId { get; set; }
    public required string Prompt { get; set; }
    public required string Answer { get; set; }
    public bool GeneratedByModel { get; set; }
    public Guid? SourceId { get; set; }
    public int? PageNumber { get; set; }
    public string? PageLabel { get; set; }
    public string? Section { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
