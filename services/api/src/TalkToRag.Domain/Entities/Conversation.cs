namespace TalkToRag.Domain.Entities;

/// <summary>A run of questions with what was found for each. Its notebooks and tags limit what it searches.</summary>
public class Conversation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Title { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public List<Message> Messages { get; } = [];
    public List<ConversationNotebook> Notebooks { get; } = [];
    public List<ConversationTag> Tags { get; } = [];
}
