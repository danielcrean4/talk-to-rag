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

    [HttpGet("{conversationId:guid}/summary/stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task StreamSummary(Guid conversationId, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("X-Accel-Buffering", "no");

        await foreach (var sseEvent in searchService.StreamSummaryAsync(conversationId, cancellationToken))
        {
            if (!string.IsNullOrEmpty(sseEvent))
            {
                await Response.WriteAsync($"{sseEvent}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }
    }
}
