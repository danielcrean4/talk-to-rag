namespace TalkToRag.Application.Speech;

public interface ISpeechService
{
    Task<TranscriptionResponse> TranscribeAsync(string contentType, Stream audio, CancellationToken cancellationToken = default);
    Task<SynthesizedSpeechResponse> SynthesizeAsync(SynthesizeSpeechRequest request, CancellationToken cancellationToken = default);
}
