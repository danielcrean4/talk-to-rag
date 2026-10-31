namespace TalkToRag.Domain.Entities;

/// <summary>Puts a source in a notebook. A source can be in any number of notebooks without being re-uploaded.</summary>
public class NotebookSource
{
    public Guid NotebookId { get; set; }
    public Guid SourceId { get; set; }
}
