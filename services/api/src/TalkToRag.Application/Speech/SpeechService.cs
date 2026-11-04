using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Application.Speech;

public sealed class SpeechService(
    IBlobStorage blobStorage,
    IAiClient aiClient,
    ICurrentUser currentUser) : ISpeechService
{
    private const long MaxAudioSizeBytes = 25 * 1024 * 1024; // 25 MB

    public async Task<TranscribeResponse> TranscribeAsync(Stream audioStream, string contentType, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId; // Enforce user auth

        if (audioStream.CanSeek && audioStream.Length == 0)
        {
            throw new RequestValidationException("Audio stream cannot be empty.");
        }

        if (audioStream.CanSeek && audioStream.Length > MaxAudioSizeBytes)
        {
            throw new RequestValidationException("Audio file exceeds 25 MB limit.");
        }

        var speechId = Guid.CreateVersion7();
        var extension = contentType.Contains("wav") ? ".wav" : ".webm";
        var blobKey = $"speech/{speechId:N}{extension}";

        await blobStorage.WriteAsync(blobKey, audioStream, cancellationToken);
        var transcript = await aiClient.TranscribeAsync(blobKey, cancellationToken);

        return new TranscribeResponse(transcript, blobKey);
    }

    public async Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId; // Enforce user auth

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RequestValidationException("Text to synthesize cannot be empty.");
        }

        return await aiClient.SynthesizeAsync(text, cancellationToken);
    }
}
