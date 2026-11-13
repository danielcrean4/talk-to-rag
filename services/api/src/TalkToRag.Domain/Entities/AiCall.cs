namespace TalkToRag.Domain.Entities;

/// <summary>
/// One request the API made to the AI service: what was sent, what came back (including the model
/// prompts and replies the AI service reports in its trace), how long it took and whether it failed.
/// The redacted copies have source passage text replaced by placeholders, for readers who may not see it.
/// </summary>
public class AiCall
{
    public Guid Id { get; set; }

    /// <summary>The signed-in user, or for ingestion the source's owner.</summary>
    public Guid? UserId { get; set; }

    /// <summary>Ties the call to the web request that caused it.</summary>
    public string? RequestTraceId { get; set; }

    /// <summary>The AI service path, such as "/v1/rag/answer".</summary>
    public required string Operation { get; set; }
    public int? StatusCode { get; set; }
    public int DurationMs { get; set; }
    public required string Request { get; set; }
    public string? Response { get; set; }
    public required string RequestRedacted { get; set; }
    public string? ResponseRedacted { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
