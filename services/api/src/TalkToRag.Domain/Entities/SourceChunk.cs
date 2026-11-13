namespace TalkToRag.Domain.Entities;

/// <summary>One searchable passage of a source, with where it came from and its embedding.</summary>
public class SourceChunk
{
    /// <summary>
    /// Length of every embedding vector. Fixed by the embedding model (nomic-embed-text makes 768),
    /// and baked into the database column type, so changing models means a migration and re-embedding.
    /// </summary>
    public const int EmbeddingDimensions = 768;

    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public int PageNumber { get; set; }
    public string? PageLabel { get; set; }
    public string? Section { get; set; }
    public int ChunkIndex { get; set; }
    public string? EmbeddingModel { get; set; }
    public required string Content { get; set; }
    public required float[] Embedding { get; set; }
}
