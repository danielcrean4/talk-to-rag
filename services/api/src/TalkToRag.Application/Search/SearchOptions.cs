namespace TalkToRag.Application.Search;

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    public float MaxCosineDistance { get; set; } = 0.6f;
}
