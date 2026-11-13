namespace TalkToRag.Domain.Entities;

/// <summary>
/// A passage a message points to, numbered as it appears in the text ([1], [2], ...).
/// Keeps its own copy of where the passage was and what it said, because chunks get new ids
/// whenever a source is ingested again; <see cref="SourceChunkId"/> is cleared when that happens.
/// </summary>
public class MessageCitation
{
    public Guid MessageId { get; set; }
    public int Number { get; set; }
    public Guid? SourceChunkId { get; set; }
    public Guid SourceId { get; set; }
    public int PageNumber { get; set; }
    public string? PageLabel { get; set; }
    public string? Section { get; set; }
    public int ChunkIndex { get; set; }
    public required string Content { get; set; }
}
