using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;
using TalkToRag.Infrastructure.Persistence;
using TalkToRag.Infrastructure.Storage;

namespace TalkToRag.IntegrationTests;

public class SourceRepositoryIntegrationTests : IAsyncLifetime
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

    [Fact]
    public async Task SaveChunksAsync_WhenPostgresRejectsAChunk_StillLetsTheSourceBeMarkedFailed()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "a@test.com", DisplayName = "User A", PasswordHash = "hash" };
        var source = new Source
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "Linear Algebra",
            FileName = "la.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/la.pdf",
            Status = SourceStatus.Processing
        };
        _db.Users.Add(user);
        _db.Sources.Add(source);
        await _db.SaveChangesAsync();

        var repository = new SourceRepository(_db);
        var rejected = new SourceChunk
        {
            Id = Guid.CreateVersion7(),
            SourceId = source.Id,
            PageNumber = 1,
            ChunkIndex = 0,
            Content = "eigen\0values",
            Embedding = new float[SourceChunk.EmbeddingDimensions],
            EmbeddingModel = "nomic-embed-text"
        };

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChunksAsync(source.Id, [rejected], default));

        Assert.False(await repository.HasChunksAsync(source.Id, default));
        Assert.True(await repository.SetStatusAsync(source.Id, SourceStatus.Failed, "rejected", default));

        _db.ChangeTracker.Clear();
        var reloaded = await _db.Sources.SingleAsync(s => s.Id == source.Id);
        Assert.Equal(SourceStatus.Failed, reloaded.Status);
    }

    [Fact]
    public async Task SaveChunksAsync_WhenASourceIsIngestedAgain_KeepsTheCitationsOfEarlierMessages()
    {
        var (source, message, chunk) = await SeedCitedSourceAsync();
        var repository = new SourceRepository(_db);

        await repository.SaveChunksAsync(source.Id, [NewChunk(source.Id, "Eigenvalues, reworded.")], default);

        _db.ChangeTracker.Clear();
        var citation = await _db.MessageCitations.SingleAsync(c => c.MessageId == message.Id);
        Assert.Equal(1, citation.Number);
        Assert.Null(citation.SourceChunkId);
        Assert.Equal(source.Id, citation.SourceId);
        Assert.Equal(chunk.PageNumber, citation.PageNumber);
        Assert.Equal(chunk.PageLabel, citation.PageLabel);
        Assert.Equal(chunk.Section, citation.Section);
        Assert.Equal(chunk.Content, citation.Content);
    }

    [Fact]
    public async Task SaveChunksAsync_WhenASourceIsIngestedAgain_ReplacesItsChunks()
    {
        var (source, _, oldChunk) = await SeedCitedSourceAsync();
        var repository = new SourceRepository(_db);
        var first = NewChunk(source.Id, "Eigenvalues, reworded.");
        var second = NewChunk(source.Id, "Eigenvectors keep their direction.", chunkIndex: 1);

        await repository.SaveChunksAsync(source.Id, [first, second], default);

        _db.ChangeTracker.Clear();
        var contents = await _db.SourceChunks.Where(c => c.SourceId == source.Id).OrderBy(c => c.ChunkIndex).Select(c => c.Content).ToListAsync();
        Assert.Equal(["Eigenvalues, reworded.", "Eigenvectors keep their direction."], contents);
        Assert.False(await _db.SourceChunks.AnyAsync(c => c.Id == oldChunk.Id));
    }

    [Fact]
    public async Task SaveChunksAsync_WhenTheNewChunksAreRejected_KeepsTheOldOnes()
    {
        var (source, _, oldChunk) = await SeedCitedSourceAsync();
        var repository = new SourceRepository(_db);

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChunksAsync(
            source.Id, [NewChunk(source.Id, "One"), NewChunk(source.Id, "Same index")], default));

        Assert.True(await repository.HasChunksAsync(source.Id, default));
        Assert.True(await repository.SetStatusAsync(source.Id, SourceStatus.Ready, "Processing again failed", default));

        _db.ChangeTracker.Clear();
        var chunk = await _db.SourceChunks.SingleAsync(c => c.SourceId == source.Id);
        Assert.Equal(oldChunk.Id, chunk.Id);
        Assert.Equal(1, await _db.MessageCitations.CountAsync(c => c.SourceChunkId == oldChunk.Id));
        var reloaded = await _db.Sources.SingleAsync(s => s.Id == source.Id);
        Assert.Equal(SourceStatus.Ready, reloaded.Status);
        Assert.Equal("Processing again failed", reloaded.FailureReason);
    }

    [Fact]
    public async Task SaveChunksAsync_WhenTheSourceWasDeletedMidIngestion_FailsAndLeavesNothing()
    {
        var (source, _, _) = await SeedCitedSourceAsync();
        var repository = new SourceRepository(_db);
        Assert.NotNull(await repository.FindByIdAsync(source.Id, default));

        Assert.True(await repository.DeleteForUserAsync(source.Id, source.UserId, default));

        await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveChunksAsync(source.Id, [NewChunk(source.Id, "Late chunk")], default));
        Assert.False(await repository.HasChunksAsync(source.Id, default));
        Assert.False(await repository.SetStatusAsync(source.Id, SourceStatus.Failed, "late", default));

        _db.ChangeTracker.Clear();
        Assert.False(await _db.Sources.AnyAsync(s => s.Id == source.Id));
        Assert.False(await _db.SourceChunks.AnyAsync(c => c.SourceId == source.Id));
    }

    [Fact]
    public async Task SetStatusAsync_ReportsWhetherTheSourceStillExists()
    {
        var (source, _, _) = await SeedCitedSourceAsync();
        var repository = new SourceRepository(_db);

        Assert.True(await repository.SetStatusAsync(source.Id, SourceStatus.Processing, null, default));
        Assert.False(await repository.SetStatusAsync(Guid.NewGuid(), SourceStatus.Ready, null, default));

        Assert.Equal(SourceStatus.Processing, (await _db.Sources.AsNoTracking().SingleAsync(s => s.Id == source.Id)).Status);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheSourceWithItsChunksCitationsLinksAndFile()
    {
        var (source, message, _) = await SeedCitedSourceAsync();
        var notebook = new Notebook { Id = Guid.NewGuid(), UserId = source.UserId, Name = "Maths" };
        var tag = new Tag { Id = Guid.NewGuid(), UserId = source.UserId, Name = "algebra" };
        var concept = new Concept { Id = Guid.NewGuid(), UserId = source.UserId, Name = "Eigenvalues", NormalizedName = "eigenvalues" };
        var passage = new ConceptPassage { Id = Guid.NewGuid(), ConceptId = concept.Id, SourceId = source.Id, PageNumber = 14, Snippet = "Eigenvalues" };
        var question = new Question
        {
            Id = Guid.NewGuid(),
            UserId = source.UserId,
            ConceptId = concept.Id,
            Prompt = "What does an eigenvalue scale?",
            Answer = "Its eigenvector.",
            SourceId = source.Id,
            PageNumber = 14
        };
        _db.AddRange(notebook, tag, concept, passage, question);
        _db.NotebookSources.Add(new NotebookSource { NotebookId = notebook.Id, SourceId = source.Id });
        _db.SourceTags.Add(new SourceTag { SourceId = source.Id, TagId = tag.Id });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var blobRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var storage = new LocalBlobStorage(blobRoot);
            await storage.WriteAsync(source.BlobKey, new MemoryStream([1, 2, 3]));
            var service = new SourceService(new SourceRepository(_db), storage, new NoQueue(), new FixedUser(source.UserId), TimeProvider.System);

            await Assert.ThrowsAsync<NotFoundException>(() =>
                new SourceService(new SourceRepository(_db), storage, new NoQueue(), new FixedUser(Guid.NewGuid()), TimeProvider.System)
                    .DeleteAsync(source.Id, default));
            Assert.True(await storage.ExistsAsync(source.BlobKey));

            await service.DeleteAsync(source.Id, default);

            _db.ChangeTracker.Clear();
            Assert.False(await _db.Sources.AnyAsync(s => s.Id == source.Id));
            Assert.False(await _db.SourceChunks.AnyAsync(c => c.SourceId == source.Id));
            Assert.False(await _db.MessageCitations.AnyAsync(c => c.MessageId == message.Id));
            Assert.False(await _db.NotebookSources.AnyAsync(ns => ns.SourceId == source.Id));
            Assert.False(await _db.SourceTags.AnyAsync(st => st.SourceId == source.Id));
            Assert.False(await _db.ConceptPassages.AnyAsync(p => p.Id == passage.Id));
            Assert.True(await _db.Messages.AnyAsync(m => m.Id == message.Id));
            Assert.True(await _db.Notebooks.AnyAsync(n => n.Id == notebook.Id));
            var keptQuestion = await _db.Questions.SingleAsync(q => q.Id == question.Id);
            Assert.Null(keptQuestion.SourceId);
            Assert.False(await storage.ExistsAsync(source.BlobKey));
        }
        finally
        {
            Directory.Delete(blobRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ListAndScope_UseEachSourcesTags()
    {
        var user = new User { Id = Guid.NewGuid(), Email = "a@test.com", DisplayName = "User A", PasswordHash = "hash" };
        Source NewSource(string title, SourceStatus status = SourceStatus.Ready) => new()
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = title,
            FileName = $"{title}.md",
            ContentType = "text/markdown",
            BlobKey = $"sources/{title}.md",
            Status = status
        };
        var tides = NewSource("tides");
        var waves = NewSource("waves");
        var rocks = NewSource("rocks");
        var pending = NewSource("pending", SourceStatus.Pending);
        var notebook = new Notebook { Id = Guid.NewGuid(), UserId = user.Id, Name = "Oceans" };
        var sea = new Tag { Id = Guid.NewGuid(), UserId = user.Id, Name = "sea" };
        var land = new Tag { Id = Guid.NewGuid(), UserId = user.Id, Name = "land" };
        _db.Users.Add(user);
        _db.AddRange(tides, waves, rocks, pending, notebook, sea, land);
        _db.NotebookSources.AddRange(
            new NotebookSource { NotebookId = notebook.Id, SourceId = tides.Id },
            new NotebookSource { NotebookId = notebook.Id, SourceId = waves.Id });
        _db.SourceTags.AddRange(
            new SourceTag { SourceId = tides.Id, TagId = sea.Id },
            new SourceTag { SourceId = rocks.Id, TagId = land.Id },
            new SourceTag { SourceId = pending.Id, TagId = sea.Id });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        var repository = new SourceRepository(_db);

        var listed = await repository.ListForUserAsync(user.Id, default);
        Assert.Equal([sea.Id], listed.Single(s => s.Id == tides.Id).TagIds);
        Assert.Empty(listed.Single(s => s.Id == waves.Id).TagIds);
        Assert.Equal([land.Id], (await repository.FindByIdForUserAsync(rocks.Id, user.Id, default))!.TagIds);

        async Task<IReadOnlyList<Guid>?> Scope(Guid[] notebookIds, Guid[] tagIds) =>
            await repository.FindReadySourceIdsForScopeAsync(user.Id, notebookIds, tagIds, [], default);

        Assert.Equal([tides.Id], await Scope([], [sea.Id]));
        Assert.Equal(
            new[] { tides.Id, rocks.Id }.Order(),
            (await Scope([], [sea.Id, land.Id]))!.Order());
        Assert.Equal([tides.Id], await Scope([notebook.Id], [sea.Id]));
        Assert.Empty((await Scope([notebook.Id], [land.Id]))!);
    }

    private async Task<(Source Source, Message Message, SourceChunk Chunk)> SeedCitedSourceAsync()
    {
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
        var chunk = NewChunk(source.Id, "Eigenvalues scale eigenvectors.");
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            Title = "eigenvalues",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var message = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversation.Id,
            Role = MessageRole.Assistant,
            Content = "They scale eigenvectors [1].",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        message.Citations.Add(new MessageCitation
        {
            MessageId = message.Id,
            Number = 1,
            SourceChunkId = chunk.Id,
            SourceId = source.Id,
            PageNumber = chunk.PageNumber,
            PageLabel = chunk.PageLabel,
            Section = chunk.Section,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content
        });
        _db.Users.Add(user);
        _db.Sources.Add(source);
        _db.SourceChunks.Add(chunk);
        _db.Conversations.Add(conversation);
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return (source, message, chunk);
    }

    private static SourceChunk NewChunk(Guid sourceId, string content, int chunkIndex = 0) => new()
    {
        Id = Guid.CreateVersion7(),
        SourceId = sourceId,
        PageNumber = 14,
        PageLabel = "xii",
        Section = "Eigenvalues",
        ChunkIndex = chunkIndex,
        Content = content,
        Embedding = new float[SourceChunk.EmbeddingDimensions],
        EmbeddingModel = "nomic-embed-text"
    };

    private sealed class FixedUser(Guid userId) : ICurrentUser
    {
        public Guid UserId => userId;
    }

    private sealed class NoQueue : ISourceIngestionQueue
    {
        public ValueTask EnqueueAsync(Guid sourceId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
