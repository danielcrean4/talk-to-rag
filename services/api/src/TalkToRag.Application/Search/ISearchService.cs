namespace TalkToRag.Application.Search;

public interface ISearchService
{
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken);
    Task<SummaryResponse> GenerateSummaryAsync(Guid conversationId, CancellationToken cancellationToken);
}
