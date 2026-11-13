namespace TalkToRag.Domain.Entities;

/// <summary>
/// Where a concept is covered in a source. Located by page and section, not by chunk,
/// because chunks get new ids whenever a source is ingested again.
/// </summary>
public class ConceptPassage
{
    public Guid Id { get; set; }
    public Guid ConceptId { get; set; }
    public Guid SourceId { get; set; }
    public int PageNumber { get; set; }
    public string? PageLabel { get; set; }
    public string? Section { get; set; }
    public required string Snippet { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
