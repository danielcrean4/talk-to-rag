namespace TalkToRag.Infrastructure.Ai;

/// <summary>The "AiService" config section: where the AI service is and how long a call to it may take.</summary>
public sealed class AiServiceOptions
{
    public const string Section = "AiService";

    public string BaseUrl { get; set; } = "http://127.0.0.1:8000";

    /// <summary>Ingestion parses and embeds a whole document in one call, which can take many minutes.</summary>
    public int IngestTimeoutSeconds { get; set; } = 30 * 60;

    /// <summary>
    /// Every other call is made while someone waits: query expansion, summaries, speech, concept proposals
    /// and flashcards. A streamed summary has to finish within this time too, not only start.
    /// </summary>
    public int InteractiveTimeoutSeconds { get; set; } = 3 * 60;
}
