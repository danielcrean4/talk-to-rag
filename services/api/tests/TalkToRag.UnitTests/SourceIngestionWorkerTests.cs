using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;
using TalkToRag.Infrastructure.Ai;
using TalkToRag.Infrastructure.Ingestion;

namespace TalkToRag.UnitTests;

public class SourceIngestionWorkerTests
{
    [Fact]
    public async Task Worker_ProcessesJob_AndMarksSourceReady()
    {
        var sourceId = Guid.NewGuid();
        var source = new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(),
            Title = "TD Learning",
            FileName = "td.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/td.pdf",
            Status = SourceStatus.Pending
        };

        var repo = new FakeSourceRepo([source]);
        var fakeAi = new FakeAiClient(new IngestSourceResponse([
            new IngestedChunkDto(1, "1", null, 0, "TD learning content", [0.1f, 0.2f])
        ]));
        var queue = new ChannelSourceIngestionQueue();
        await queue.EnqueueAsync(sourceId);

        var services = new ServiceCollection();
        services.AddSingleton<ISourceRepository>(repo);
        services.AddSingleton<IAiClient>(fakeAi);
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        var worker = new SourceIngestionWorker(provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<SourceIngestionWorker>.Instance);

        var runTask = worker.StartAsync(cts.Token);
        for (var i = 0; i < 20 && source.Status != SourceStatus.Ready; i++)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await runTask; } catch { /* cancellation expected */ }

        Assert.Equal(SourceStatus.Ready, source.Status);
        Assert.Null(source.FailureReason);
        var chunk = Assert.Single(source.Chunks);
        Assert.Equal(1, chunk.PageNumber);
        Assert.Equal("TD learning content", chunk.Content);
        Assert.Equal([0.1f, 0.2f], chunk.Embedding);
    }

    [Fact]
    public async Task Worker_SetsFailedStatus_WhenAiServiceRejects()
    {
        var sourceId = Guid.NewGuid();
        var source = new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(),
            Title = "Scanned Doc",
            FileName = "scanned.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/scanned.pdf",
            Status = SourceStatus.Pending
        };

        var repo = new FakeSourceRepo([source]);
        var fakeAi = new FakeAiClient(new AiIngestionException("No text layer found. This looks like a scanned PDF and needs OCR."));
        var queue = new ChannelSourceIngestionQueue();
        await queue.EnqueueAsync(sourceId);

        var services = new ServiceCollection();
        services.AddSingleton<ISourceRepository>(repo);
        services.AddSingleton<IAiClient>(fakeAi);
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        var worker = new SourceIngestionWorker(provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<SourceIngestionWorker>.Instance);

        var runTask = worker.StartAsync(cts.Token);
        for (var i = 0; i < 20 && source.Status != SourceStatus.Failed; i++)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await runTask; } catch { /* cancellation expected */ }

        Assert.Equal(SourceStatus.Failed, source.Status);
        Assert.Equal("No text layer found. This looks like a scanned PDF and needs OCR.", source.FailureReason);
        Assert.Empty(source.Chunks);
    }

    [Fact]
    public async Task Worker_ReEnqueuesUnfinishedSourcesOnStartup()
    {
        var pendingId = Guid.NewGuid();
        var processingId = Guid.NewGuid();
        var readyId = Guid.NewGuid();

        var repo = new FakeSourceRepo([
            new Source { Id = pendingId, UserId = Guid.NewGuid(), Title = "A", FileName = "a.txt", ContentType = "text/plain", BlobKey = "a", Status = SourceStatus.Pending },
            new Source { Id = processingId, UserId = Guid.NewGuid(), Title = "B", FileName = "b.txt", ContentType = "text/plain", BlobKey = "b", Status = SourceStatus.Processing },
            new Source { Id = readyId, UserId = Guid.NewGuid(), Title = "C", FileName = "c.txt", ContentType = "text/plain", BlobKey = "c", Status = SourceStatus.Ready }
        ]);

        var queue = new ChannelSourceIngestionQueue();
        var services = new ServiceCollection();
        services.AddSingleton<ISourceRepository>(repo);
        services.AddSingleton<IAiClient>(new FakeAiClient(new IngestSourceResponse([])));
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        var worker = new SourceIngestionWorker(provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<SourceIngestionWorker>.Instance);
        await worker.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        var sourceA = await repo.FindByIdAsync(pendingId, CancellationToken.None);
        var sourceB = await repo.FindByIdAsync(processingId, CancellationToken.None);
        while (DateTime.UtcNow < deadline && (sourceA!.Status != SourceStatus.Ready || sourceB!.Status != SourceStatus.Ready))
        {
            await Task.Delay(50);
        }

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Ready, sourceA!.Status);
        Assert.Equal(SourceStatus.Ready, sourceB!.Status);
    }

    [Fact]
    public async Task HttpAiClient_ThrowsAiIngestionException_On422WithDetail()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.UnprocessableEntity,
            """{"detail": "No text layer found. This looks like a scanned PDF and needs OCR."}""");
        var client = new HttpAiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000") });

        var ex = await Assert.ThrowsAsync<AiIngestionException>(() =>
            client.IngestSourceAsync(new IngestSourceRequest(Guid.NewGuid(), "key", "application/pdf")));

        Assert.Equal("No text layer found. This looks like a scanned PDF and needs OCR.", ex.Message);
    }

    [Fact]
    public async Task HttpAiClient_ThrowsAiServiceUnavailableException_On503WithDetail()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.ServiceUnavailable,
            """{"detail": "Ollama connection refused at http://localhost:11434"}""");
        var client = new HttpAiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000") });

        var ex = await Assert.ThrowsAsync<AiServiceUnavailableException>(() =>
            client.IngestSourceAsync(new IngestSourceRequest(Guid.NewGuid(), "key", "application/pdf")));

        Assert.Equal("Ollama connection refused at http://localhost:11434", ex.Message);
    }

    [Fact]
    public async Task Worker_MarksSourceFailed_WhenAiServiceUnavailable()
    {
        var sourceId = Guid.NewGuid();
        var source = new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(),
            Title = "TD Learning",
            FileName = "td.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/td.pdf",
            Status = SourceStatus.Pending
        };

        var repo = new FakeSourceRepo([source]);
        var fakeAi = new FakeAiClient(new AiServiceUnavailableException("Ollama connection refused"));
        var queue = new ChannelSourceIngestionQueue();
        await queue.EnqueueAsync(sourceId);

        var services = new ServiceCollection();
        services.AddSingleton<ISourceRepository>(repo);
        services.AddSingleton<IAiClient>(fakeAi);
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        var worker = new SourceIngestionWorker(provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<SourceIngestionWorker>.Instance);

        var runTask = worker.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && source.Status == SourceStatus.Pending)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await runTask; } catch { }

        Assert.Equal(SourceStatus.Failed, source.Status);
        Assert.Contains("Embedding service unavailable: Ollama connection refused", source.FailureReason);
        Assert.Empty(source.Chunks);
    }

    [Fact]
    public async Task Worker_ReIngest_ReplacesOldChunksWithoutDuplicateConstraint()
    {
        var sourceId = Guid.NewGuid();
        var source = new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(),
            Title = "TD Learning",
            FileName = "td.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/td.pdf",
            Status = SourceStatus.Pending
        };
        source.Chunks.Add(new SourceChunk
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            PageNumber = 1,
            ChunkIndex = 0,
            Content = "Old partial chunk",
            Embedding = [0.0f]
        });

        var repo = new FakeSourceRepo([source]);
        var fakeAi = new FakeAiClient(new IngestSourceResponse([
            new IngestedChunkDto(1, null, null, 0, "New chunk 0", [0.1f, 0.2f]),
            new IngestedChunkDto(1, null, null, 1, "New chunk 1", [0.3f, 0.4f])
        ]));
        var queue = new ChannelSourceIngestionQueue();
        await queue.EnqueueAsync(sourceId);

        var services = new ServiceCollection();
        services.AddSingleton<ISourceRepository>(repo);
        services.AddSingleton<IAiClient>(fakeAi);
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        var worker = new SourceIngestionWorker(provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<SourceIngestionWorker>.Instance);

        var runTask = worker.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && source.Status != SourceStatus.Ready)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await runTask; } catch { }

        Assert.Equal(SourceStatus.Ready, source.Status);
        Assert.Equal(2, source.Chunks.Count);
        Assert.Equal("New chunk 0", source.Chunks.First().Content);
        Assert.Equal("New chunk 1", source.Chunks.Last().Content);
    }

    [Fact]
    public async Task Worker_FailedReIngest_KeepsOldChunks_AndLeavesSourceReady()
    {
        var sourceId = Guid.NewGuid();
        var source = new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(),
            Title = "TD Learning",
            FileName = "td.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/td.pdf",
            Status = SourceStatus.Pending
        };
        source.Chunks.Add(new SourceChunk { Id = Guid.NewGuid(), SourceId = sourceId, ChunkIndex = 0, Content = "Old chunk", Embedding = [0.0f] });

        var repo = new FakeSourceRepo([source]);
        var queue = new ChannelSourceIngestionQueue();
        await queue.EnqueueAsync(sourceId);
        var worker = CreateWorker(repo, new FakeAiClient(new AiServiceUnavailableException("Ollama connection refused")), queue);

        using var cts = new CancellationTokenSource();
        var runTask = worker.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && source.FailureReason is null)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await runTask; } catch { }

        Assert.Equal(SourceStatus.Ready, source.Status);
        Assert.Contains("earlier passages are kept", source.FailureReason);
        Assert.Contains("Ollama connection refused", source.FailureReason);
        Assert.Equal("Old chunk", Assert.Single(source.Chunks).Content);
    }

    [Fact]
    public async Task Worker_SkipsSourceDeletedDuringIngestion_AndKeepsGoing()
    {
        var deletedId = Guid.NewGuid();
        var nextId = Guid.NewGuid();
        var deleted = new Source { Id = deletedId, UserId = Guid.NewGuid(), Title = "A", FileName = "a.txt", ContentType = "text/plain", BlobKey = "a", Status = SourceStatus.Pending };
        var next = new Source { Id = nextId, UserId = Guid.NewGuid(), Title = "B", FileName = "b.txt", ContentType = "text/plain", BlobKey = "b", Status = SourceStatus.Pending };

        var repo = new FakeSourceRepo([deleted, next]);
        var fakeAi = new FakeAiClient(request =>
        {
            if (request.SourceId == deletedId)
            {
                repo.DeleteForUserAsync(deletedId, deleted.UserId, CancellationToken.None).GetAwaiter().GetResult();
            }

            return new IngestSourceResponse([new IngestedChunkDto(1, null, null, 0, "Content", [0.1f])]);
        });
        var queue = new ChannelSourceIngestionQueue();
        await queue.EnqueueAsync(deletedId);
        await queue.EnqueueAsync(nextId);
        var worker = CreateWorker(repo, fakeAi, queue);

        using var cts = new CancellationTokenSource();
        var runTask = worker.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && next.Status != SourceStatus.Ready)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await runTask; } catch { }

        Assert.Null(await repo.FindByIdAsync(deletedId, CancellationToken.None));
        Assert.Empty(deleted.Chunks);
        Assert.Equal(SourceStatus.Ready, next.Status);
    }

    private static SourceIngestionWorker CreateWorker(ISourceRepository repo, IAiClient aiClient, ISourceIngestionQueue queue)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        services.AddSingleton(aiClient);
        var provider = services.BuildServiceProvider();
        return new SourceIngestionWorker(provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<SourceIngestionWorker>.Instance);
    }

    [Fact]
    public async Task Worker_ContinuesProcessing_WhenDatabaseFailsOnInitialFind()
    {
        var failedId = Guid.NewGuid();
        var successId = Guid.NewGuid();
        var successSource = new Source
        {
            Id = successId,
            UserId = Guid.NewGuid(),
            Title = "Success Book",
            FileName = "success.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/success.pdf",
            Status = SourceStatus.Pending
        };

        var repo = new CrashingOnFirstFindRepo([successSource], failedId);
        var fakeAi = new FakeAiClient(new IngestSourceResponse([
            new IngestedChunkDto(1, null, null, 0, "Content", [0.1f])
        ]));
        var queue = new ChannelSourceIngestionQueue();
        await queue.EnqueueAsync(failedId);
        await queue.EnqueueAsync(successId);

        var services = new ServiceCollection();
        services.AddSingleton<ISourceRepository>(repo);
        services.AddSingleton<IAiClient>(fakeAi);
        var provider = services.BuildServiceProvider();

        using var cts = new CancellationTokenSource();
        var worker = new SourceIngestionWorker(provider.GetRequiredService<IServiceScopeFactory>(), queue, NullLogger<SourceIngestionWorker>.Instance);

        var runTask = worker.StartAsync(cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && successSource.Status != SourceStatus.Ready)
        {
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await runTask; } catch { }

        // The worker kept running after the first job failed.
        Assert.Equal(SourceStatus.Ready, successSource.Status);
    }

    [Fact]
    public async Task HttpAiClient_ParsesSuccessResponse()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK,
            """
            {
              "chunks": [
                {
                  "page_number": 3,
                  "page_label": "iii",
                  "section": "Heading 1 > Subheading",
                  "chunk_index": 0,
                  "content": "some chunk content",
                  "embedding": [0.1, 0.2, 0.3]
                }
              ]
            }
            """);
        var client = new HttpAiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000") });

        var response = await client.IngestSourceAsync(new IngestSourceRequest(Guid.NewGuid(), "key", "text/markdown"));

        var chunk = Assert.Single(response.Chunks);
        Assert.Equal(3, chunk.PageNumber);
        Assert.Equal("iii", chunk.PageLabel);
        Assert.Equal("Heading 1 > Subheading", chunk.Section);
        Assert.Equal(0, chunk.ChunkIndex);
        Assert.Equal("some chunk content", chunk.Content);
        Assert.Equal([0.1f, 0.2f, 0.3f], chunk.Embedding);
    }

    private class FakeSourceRepo(IEnumerable<Source> sources) : ISourceRepository
    {
        private readonly List<Source> _all = [.. sources];

        public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken)
        {
            var s = _all.FirstOrDefault(s => s.Id == id && s.UserId == userId);
            if (s is null) return Task.FromResult<SourceDto?>(null);
            return Task.FromResult<SourceDto?>(new SourceDto(
                s.Id, s.Title, s.FileName, s.ContentType, s.SizeBytes, s.Status.ToString(), s.FailureReason, s.Chunks.Count, s.CreatedAtUtc, []));
        }

        public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.FirstOrDefault(s => s.Id == id && s.UserId == userId));

        public virtual Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_all.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceDto>>([]);

        public Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(_all.Where(s => s.Status == SourceStatus.Pending || s.Status == SourceStatus.Processing).Select(s => s.Id).ToList());

        public Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>?>(null);

        public Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken)
        {
            var s = _all.FirstOrDefault(s => s.Id == sourceId)
                ?? throw new InvalidOperationException("Foreign key violation: the source no longer exists.");
            s.Chunks.Clear();
            s.Chunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task AddAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateAsync(Source source, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken)
        {
            var source = _all.FirstOrDefault(s => s.Id == id);
            if (source is null) return Task.FromResult(false);
            source.Status = status;
            source.FailureReason = failureReason;
            return Task.FromResult(true);
        }

        public Task<bool> SetDetailsForUserAsync(Guid id, Guid userId, string? license, string? sourceUrl, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_all.Any(s => s.Id == id && s.Chunks.Count > 0));

        public Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(_all.RemoveAll(s => s.Id == id && s.UserId == userId) > 0);
    }

    private sealed class CrashingOnFirstFindRepo(IEnumerable<Source> sources, Guid crashingId) : FakeSourceRepo(sources)
    {
        public override Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            if (id == crashingId) throw new InvalidOperationException("Postgres connection dropped.");
            return base.FindByIdAsync(id, cancellationToken);
        }
    }

    private sealed class FakeAiClient : IAiClient
    {
        private readonly Func<IngestSourceRequest, IngestSourceResponse> _ingest;

        public FakeAiClient(IngestSourceResponse response) => _ingest = _ => response;
        public FakeAiClient(Exception exception) => _ingest = _ => throw exception;
        public FakeAiClient(Func<IngestSourceRequest, IngestSourceResponse> ingest) => _ingest = ingest;

        public Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_ingest(request));

        public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExpandQueryResponse([]));

        public Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AnswerResponse("", []));

        public async IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        public Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<SynthesizeResponse> SynthesizeAsync(SynthesizeRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
