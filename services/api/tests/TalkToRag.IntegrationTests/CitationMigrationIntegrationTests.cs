using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;
using TalkToRag.Infrastructure.Persistence;

namespace TalkToRag.IntegrationTests;

public class CitationMigrationIntegrationTests : IAsyncLifetime
{
    private const string MigrationBeforeCitationLocations = "20261112160358_WithholdModelTextFromAuditViews";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    private TalkToRagDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<TalkToRagDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .Options;

        _db = new TalkToRagDbContext(options);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task Migration_CopiesEachCitationsPassageFromItsChunk()
    {
        await _db.GetService<IMigrator>().MigrateAsync(MigrationBeforeCitationLocations);

        var user = new User { Id = Guid.NewGuid(), Email = "a@test.com", DisplayName = "User A", PasswordHash = "hash" };
        var source = new Source
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "Linear Algebra",
            FileName = "la.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/la.pdf",
            Status = SourceStatus.Ready
        };
        var chunk = new SourceChunk
        {
            Id = Guid.CreateVersion7(),
            SourceId = source.Id,
            PageNumber = 14,
            PageLabel = "xii",
            Section = "Eigenvalues",
            ChunkIndex = 3,
            Content = "Eigenvalues scale eigenvectors.",
            Embedding = new float[SourceChunk.EmbeddingDimensions],
            EmbeddingModel = "nomic-embed-text"
        };
        var conversation = new Conversation { Id = Guid.NewGuid(), UserId = user.Id, Title = "eigenvalues" };
        var message = new Message { Id = Guid.NewGuid(), ConversationId = conversation.Id, Role = MessageRole.Assistant, Content = "[1]" };
        _db.AddRange(user, source, chunk, conversation, message);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        await _db.Database.ExecuteSqlAsync(
            $"INSERT INTO message_citations (message_id, source_chunk_id, number) VALUES ({message.Id}, {chunk.Id}, 1)");

        await _db.Database.MigrateAsync();

        var citation = await _db.MessageCitations.SingleAsync();
        Assert.Equal(chunk.Id, citation.SourceChunkId);
        Assert.Equal(source.Id, citation.SourceId);
        Assert.Equal(14, citation.PageNumber);
        Assert.Equal("xii", citation.PageLabel);
        Assert.Equal("Eigenvalues", citation.Section);
        Assert.Equal(3, citation.ChunkIndex);
        Assert.Equal("Eigenvalues scale eigenvectors.", citation.Content);

        var audited = await _db.Database
            .SqlQuery<string>($"SELECT source_title || ' p' || page_number || ' #' || chunk_index AS \"Value\" FROM audit.citations")
            .SingleAsync();
        Assert.Equal("Linear Algebra p14 #3", audited);

        var auditorCanRead = await _db.Database
            .SqlQuery<bool>($"SELECT has_table_privilege('talk_to_rag_auditor', 'audit.citations', 'SELECT') AS \"Value\"")
            .SingleAsync();
        Assert.True(auditorCanRead);
    }
}
