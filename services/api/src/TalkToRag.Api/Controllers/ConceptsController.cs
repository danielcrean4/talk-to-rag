using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Concepts;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/concepts")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class ConceptsController(IConceptService conceptService) : ControllerBase
{
    [HttpGet("graph")]
    public Task<ConceptGraphDto> GetGraph(CancellationToken cancellationToken) =>
        conceptService.GetGraphAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType<ConceptDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConceptDto>> Create(CreateConceptRequest request, CancellationToken cancellationToken)
    {
        var concept = await conceptService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = concept.Id }, concept);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConceptDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ConceptDetailsDto> GetById(Guid id, CancellationToken cancellationToken) =>
        conceptService.GetDetailsAsync(id, cancellationToken);

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<ConceptDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ConceptDto> Update(Guid id, UpdateConceptRequest request, CancellationToken cancellationToken) =>
        conceptService.UpdateAsync(id, request, cancellationToken);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await conceptService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/accept")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ConceptGraphDto> Accept(Guid id, CancellationToken cancellationToken) =>
        conceptService.AcceptAsync(id, cancellationToken);

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ConceptGraphDto> Reject(Guid id, CancellationToken cancellationToken) =>
        conceptService.RejectAsync(id, cancellationToken);

    [HttpPut("{id:guid}/prerequisites/{prerequisiteId:guid}")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ConceptGraphDto> AddPrerequisite(Guid id, Guid prerequisiteId, CancellationToken cancellationToken) =>
        conceptService.AddPrerequisiteAsync(id, prerequisiteId, cancellationToken);

    [HttpDelete("{id:guid}/prerequisites/{prerequisiteId:guid}")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ConceptGraphDto> RemovePrerequisite(Guid id, Guid prerequisiteId, CancellationToken cancellationToken) =>
        conceptService.RemovePrerequisiteAsync(id, prerequisiteId, cancellationToken);

    [HttpPost("{id:guid}/split")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ConceptGraphDto> Split(Guid id, SplitConceptRequest request, CancellationToken cancellationToken) =>
        conceptService.SplitAsync(id, request, cancellationToken);

    [HttpPost("{id:guid}/propose-prerequisites")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<ConceptGraphDto> ProposePrerequisites(Guid id, CancellationToken cancellationToken) =>
        conceptService.ProposePrerequisitesAsync(id, cancellationToken);

    [HttpGet("{id:guid}/passage-suggestions")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<IReadOnlyList<PassageSuggestionDto>> SuggestPassages(Guid id, CancellationToken cancellationToken) =>
        conceptService.SuggestPassagesAsync(id, cancellationToken);

    [HttpPost("{id:guid}/passages")]
    [ProducesResponseType<ConceptPassageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ConceptPassageDto>> AddPassage(Guid id, AddPassageRequest request, CancellationToken cancellationToken)
    {
        var passage = await conceptService.AddPassageAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, passage);
    }

    [HttpDelete("{id:guid}/passages/{passageId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePassage(Guid id, Guid passageId, CancellationToken cancellationToken)
    {
        await conceptService.RemovePassageAsync(id, passageId, cancellationToken);
        return NoContent();
    }
}
