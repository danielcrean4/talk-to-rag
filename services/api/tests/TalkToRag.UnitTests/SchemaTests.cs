using Microsoft.EntityFrameworkCore;
using TalkToRag.Infrastructure.Persistence;

namespace TalkToRag.UnitTests;

/// <summary>Checks the SQL the model produces, without a database: the search features depend on these lines.</summary>
public class SchemaTests
{
    private static readonly string Sql = CreateScript();

    private static string CreateScript()
    {
        var options = new DbContextOptionsBuilder<TalkToRagDbContext>()
            .UseNpgsql("Host=unused", npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .Options;
        using var context = new TalkToRagDbContext(options);
        return context.Database.GenerateCreateScript();
    }

    [Theory]
    [InlineData("CREATE EXTENSION IF NOT EXISTS vector;")]
    [InlineData("embedding vector(768) NOT NULL")]
    [InlineData("USING hnsw (embedding vector_cosine_ops)")]
    [InlineData("search_vector tsvector GENERATED ALWAYS AS (to_tsvector('english', content)) STORED")]
    [InlineData("USING gin (search_vector)")]
    [InlineData("section text")]
    [InlineData("page_label text")]
    public void Schema_SupportsHybridSearch(string expected) => Assert.Contains(expected, Sql);

    [Theory]
    [InlineData("users")]
    [InlineData("notebooks")]
    [InlineData("sources")]
    [InlineData("source_chunks")]
    [InlineData("tags")]
    [InlineData("notebook_sources")]
    [InlineData("source_tags")]
    [InlineData("conversations")]
    [InlineData("messages")]
    [InlineData("message_citations")]
    [InlineData("concepts")]
    [InlineData("concept_prerequisites")]
    [InlineData("concept_passages")]
    [InlineData("questions")]
    [InlineData("attempts")]
    [InlineData("ai_calls")]
    public void Schema_HasSnakeCaseTable(string table) => Assert.Contains($"CREATE TABLE {table} (", Sql);

    [Fact]
    public void Statuses_AreStoredAsReadableText() => Assert.Contains("status text NOT NULL", Sql);

    [Fact]
    public void Citations_OutliveTheChunksTheyCameFrom() =>
        Assert.Contains("FOREIGN KEY (source_chunk_id) REFERENCES source_chunks (id) ON DELETE SET NULL", Sql);
}
