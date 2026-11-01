namespace TalkToRag.Domain.Entities;

public class Notebook
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<NotebookSource> Sources { get; } = [];
}
