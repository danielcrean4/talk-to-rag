using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Speech;

namespace TalkToRag.Api.Controllers;

public sealed record SynthesizeSpeechRequest(string Text);
public sealed record RouteVoiceCommandRequest(string Transcript, ScreenContextDto? ScreenContext);
public sealed record PullUpVoiceCommandRequest(string Topic, Guid? NotebookId = null);

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

    [HttpPost("route")]
    [ProducesResponseType<RouteIntentResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RouteIntentResponseDto>> RouteCommand([FromBody] RouteVoiceCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await speechService.RouteIntentAsync(request.Transcript, request.ScreenContext, cancellationToken);
        return Ok(result);
    }

    [HttpPost("pull-up")]
    [ProducesResponseType<PullUpResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PullUpResponseDto>> PullUpTopic([FromBody] PullUpVoiceCommandRequest request, CancellationToken cancellationToken)
    {
        var result = await speechService.PullUpTopicAsync(request.Topic, request.NotebookId, cancellationToken);
        return Ok(result);
    }
}
