using TalkToRag.Domain.Enums;

namespace TalkToRag.Domain.Entities;

/// <summary>Something to understand, such as "Convolutional neural networks". Concepts form a graph through prerequisites.</summary>
public class Concept
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Name { get; set; }

    /// <summary>Lower-cased name with single spaces, so "Chain  Rule" and "chain rule" are one concept.</summary>
    public required string NormalizedName { get; set; }
    public string? Summary { get; set; }
    public ProposalState State { get; set; }
    public bool ProposedByModel { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public List<ConceptPassage> Passages { get; } = [];

    public static string Normalize(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
}
