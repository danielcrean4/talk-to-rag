using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Notebooks;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/notebooks")]
public sealed class NotebooksController(INotebookService notebookService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<NotebookDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NotebookDto>> Create(CreateNotebookRequest request, CancellationToken cancellationToken)
    {
        var result = await notebookService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NotebookDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IReadOnlyList<NotebookDto>> List(CancellationToken cancellationToken) =>
        notebookService.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<NotebookDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<NotebookDetailsDto> GetById(Guid id, CancellationToken cancellationToken) =>
        notebookService.GetByIdAsync(id, cancellationToken);

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<NotebookDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<NotebookDto> Update(Guid id, UpdateNotebookRequest request, CancellationToken cancellationToken) =>
        notebookService.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await notebookService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/sources/{sourceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddSource(Guid id, Guid sourceId, CancellationToken cancellationToken)
    {
        await notebookService.AddSourceAsync(id, sourceId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/sources/{sourceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveSource(Guid id, Guid sourceId, CancellationToken cancellationToken)
    {
        await notebookService.RemoveSourceAsync(id, sourceId, cancellationToken);
        return NoContent();
    }
}
