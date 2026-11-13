using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Pgvector;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class SourceChunkConfiguration : IEntityTypeConfiguration<SourceChunk>
{
    public const string SearchVector = "SearchVector";

    public void Configure(EntityTypeBuilder<SourceChunk> builder)
    {
        builder.HasOne<Source>().WithMany(s => s.Chunks).HasForeignKey(c => c.SourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.SourceId, c.ChunkIndex }).IsUnique();
        builder.Property(c => c.EmbeddingModel).HasMaxLength(100);

        // Semantic search: the domain keeps a plain float[]; only this layer knows it is a pgvector column.
        // HNSW is an approximate nearest-neighbour index; vector_cosine_ops makes it serve the <=> operator.
        builder.Property(c => c.Embedding)
            .HasColumnType($"vector({SourceChunk.EmbeddingDimensions})")
            .HasConversion(
                v => new Vector(v),
                v => v.ToArray(),
                // Compare by contents, so EF notices when an embedding's values change.
                new ValueComparer<float[]>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, x) => HashCode.Combine(hash, x)),
                    v => v.ToArray()));
        builder.HasIndex(c => c.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");

        // Keyword search: Postgres keeps a stemmed word list of content up to date itself, indexed with GIN.
        // A shadow property, because the domain has no use for it.
        builder.Property<NpgsqlTsVector>(SearchVector)
            .HasComputedColumnSql("to_tsvector('english', content)", stored: true);
        builder.HasIndex(SearchVector).HasMethod("gin");
    }
}
