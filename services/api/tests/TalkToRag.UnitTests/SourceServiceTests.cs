using System.Text;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.UnitTests;

public class SourceServiceTests
{
    private readonly InMemorySourceRepository _repository = new();
    private readonly InMemoryBlobStorage _storage = new();
    private readonly InMemoryIngestionQueue _queue = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };
    private readonly FixedClock _clock = new();

    private SourceService CreateService() =>
        new(_repository, _storage, _queue, _currentUser, _clock);

    [Fact]
    public async Task UploadAsync_StoresBlob_CreatesPendingSource_AndEnqueuesJob()
    {
        var service = CreateService();
        var content = Encoding.UTF8.GetBytes("# Introduction to RAG\n\nSome text content.");
        using var stream = new MemoryStream(content);

        var response = await service.UploadAsync("textbook.md", stream, null, null, default);

        Assert.Equal("Pending", response.Status);
        Assert.Equal("textbook", response.Title);

        var stored = Assert.Single(_repository.All);
        Assert.Equal(response.Id, stored.Id);
        Assert.Equal(_currentUser.UserId, stored.UserId);
        Assert.Equal("textbook.md", stored.FileName);
        Assert.Equal("text/markdown", stored.ContentType);
        Assert.Equal(content.Length, stored.SizeBytes);
        Assert.NotNull(stored.FileHash);
        Assert.Equal(64, stored.FileHash.Length);
        Assert.Equal(SourceStatus.Pending, stored.Status);

        Assert.True(_storage.Written.ContainsKey(stored.BlobKey));
        Assert.Equal(content, _storage.Written[stored.BlobKey]);

        var enqueued = Assert.Single(_queue.Enqueued);
        Assert.Equal(response.Id, enqueued);
    }

    [Theory]
    [InlineData("test.exe")]
    [InlineData("doc.docx")]
    [InlineData("image.png")]
    [InlineData("no-extension")]
    public async Task UploadAsync_RejectsUnsupportedFileExtension(string fileName)
    {
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UploadAsync(fileName, stream, null, null, default));

        Assert.Empty(_repository.All);
        Assert.Empty(_queue.Enqueued);
    }

    [Fact]
    public async Task UploadAsync_StoresLicenseAndSourceUrl_Trimmed()
    {
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        await service.UploadAsync("notes.txt", stream, "  CC BY 4.0 ", " https://example.org/notes.txt ", default);

        var stored = Assert.Single(_repository.All);
        Assert.Equal("CC BY 4.0", stored.License);
        Assert.Equal("https://example.org/notes.txt", stored.SourceUrl);
    }

    [Fact]
    public async Task UploadAsync_StoresBlankLicenseAndSourceUrl_AsNull()
    {
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        await service.UploadAsync("notes.txt", stream, "   ", "", default);

        var stored = Assert.Single(_repository.All);
        Assert.Null(stored.License);
        Assert.Null(stored.SourceUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.org/book.pdf")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//example.org/book.pdf")]
    [InlineData("/books/book.pdf")]
    [InlineData("example.org/book.pdf")]
    [InlineData("data:text/html,hi")]
    [InlineData("http://")]
    public async Task UploadAsync_RejectsSourceUrlThatIsNotAbsoluteHttp_BeforeWritingAnything(string sourceUrl)
    {
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UploadAsync("notes.txt", stream, null, sourceUrl, default));

        Assert.Empty(_repository.All);
        Assert.Empty(_storage.Written);
        Assert.Empty(_queue.Enqueued);
    }

    [Fact]
    public async Task UploadAsync_RejectsOverlongLicense()
    {
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UploadAsync("notes.txt", stream, new string('a', Source.MaxLicenseLength + 1), null, default));

        Assert.Empty(_storage.Written);
    }

    [Fact]
    public async Task UpdateAsync_SetsLicenseAndSourceUrl()
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, SourceStatus.Ready);

        var result = await service.UpdateAsync(source.Id, new UpdateSourceRequest(" Public domain ", "http://example.org/a?b=c"), default);

        Assert.Equal("Public domain", result.License);
        Assert.Equal("http://example.org/a?b=c", result.SourceUrl);
        Assert.Equal("Public domain", source.License);
        Assert.Equal("http://example.org/a?b=c", source.SourceUrl);
    }

    [Fact]
    public async Task UpdateAsync_KeepsAFieldLeftNull_AndClearsABlankOne()
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, SourceStatus.Ready);
        source.License = "CC BY 4.0";
        source.SourceUrl = "https://example.org/book.pdf";

        var result = await service.UpdateAsync(source.Id, new UpdateSourceRequest(null, "  "), default);

        Assert.Equal("CC BY 4.0", result.License);
        Assert.Null(result.SourceUrl);
        Assert.Equal("CC BY 4.0", source.License);
        Assert.Null(source.SourceUrl);
    }

    [Fact]
    public async Task UpdateAsync_AcceptsFieldsAtTheirMaximumLength()
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, SourceStatus.Ready);
        var license = new string('a', Source.MaxLicenseLength);
        var url = "https://example.org/" + new string('p', Source.MaxSourceUrlLength - "https://example.org/".Length);

        var result = await service.UpdateAsync(source.Id, new UpdateSourceRequest(license, url), default);

        Assert.Equal(license, result.License);
        Assert.Equal(url, result.SourceUrl);
    }

    [Fact]
    public async Task UpdateAsync_RejectsOverlongFields_AndChangesNothing()
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, SourceStatus.Ready);
        source.License = "CC0";
        var longUrl = "https://example.org/" + new string('p', Source.MaxSourceUrlLength);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UpdateAsync(source.Id, new UpdateSourceRequest(new string('a', Source.MaxLicenseLength + 1), null), default));
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UpdateAsync(source.Id, new UpdateSourceRequest(null, longUrl), default));

        Assert.Equal("CC0", source.License);
        Assert.Null(source.SourceUrl);
    }

    [Fact]
    public async Task UpdateAsync_RejectsSourceUrlWithAnotherScheme()
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, SourceStatus.Ready);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UpdateAsync(source.Id, new UpdateSourceRequest(null, "javascript:alert(1)"), default));

        Assert.Null(source.SourceUrl);
    }

    [Fact]
    public async Task UpdateAsync_ThrowsNotFound_WhenOwnedByAnotherUser()
    {
        var service = CreateService();
        var source = AddSource(Guid.NewGuid(), SourceStatus.Ready);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateAsync(source.Id, new UpdateSourceRequest("CC0", null), default));

        Assert.Null(source.License);
    }

    [Fact]
    public async Task UploadAsync_RejectsEmptyFile()
    {
        var service = CreateService();
        using var stream = new MemoryStream([]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.UploadAsync("notes.txt", stream, null, null, default));
    }

    [Fact]
    public async Task UploadAsync_RequiresAuthenticatedUser()
    {
        var service = CreateService();
        using var stream = new MemoryStream([1, 2, 3]);

        // Like ClaimsCurrentUser on a request with no signed-in user.
        _currentUser.ThrowOnAccess = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UploadAsync("notes.txt", stream, null, null, default));
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsSource_WhenOwnedByCaller()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var source = new Source
        {
            Id = sourceId,
            UserId = _currentUser.UserId,
            Title = "TD Learning",
            FileName = "td.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            BlobKey = "sources/td.pdf",
            Status = SourceStatus.Ready,
            CreatedAtUtc = _clock.GetUtcNow()
        };
        source.Chunks.Add(new SourceChunk { Id = Guid.NewGuid(), SourceId = sourceId, Content = "abc", Embedding = [0.1f] });
        _repository.All.Add(source);

        var result = await service.GetByIdAsync(sourceId, default);

        Assert.Equal(sourceId, result.Id);
        Assert.Equal("TD Learning", result.Title);
        Assert.Equal("Ready", result.Status);
        Assert.Equal(1, result.ChunkCount);
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsNotFound_WhenOwnedByAnotherUser()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        _repository.All.Add(new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(), // Different user!
            Title = "Other user notes",
            FileName = "other.pdf",
            ContentType = "application/pdf",
            SizeBytes = 1024,
            BlobKey = "sources/other.pdf",
            Status = SourceStatus.Ready,
            CreatedAtUtc = _clock.GetUtcNow()
        });

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(sourceId, default));
    }

    [Fact]
    public async Task RetryAsync_ReEnqueuesFailedSource_AndSetsStatusToPending()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        _repository.All.Add(new Source
        {
            Id = sourceId,
            UserId = _currentUser.UserId,
            Title = "Failed Book",
            FileName = "book.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/book.pdf",
            Status = SourceStatus.Failed,
            FailureReason = "Model server down"
        });

        await service.RetryAsync(sourceId, default);

        var source = _repository.All.Single(s => s.Id == sourceId);
        Assert.Equal(SourceStatus.Pending, source.Status);
        Assert.Null(source.FailureReason);
        Assert.Contains(sourceId, _queue.Enqueued);
    }

    [Fact]
    public async Task RetryAsync_ReEnqueuesReadySource()
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, SourceStatus.Ready);

        await service.RetryAsync(source.Id, default);

        Assert.Equal(SourceStatus.Pending, source.Status);
        Assert.Contains(source.Id, _queue.Enqueued);
    }

    [Theory]
    [InlineData(SourceStatus.Pending)]
    [InlineData(SourceStatus.Processing)]
    public async Task RetryAsync_RefusesSourceAlreadyQueued(SourceStatus status)
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, status);

        await Assert.ThrowsAsync<ConflictException>(() => service.RetryAsync(source.Id, default));

        Assert.Equal(status, source.Status);
        Assert.Empty(_queue.Enqueued);
    }

    [Fact]
    public async Task DeleteAsync_RemovesSourceAndBlob()
    {
        var service = CreateService();
        var source = AddSource(_currentUser.UserId, SourceStatus.Ready);
        _storage.Written[source.BlobKey] = [1, 2, 3];

        await service.DeleteAsync(source.Id, default);

        Assert.Empty(_repository.All);
        Assert.False(_storage.Written.ContainsKey(source.BlobKey));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsNotFound_AndKeepsEverything_WhenOwnedByOtherUser()
    {
        var service = CreateService();
        var source = AddSource(Guid.NewGuid(), SourceStatus.Ready);
        _storage.Written[source.BlobKey] = [1, 2, 3];

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(source.Id, default));

        Assert.Single(_repository.All);
        Assert.True(_storage.Written.ContainsKey(source.BlobKey));
    }

    [Fact]
    public async Task DeleteAsync_ThrowsNotFound_WhenMissing()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<NotFoundException>(() => service.DeleteAsync(Guid.NewGuid(), default));
    }

    private Source AddSource(Guid userId, SourceStatus status)
    {
        var id = Guid.NewGuid();
        var source = new Source
        {
            Id = id,
            UserId = userId,
            Title = "Book",
            FileName = "book.pdf",
            ContentType = "application/pdf",
            BlobKey = $"sources/{id:N}.pdf",
            Status = status
        };
        _repository.All.Add(source);
        return source;
    }

    [Fact]
    public async Task RetryAsync_ThrowsNotFound_WhenOwnedByOtherUser()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        _repository.All.Add(new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(), // different user
            Title = "Failed Book",
            FileName = "book.pdf",
            ContentType = "application/pdf",
            BlobKey = "sources/book.pdf",
            Status = SourceStatus.Failed
        });

        await Assert.ThrowsAsync<NotFoundException>(() => service.RetryAsync(sourceId, default));
    }

    [Fact]
    public async Task GetFileAsync_ReturnsStreamAndMetadata_WhenUserOwnsSource()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("%PDF-1.4 sample content");
        var blobKey = $"sources/{sourceId:N}.pdf";
        _storage.Written[blobKey] = content;

        _repository.All.Add(new Source
        {
            Id = sourceId,
            UserId = _currentUser.UserId,
            Title = "CS229 Notes",
            FileName = "cs229-notes.pdf",
            ContentType = "application/pdf",
            SizeBytes = content.Length,
            BlobKey = blobKey,
            Status = SourceStatus.Ready,
            CreatedAtUtc = _clock.GetUtcNow()
        });

        var result = await service.GetFileAsync(sourceId, default);

        Assert.NotNull(result);
        Assert.Equal("cs229-notes.pdf", result.FileName);
        Assert.Equal("application/pdf", result.ContentType);
        using var ms = new MemoryStream();
        await result.Stream.CopyToAsync(ms);
        Assert.Equal(content, ms.ToArray());
    }

    [Fact]
    public async Task GetFileAsync_ThrowsNotFoundException_WhenSourceBelongsToDifferentUser()
    {
        var service = CreateService();
        var sourceId = Guid.NewGuid();
        _storage.Written[$"sources/{sourceId:N}.pdf"] = [1, 2, 3];

        _repository.All.Add(new Source
        {
            Id = sourceId,
            UserId = Guid.NewGuid(), // other user
            Title = "Other's Notes",
            FileName = "other.pdf",
            ContentType = "application/pdf",
            BlobKey = $"sources/{sourceId:N}.pdf",
            Status = SourceStatus.Ready,
            CreatedAtUtc = _clock.GetUtcNow()
        });

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetFileAsync(sourceId, default));
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlySourcesForCurrentCaller()
    {
        var service = CreateService();
        var mySource = new Source
        {
            Id = Guid.NewGuid(),
            UserId = _currentUser.UserId,
            Title = "My Notes",
            FileName = "my.md",
            ContentType = "text/markdown",
            SizeBytes = 50,
            BlobKey = "sources/my.md",
            Status = SourceStatus.Ready,
            CreatedAtUtc = _clock.GetUtcNow()
        };
        var otherSource = new Source
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Title = "Someone else's notes",
            FileName = "other.md",
            ContentType = "text/markdown",
            SizeBytes = 50,
            BlobKey = "sources/other.md",
            Status = SourceStatus.Ready,
            CreatedAtUtc = _clock.GetUtcNow()
        };

        _repository.All.Add(mySource);
        _repository.All.Add(otherSource);

        var list = await service.ListAsync(default);

        var item = Assert.Single(list);
        Assert.Equal(mySource.Id, item.Id);
    }

    private sealed class InMemorySourceRepository : ISourceRepository
    {
        public List<Source> All { get; } = [];

        public Task<SourceDto?> FindByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken)
        {
            var s = All.FirstOrDefault(s => s.Id == id && s.UserId == userId);
            if (s is null) return Task.FromResult<SourceDto?>(null);
            return Task.FromResult<SourceDto?>(new SourceDto(
                s.Id,
                s.Title,
                s.FileName,
                s.ContentType,
                s.SizeBytes,
                s.Status.ToString(),
                s.FailureReason,
                s.Chunks.Count,
                s.CreatedAtUtc, [], s.License, s.SourceUrl));
        }

        public Task<Source?> FindEntityByIdForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(s => s.Id == id && s.UserId == userId));

        public Task<Source?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(s => s.Id == id));

        public Task<IReadOnlyList<SourceDto>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            IReadOnlyList<SourceDto> result = All
                .Where(s => s.UserId == userId)
                .Select(s => new SourceDto(
                    s.Id,
                    s.Title,
                    s.FileName,
                    s.ContentType,
                    s.SizeBytes,
                    s.Status.ToString(),
                    s.FailureReason,
                    s.Chunks.Count,
                    s.CreatedAtUtc, [], s.License, s.SourceUrl))
                .ToList();
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<Guid>> FindPendingOrProcessingIdsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(All.Where(s => s.Status == SourceStatus.Pending || s.Status == SourceStatus.Processing).Select(s => s.Id).ToList());

        public Task<IReadOnlyList<Guid>?> FindReadySourceIdsForScopeAsync(Guid userId, IReadOnlyList<Guid> notebookIds, IReadOnlyList<Guid> tagIds, IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>?>(null);

        public Task SaveChunksAsync(Guid sourceId, IReadOnlyList<SourceChunk> chunks, CancellationToken cancellationToken)
        {
            var source = All.FirstOrDefault(s => s.Id == sourceId);
            if (source is not null)
            {
                source.Chunks.Clear();
                foreach (var chunk in chunks)
                {
                    source.Chunks.Add(chunk);
                }
            }
            return Task.CompletedTask;
        }

        public Task AddAsync(Source source, CancellationToken cancellationToken)
        {
            All.Add(source);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Source source, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> SetStatusAsync(Guid id, SourceStatus status, string? failureReason, CancellationToken cancellationToken)
        {
            var source = All.FirstOrDefault(s => s.Id == id);
            if (source is null) return Task.FromResult(false);
            source.Status = status;
            source.FailureReason = failureReason;
            return Task.FromResult(true);
        }

        public Task<bool> SetDetailsForUserAsync(Guid id, Guid userId, string? license, string? sourceUrl, CancellationToken cancellationToken)
        {
            var source = All.FirstOrDefault(s => s.Id == id && s.UserId == userId);
            if (source is null) return Task.FromResult(false);
            source.License = license;
            source.SourceUrl = sourceUrl;
            return Task.FromResult(true);
        }

        public Task<bool> HasChunksAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(All.Any(s => s.Id == id && s.Chunks.Count > 0));

        public Task<bool> DeleteForUserAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(All.RemoveAll(s => s.Id == id && s.UserId == userId) > 0);
    }

    private sealed class InMemoryBlobStorage : IBlobStorage
    {
        public Dictionary<string, byte[]> Written { get; } = [];

        public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, cancellationToken);
            Written[key] = ms.ToArray();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!Written.TryGetValue(key, out var bytes))
                throw new NotFoundException($"Blob {key} not found");
            return Task.FromResult<Stream>(new MemoryStream(bytes));
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Written.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Written.ContainsKey(key));
    }

    private sealed class InMemoryIngestionQueue : ISourceIngestionQueue
    {
        public List<Guid> Enqueued { get; } = [];

        public ValueTask EnqueueAsync(Guid sourceId, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(sourceId);
            return ValueTask.CompletedTask;
        }

        public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
