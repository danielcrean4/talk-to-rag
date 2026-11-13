using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Application.Speech;

public sealed class SpeechService(
    IBlobStorage blobStorage,
    IAiClient aiClient,
    ICurrentUser currentUser) : ISpeechService
{
    public const int MaxSynthesizeCharacters = 5000;

    private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/webm"] = ".webm",
        ["audio/ogg"] = ".ogg",
        ["audio/mp4"] = ".m4a",
        ["audio/mpeg"] = ".mp3",
        ["audio/wav"] = ".wav",
        ["audio/x-wav"] = ".wav",
    };

    public async Task<TranscriptionResponse> TranscribeAsync(string contentType, Stream audio, CancellationToken cancellationToken = default)
    {
        // Browsers append the codec, as in "audio/webm;codecs=opus".
        var mediaType = contentType.Split(';')[0].Trim();
        if (!Extensions.TryGetValue(mediaType, out var extension))
        {
            throw new RequestValidationException($"Unsupported audio type '{mediaType}'.");
        }

        // The recording is only a hand-off to the AI service, so it is removed once transcribed.
        var blobKey = $"audio/{currentUser.UserId}/{Guid.NewGuid()}{extension}";
        await blobStorage.WriteAsync(blobKey, audio, cancellationToken);
        try
        {
            var result = await aiClient.TranscribeAsync(new TranscribeRequest(blobKey), cancellationToken);
            return new TranscriptionResponse(result.Text.Trim());
        }
        finally
        {
            await blobStorage.DeleteAsync(blobKey, CancellationToken.None);
        }
    }

    public async Task<SynthesizedSpeechResponse> SynthesizeAsync(SynthesizeSpeechRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new RequestValidationException("Text to speak is required.");
        }

        if (request.Text.Length > MaxSynthesizeCharacters)
        {
            throw new RequestValidationException($"Text to speak must be at most {MaxSynthesizeCharacters} characters.");
        }

        // The AI service writes the audio here; it is deleted once the caller has read it.
        var blobKey = $"audio/{currentUser.UserId}/{Guid.NewGuid()}.wav";
        try
        {
            // Not cancelled with the request: the AI service would still write the file, after the cleanup below.
            var result = await aiClient.SynthesizeAsync(new SynthesizeRequest(request.Text, request.Voice, blobKey), CancellationToken.None);
            cancellationToken.ThrowIfCancellationRequested();
            var audio = await blobStorage.OpenReadAsync(blobKey, cancellationToken);
            return new SynthesizedSpeechResponse(new DeleteOnDisposeStream(audio, blobStorage, blobKey), result.ContentType);
        }
        catch
        {
            await blobStorage.DeleteAsync(blobKey, CancellationToken.None);
            throw;
        }
    }
}
