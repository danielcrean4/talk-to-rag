using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Speech;

namespace TalkToRag.Api.Controllers;

public sealed record SynthesizeSpeechRequest(string Text);

[Authorize]
[ApiController]
[Route("api/speech")]
public sealed class SpeechController(ISpeechService speechService) : ControllerBase
{
    [HttpPost("transcribe")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 30 * 1024 * 1024)]
    [ProducesResponseType<TranscribeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<TranscribeResponse>> Transcribe(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new RequestValidationException("Audio file is required.");
        }

        await using var stream = file.OpenReadStream();
        var result = await speechService.TranscribeAsync(stream, file.ContentType, cancellationToken);
        return Ok(result);
    }

    [HttpPost("synthesize")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Synthesize([FromBody] SynthesizeSpeechRequest request, CancellationToken cancellationToken)
    {
        var stream = await speechService.SynthesizeAsync(request.Text, cancellationToken);
        return File(stream, "audio/wav");
    }
}
