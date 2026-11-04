using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Sources;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/sources")]
public sealed class SourcesController(ISourceService sourceService) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(60 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 60 * 1024 * 1024)]
    [ProducesResponseType<CreateSourceResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CreateSourceResponse>> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new RequestValidationException("A non-empty file is required.");
        }

        await using var stream = file.OpenReadStream();
        var response = await sourceService.UploadAsync(file.FileName, file.ContentType, stream, cancellationToken);
        return AcceptedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SourceDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IReadOnlyList<SourceDto>> List(CancellationToken cancellationToken) =>
        sourceService.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<SourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<SourceDto> GetById(Guid id, CancellationToken cancellationToken) =>
        sourceService.GetByIdAsync(id, cancellationToken);

    [HttpGet("{id:guid}/file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken cancellationToken)
    {
        var file = await sourceService.GetFileAsync(id, cancellationToken);
        Response.Headers.Append("Content-Disposition", $"inline; filename=\"{file.FileName}\"");
        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await sourceService.RetryAsync(id, cancellationToken);
        return Accepted();
    }
}
