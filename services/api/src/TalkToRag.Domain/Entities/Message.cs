using TalkToRag.Domain.Enums;

namespace TalkToRag.Domain.Entities;

public class Message
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public MessageRole Role { get; set; }
    public required string Content { get; set; }

    /// <summary>The original recording when the question was spoken; null when typed.</summary>
    public string? AudioBlobKey { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<MessageCitation> Citations { get; } = [];
}
