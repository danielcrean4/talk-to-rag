namespace TalkToRag.Domain.Entities;

public class Tag
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
