using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Search;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/search")]
public sealed class SearchController(ISearchService searchService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<SearchResponse> Search(SearchRequest request, CancellationToken cancellationToken) =>
        searchService.SearchAsync(request, cancellationToken);

    [HttpPost("{conversationId:guid}/summary")]
    [ProducesResponseType<SummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<SummaryResponse> Summary(Guid conversationId, CancellationToken cancellationToken) =>
        searchService.GenerateSummaryAsync(conversationId, cancellationToken);
}
