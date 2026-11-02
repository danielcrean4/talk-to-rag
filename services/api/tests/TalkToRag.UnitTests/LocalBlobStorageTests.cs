using System.Text;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Infrastructure.Storage;

namespace TalkToRag.UnitTests;

public class LocalBlobStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly LocalBlobStorage _storage;

    public LocalBlobStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"talk-to-rag-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _storage = new LocalBlobStorage(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task WriteAsync_And_OpenReadAsync_RoundTripsContent()
    {
        const string key = "sources/doc-123.pdf";
        var content = Encoding.UTF8.GetBytes("sample pdf binary bytes");
        using var stream = new MemoryStream(content);

        await _storage.WriteAsync(key, stream);

        Assert.True(await _storage.ExistsAsync(key));

        await using var readStream = await _storage.OpenReadAsync(key);
        using var memoryStream = new MemoryStream();
        await readStream.CopyToAsync(memoryStream);

        Assert.Equal(content, memoryStream.ToArray());
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingFileAtomically()
    {
        const string key = "sources/doc-123.txt";
        var initial = Encoding.UTF8.GetBytes("v1");
        var updated = Encoding.UTF8.GetBytes("v2-updated");

        await _storage.WriteAsync(key, new MemoryStream(initial));
        await _storage.WriteAsync(key, new MemoryStream(updated));

        await using var readStream = await _storage.OpenReadAsync(key);
        using var memoryStream = new MemoryStream();
        await readStream.CopyToAsync(memoryStream);

        Assert.Equal(updated, memoryStream.ToArray());
    }

    [Fact]
    public async Task DeleteAsync_RemovesFileIfExists()
    {
        const string key = "sources/to-delete.pdf";
        await _storage.WriteAsync(key, new MemoryStream([1, 2, 3]));
        Assert.True(await _storage.ExistsAsync(key));

        await _storage.DeleteAsync(key);
        Assert.False(await _storage.ExistsAsync(key));

        // Deleting again is a safe no-op
        await _storage.DeleteAsync(key);
    }

    [Fact]
    public async Task OpenReadAsync_ThrowsNotFoundException_WhenMissing()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _storage.OpenReadAsync("sources/missing.pdf"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/absolute/path.pdf")]
    [InlineData(@"sources\windows\path.pdf")]
    [InlineData("sources/../escaped.pdf")]
    [InlineData("../escape.pdf")]
    public async Task StorageOperations_RejectInvalidOrEscapingKeys(string invalidKey)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() => _storage.OpenReadAsync(invalidKey));
        await Assert.ThrowsAsync<RequestValidationException>(() => _storage.WriteAsync(invalidKey, new MemoryStream([1])));
        await Assert.ThrowsAsync<RequestValidationException>(() => _storage.DeleteAsync(invalidKey));
        await Assert.ThrowsAsync<RequestValidationException>(() => _storage.ExistsAsync(invalidKey));
    }
}
