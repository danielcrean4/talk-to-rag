using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Tags;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
public sealed class TagsController(ITagService tagService) : ControllerBase
{
    [HttpPost("api/tags")]
    [ProducesResponseType<TagDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TagDto>> Create(CreateTagRequest request, CancellationToken cancellationToken)
    {
        var result = await tagService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("api/tags")]
    [ProducesResponseType<IReadOnlyList<TagDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IReadOnlyList<TagDto>> List(CancellationToken cancellationToken) =>
        tagService.ListAsync(cancellationToken);

    [HttpDelete("api/tags/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await tagService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("api/sources/{sourceId:guid}/tags/{tagId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddTagToSource(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        await tagService.AddTagToSourceAsync(sourceId, tagId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("api/sources/{sourceId:guid}/tags/{tagId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveTagFromSource(Guid sourceId, Guid tagId, CancellationToken cancellationToken)
    {
        await tagService.RemoveTagFromSourceAsync(sourceId, tagId, cancellationToken);
        return NoContent();
    }

    [HttpGet("api/sources/{sourceId:guid}/tags")]
    [ProducesResponseType<IReadOnlyList<Guid>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<Guid>> GetTagsForSource(Guid sourceId, CancellationToken cancellationToken) =>
        tagService.GetTagIdsForSourceAsync(sourceId, cancellationToken);
}
