namespace TalkToRag.Application.Speech;

public sealed record TranscriptionResponse(string Text);

public sealed record SynthesizeSpeechRequest(string Text, string? Voice = null);

/// <summary>Spoken audio. Disposing <see cref="Audio"/> deletes the stored file it is read from.</summary>
public sealed record SynthesizedSpeechResponse(Stream Audio, string ContentType);
