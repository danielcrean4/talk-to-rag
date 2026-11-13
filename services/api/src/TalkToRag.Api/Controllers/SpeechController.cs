using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Speech;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/speech")]
public sealed class SpeechController(ISpeechService speechService) : ControllerBase
{
    private const int MaxAudioBytes = 10 * 1024 * 1024;

    [HttpPost("transcribe")]
    [RequestSizeLimit(MaxAudioBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxAudioBytes)]
    [ProducesResponseType<TranscriptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<TranscriptionResponse> Transcribe(IFormFile? audio, CancellationToken cancellationToken)
    {
        if (audio is null || audio.Length == 0)
        {
            throw new RequestValidationException("A non-empty audio recording is required.");
        }

        await using var stream = audio.OpenReadStream();
        return await speechService.TranscribeAsync(audio.ContentType, stream, cancellationToken);
    }

    /// <summary>Speaks text with the local voice model and returns a WAV file.</summary>
    [HttpPost("synthesize")]
    [Produces("audio/wav")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "audio/wav")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Synthesize(SynthesizeSpeechRequest request, CancellationToken cancellationToken)
    {
        var speech = await speechService.SynthesizeAsync(request, cancellationToken);
        return File(speech.Audio, speech.ContentType);
    }
}
