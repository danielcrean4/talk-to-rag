namespace TalkToRag.Domain.Entities;

/// <summary>A passage a message points to, numbered as it appears in the text ([1], [2], ...).</summary>
public class MessageCitation
{
    public Guid MessageId { get; set; }
    public Guid SourceChunkId { get; set; }
    public int Number { get; set; }
}
