using System.Text;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Speech;

namespace TalkToRag.UnitTests;

public class SpeechServiceTests
{
    private readonly InMemoryBlobStorage _blobStorage = new();
    private readonly FakeSpeechAiClient _aiClient = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };

    private SpeechService CreateService() =>
        new(_blobStorage, _aiClient, _currentUser);

    [Fact]
    public async Task TranscribeAsync_StoresBlobAndReturnsTranscript()
    {
        var service = CreateService();
        var audioBytes = Encoding.UTF8.GetBytes("fake audio bytes");
        using var stream = new MemoryStream(audioBytes);

        var result = await service.TranscribeAsync(stream, "audio/webm", default);

        Assert.Equal("what is the normal equation", result.Text);
        Assert.StartsWith("speech/", result.BlobKey);
        Assert.EndsWith(".webm", result.BlobKey);
        Assert.True(_blobStorage.Written.ContainsKey(result.BlobKey));
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsValidationException_WhenStreamIsEmpty()
    {
        var service = CreateService();
        using var stream = new MemoryStream([]);

        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.TranscribeAsync(stream, "audio/webm", default));
    }

    [Fact]
    public async Task SynthesizeAsync_ReturnsAudioStream()
    {
        var service = CreateService();
        var stream = await service.SynthesizeAsync("Hello world", default);

        Assert.NotNull(stream);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        Assert.Equal([1, 2, 3], ms.ToArray());
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsValidationException_WhenTextIsEmpty()
    {
        var service = CreateService();
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            service.SynthesizeAsync("", default));
    }

    private sealed class FakeSpeechAiClient : IAiClient
    {
        public Task<string> TranscribeAsync(string blobKey, CancellationToken cancellationToken = default) =>
            Task.FromResult("what is the normal equation");

        public Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
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

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }
}
