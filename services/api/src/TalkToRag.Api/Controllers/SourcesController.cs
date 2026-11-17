using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using TalkToRag.Api.Security;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/sources")]
public sealed class SourcesController(ISourceService sourceService) : ControllerBase
{
    // Room for the multipart envelope on top of the largest file the service accepts.
    private const long MaxRequestBytes = SourceService.MaxSizeBytes + 10 * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [ProducesResponseType<CreateSourceResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CreateSourceResponse>> Upload(
        IFormFile? file,
        [FromForm] string? license,
        [FromForm] string? sourceUrl,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new RequestValidationException("A non-empty file is required.");
        }

        await using var stream = file.OpenReadStream();
        var response = await sourceService.UploadAsync(file.FileName, stream, license, sourceUrl, cancellationToken);
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

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<SourceDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<SourceDto> Update(Guid id, UpdateSourceRequest request, CancellationToken cancellationToken) =>
        sourceService.UpdateAsync(id, request, cancellationToken);

    /// <summary>A link to the file for a new tab, which cannot send the sign-in header. It opens this file only, for a few minutes.</summary>
    [HttpPost("{id:guid}/file-link")]
    [ProducesResponseType<SourceFileLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<SourceFileLinkResponse> CreateFileLink(
        Guid id,
        [FromServices] ITokenGenerator tokens,
        [FromServices] ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        // Answers 404 for a source that is not the caller's.
        await sourceService.GetByIdAsync(id, cancellationToken);
        var token = tokens.CreateFileToken(currentUser.UserId, id);
        return new SourceFileLinkResponse(
            $"/api/sources/{id}/file?{SourceFileLink.QueryParameter}={Uri.EscapeDataString(token.Token)}",
            token.ExpiresAtUtc);
    }

    [HttpGet("{id:guid}/file")]
    [Authorize(AuthenticationSchemes = $"{JwtBearerDefaults.AuthenticationScheme},{SourceFileLink.Scheme}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(Guid id, CancellationToken cancellationToken)
    {
        var file = await sourceService.GetFileAsync(id, cancellationToken);
        Response.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline")
        {
            FileNameStar = file.FileName,
        }.ToString();
        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }

    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await sourceService.RetryAsync(id, cancellationToken);
        return Accepted();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sourceService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}
