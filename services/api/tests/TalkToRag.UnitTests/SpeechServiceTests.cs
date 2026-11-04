using System.Text;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Search;
using TalkToRag.Application.Speech;

namespace TalkToRag.UnitTests;

public class SpeechServiceTests
{
    private readonly InMemoryBlobStorage _blobStorage = new();
    private readonly FakeSpeechAiClient _aiClient = new();
    private readonly FakeSearchService _searchService = new();
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };

    private SpeechService CreateService() =>
        new(_blobStorage, _aiClient, _searchService, _currentUser);

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

    [Fact]
    public async Task RouteIntentAsync_ReturnsRoutedCommand()
    {
        var service = CreateService();
        var result = await service.RouteIntentAsync("pull up normal equation", new ScreenContextDto(CurrentPage: 8), default);

        Assert.Equal("pull_up", result.Intent);
        Assert.Equal("normal equation", result.Topic);
        Assert.Equal("fast_path", result.Route);
    }

    [Fact]
    public async Task PullUpTopicAsync_ReturnsTop3AndFormattedVoicePrompt()
    {
        var service = CreateService();
        var result = await service.PullUpTopicAsync("normal equation", null, default);

        Assert.Equal("normal equation", result.Topic);
        Assert.Equal(3, result.Candidates.Count);
        Assert.Contains("Found 3: cs229-notes1 page 8, cs229-notes1 page 10, and cs229-notes1 page 25. Which one?", result.SpokenPrompt);
        Assert.Equal(8, result.Candidates[0].PageNumber);
    }

    private sealed class FakeSearchService : ISearchService
    {
        public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
        {
            var hit1 = new PassageHitDto(1, Guid.NewGuid(), 8, "8", "Normal Equation", "theta = (X^T X)^-1 X^T y", 0.03, ["semantic"]);
            var hit2 = new PassageHitDto(2, Guid.NewGuid(), 10, "10", "Derivation", "minimizing squared error", 0.02, ["semantic"]);
            var hit3 = new PassageHitDto(3, Guid.NewGuid(), 25, "25", "Linear Algebra Review", "matrix calculus rules", 0.01, ["semantic"]);
            var group = new SourcePassageGroupDto(
                new SourceSummaryDto(Guid.NewGuid(), "cs229-notes1.pdf", "application/pdf"),
                [hit1, hit2, hit3]);
            return Task.FromResult(new SearchResponse(Guid.NewGuid(), request.Query, [group]));
        }

        public Task<SummaryResponse> GenerateSummaryAsync(Guid conversationId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public IAsyncEnumerable<string> StreamSummaryAsync(Guid conversationId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class FakeSpeechAiClient : IAiClient
    {
        public Task<string> TranscribeAsync(string blobKey, CancellationToken cancellationToken = default) =>
            Task.FromResult("what is the normal equation");

        public Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public Task<RouteIntentResponseDto> RouteIntentAsync(RouteIntentRequestDto request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RouteIntentResponseDto("pull_up", "normal equation", null, "fast_path", 1.0));

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
