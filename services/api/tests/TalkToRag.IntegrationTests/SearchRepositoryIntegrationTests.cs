using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;
using TalkToRag.Infrastructure.Persistence;

namespace TalkToRag.IntegrationTests;

public class SearchRepositoryIntegrationTests : IAsyncLifetime
{
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
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private static float[] MakeVector(int activeDimension)
    {
        var v = new float[SourceChunk.EmbeddingDimensions];
        if (activeDimension >= 0 && activeDimension < v.Length)
        {
            v[activeDimension] = 1.0f;
        }
        return v;
    }

    [Fact]
    public async Task SearchAsync_VerifiesHybridRanking_ScopeIsolation_AndCutoff()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        _db.Users.AddRange(
            new User { Id = userA, Email = "a@test.com", DisplayName = "User A", PasswordHash = "hash" },
            new User { Id = userB, Email = "b@test.com", DisplayName = "User B", PasswordHash = "hash" });

        var sourceReadyA = new Source
        {
            Id = Guid.NewGuid(),
            UserId = userA,
            Title = "RL Textbook",
            FileName = "rl.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/rl.pdf",
            Status = SourceStatus.Ready
        };

        var sourcePendingA = new Source
        {
            Id = Guid.NewGuid(),
            UserId = userA,
            Title = "Unprocessed Notes",
            FileName = "notes.md",
            ContentType = "text/markdown",
            BlobKey = "sources/notes.md",
            Status = SourceStatus.Pending
        };

        var sourceUserB = new Source
        {
            Id = Guid.NewGuid(),
            UserId = userB,
            Title = "Secret Notes",
            FileName = "secret.txt",
            ContentType = "text/plain",
            BlobKey = "sources/secret.txt",
            Status = SourceStatus.Ready
        };

        _db.Sources.AddRange(sourceReadyA, sourcePendingA, sourceUserB);

        var chunkTd = new SourceChunk
        {
            Id = Guid.NewGuid(),
            SourceId = sourceReadyA.Id,
            PageNumber = 120,
            PageLabel = "120",
            Section = "6.1 TD Learning",
            ChunkIndex = 0,
            Content = "Temporal difference learning bootstraps from estimates without waiting for the final reward.",
            Embedding = MakeVector(0)
        };

        var chunkDqn = new SourceChunk
        {
            Id = Guid.NewGuid(),
            SourceId = sourceReadyA.Id,
            PageNumber = 200,
            PageLabel = "200",
            Section = "Deep RL",
            ChunkIndex = 1,
            Content = "Deep Q-networks use neural networks with experience replay buffers.",
            Embedding = MakeVector(1)
        };

        var chunkPending = new SourceChunk
        {
            Id = Guid.NewGuid(),
            SourceId = sourcePendingA.Id,
            PageNumber = 1,
            ChunkIndex = 0,
            Content = "Temporal difference learning draft in pending state.",
            Embedding = MakeVector(0)
        };

        var chunkUserB = new SourceChunk
        {
            Id = Guid.NewGuid(),
            SourceId = sourceUserB.Id,
            PageNumber = 1,
            ChunkIndex = 0,
            Content = "Temporal difference notes belonging exclusively to User B.",
            Embedding = MakeVector(0)
        };

        _db.SourceChunks.AddRange(chunkTd, chunkDqn, chunkPending, chunkUserB);
        await _db.SaveChangesAsync();

        var repo = new SearchRepository(_db);

        // 1. Hybrid search for User A: finds chunkTd as top hit, never returns pending or user B chunks!
        var hybridParams = new SearchQueryParameters(
            UserId: userA,
            QueryText: "Temporal difference",
            QueryEmbeddings: [MakeVector(0)],
            AllowedSourceIds: null,
            Mode: "hybrid",
            MaxCosineDistance: 0.8f,
            Limit: 10);

        var hits = await repo.SearchAsync(hybridParams, default);

        var topHit = Assert.Single(hits);
        Assert.Equal(chunkTd.Id, topHit.ChunkId);
        Assert.Equal(120, topHit.PageNumber);
        Assert.Equal("120", topHit.PageLabel);
        Assert.Equal("6.1 TD Learning", topHit.Section);
        Assert.Contains("keyword", topHit.MatchedBy);
        Assert.Contains("semantic", topHit.MatchedBy);
        Assert.NotNull(topHit.Headline);
        Assert.Contains("<mark>Temporal</mark> <mark>difference</mark>", topHit.Headline);

        // 2. Keyword only search
        var keywordParams = hybridParams with { Mode = "keyword" };
        var keywordHits = await repo.SearchAsync(keywordParams, default);
        Assert.Single(keywordHits);
        Assert.Equal(chunkTd.Id, keywordHits[0].ChunkId);
        Assert.Equal(["keyword"], keywordHits[0].MatchedBy);

        // 3. Semantic only search
        var semanticParams = hybridParams with { Mode = "semantic", QueryText = "unrelated word" };
        var semanticHits = await repo.SearchAsync(semanticParams, default);
        Assert.Single(semanticHits);
        Assert.Equal(chunkTd.Id, semanticHits[0].ChunkId);
        Assert.Equal(["semantic"], semanticHits[0].MatchedBy);

        // 4. Distance cutoff: cutoff of 0.05 when distance to MakeVector(1) is ~1.0 drops the chunk!
        var cutoffParams = hybridParams with { QueryEmbeddings = [MakeVector(1)], QueryText = "xyz", Mode = "semantic", MaxCosineDistance = 0.05f };
        var cutoffHits = await repo.SearchAsync(cutoffParams, default);
        // chunkTd has vector(0) so distance to vector(1) is ~1.0; should be filtered out by cutoff!
        Assert.DoesNotContain(cutoffHits, h => h.ChunkId == chunkTd.Id);

        // 5. Scope filter: restricting to sourcePendingA returns 0 hits because it is not Ready
        var scopedParams = hybridParams with { AllowedSourceIds = [sourcePendingA.Id] };
        var scopedHits = await repo.SearchAsync(scopedParams, default);
        Assert.Empty(scopedHits);
    }
}
