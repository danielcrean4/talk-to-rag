namespace TalkToRag.Application.Speech;

public sealed record TranscribeResponse(string Text, string BlobKey);

public interface ISpeechService
{
    Task<TranscribeResponse> TranscribeAsync(Stream audioStream, string contentType, CancellationToken cancellationToken);
    Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken);
}
