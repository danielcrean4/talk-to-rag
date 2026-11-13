using System.Runtime.CompilerServices;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Speech;

namespace TalkToRag.UnitTests;

public class SpeechServiceTests
{
    private readonly RecordingBlobStorage _storage = new();
    private readonly FakeAiClient _aiClient;
    private readonly TestCurrentUser _currentUser = new() { UserId = Guid.NewGuid() };

    public SpeechServiceTests() => _aiClient = new FakeAiClient(_storage);

    private SpeechService CreateService() => new(_storage, _aiClient, _currentUser);

    [Fact]
    public async Task TranscribeAsync_HandsTheRecordingToTheAiServiceByKey_AndReturnsTrimmedText()
    {
        _aiClient.Text = "  what is a normal map  ";
        using var audio = new MemoryStream([1, 2, 3]);

        var response = await CreateService().TranscribeAsync("audio/webm;codecs=opus", audio);

        Assert.Equal("what is a normal map", response.Text);
        var key = Assert.Single(_aiClient.Keys);
        Assert.StartsWith($"audio/{_currentUser.UserId}/", key);
        Assert.EndsWith(".webm", key);
        Assert.Equal([1, 2, 3], _storage.WrittenAtCallTime[key]);
    }

    [Fact]
    public async Task TranscribeAsync_DeletesTheRecording_AfterTranscribing()
    {
        using var audio = new MemoryStream([1]);

        await CreateService().TranscribeAsync("audio/mp4", audio);

        Assert.Empty(_storage.Blobs);
    }

    [Fact]
    public async Task TranscribeAsync_DeletesTheRecording_WhenTheAiServiceFails()
    {
        _aiClient.Failure = new AiServiceUnavailableException("Speech-to-text is not configured.");
        using var audio = new MemoryStream([1]);

        await Assert.ThrowsAsync<AiServiceUnavailableException>(
            () => CreateService().TranscribeAsync("audio/webm", audio));

        Assert.Empty(_storage.Blobs);
    }

    [Fact]
    public async Task TranscribeAsync_RejectsNonAudioUploads_WithoutStoringThem()
    {
        using var audio = new MemoryStream([1]);

        await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateService().TranscribeAsync("application/pdf", audio));

        Assert.Empty(_storage.WrittenAtCallTime);
    }

    [Fact]
    public async Task SynthesizeAsync_HasTheAiServiceWriteUnderTheUsersAudioKey_AndReturnsTheWav()
    {
        var speech = await CreateService().SynthesizeAsync(new SynthesizeSpeechRequest("TD learning bootstraps [1].", "af_heart"));

        var request = Assert.Single(_aiClient.Synthesized);
        Assert.Equal("TD learning bootstraps [1].", request.Text);
        Assert.Equal("af_heart", request.Voice);
        Assert.StartsWith($"audio/{_currentUser.UserId}/", request.BlobKey);
        Assert.EndsWith(".wav", request.BlobKey);
        Assert.Equal("audio/wav", speech.ContentType);
        using var copy = new MemoryStream();
        await speech.Audio.CopyToAsync(copy);
        Assert.Equal(FakeAiClient.Wav, copy.ToArray());
        await speech.Audio.DisposeAsync();
    }

    [Fact]
    public async Task SynthesizeAsync_KeepsTheAudioUntilTheStreamIsDisposed_ThenDeletesIt()
    {
        var speech = await CreateService().SynthesizeAsync(new SynthesizeSpeechRequest("Hello."));
        var key = Assert.Single(_aiClient.Synthesized).BlobKey;

        Assert.True(_storage.Blobs.ContainsKey(key));
        await speech.Audio.DisposeAsync();
        Assert.Empty(_storage.Blobs);
    }

    [Fact]
    public async Task SynthesizeAsync_DeletesTheAudio_WhenTheAiServiceFails()
    {
        _aiClient.Failure = new AiServiceUnavailableException("Text-to-speech is not configured.");

        await Assert.ThrowsAsync<AiServiceUnavailableException>(
            () => CreateService().SynthesizeAsync(new SynthesizeSpeechRequest("Hello.")));

        Assert.Single(_aiClient.Synthesized);
        Assert.Empty(_storage.Blobs);
    }

    [Fact]
    public async Task SynthesizeAsync_DeletesTheAudio_WhenTheCallerCancelsDuringSynthesis()
    {
        using var cancellation = new CancellationTokenSource();
        _aiClient.OnSynthesized = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateService().SynthesizeAsync(new SynthesizeSpeechRequest("Hello."), cancellation.Token));

        Assert.Single(_aiClient.Synthesized);
        Assert.Empty(_storage.Blobs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SynthesizeAsync_RejectsEmptyText_WithoutCallingTheAiService(string text)
    {
        await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateService().SynthesizeAsync(new SynthesizeSpeechRequest(text)));

        Assert.Empty(_aiClient.Synthesized);
    }

    [Fact]
    public async Task SynthesizeAsync_RejectsTextOverTheLimit_WithoutCallingTheAiService()
    {
        var atLimit = new string('a', SpeechService.MaxSynthesizeCharacters);

        await CreateService().SynthesizeAsync(new SynthesizeSpeechRequest(atLimit));
        await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateService().SynthesizeAsync(new SynthesizeSpeechRequest(atLimit + "a")));

        Assert.Single(_aiClient.Synthesized);
    }

    private sealed class RecordingBlobStorage : IBlobStorage
    {
        public Dictionary<string, byte[]> Blobs { get; } = [];
        public Dictionary<string, byte[]> WrittenAtCallTime { get; } = [];

        public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Blobs[key] = buffer.ToArray();
            WrittenAtCallTime[key] = buffer.ToArray();
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Blobs[key]));

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Blobs.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Blobs.ContainsKey(key));
    }

    private sealed class FakeAiClient(IBlobStorage storage) : IAiClient
    {
        public static readonly byte[] Wav = [(byte)'R', (byte)'I', (byte)'F', (byte)'F'];

        public string Text { get; set; } = "hello";
        public List<SynthesizeRequest> Synthesized { get; } = [];
        public Action? OnSynthesized { get; set; }
        public Exception? Failure { get; set; }
        public List<string> Keys { get; } = [];

        public Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken cancellationToken = default)
        {
            Keys.Add(request.BlobKey);
            return Failure is null ? Task.FromResult(new TranscribeResponse(Text)) : Task.FromException<TranscribeResponse>(Failure);
        }

        // Like the AI service, write the audio to shared storage under the key it was given.
        public async Task<SynthesizeResponse> SynthesizeAsync(SynthesizeRequest request, CancellationToken cancellationToken = default)
        {
            Synthesized.Add(request);
            await storage.WriteAsync(request.BlobKey, new MemoryStream(Wav), cancellationToken);
            OnSynthesized?.Invoke();
            if (Failure is not null)
            {
                throw Failure;
            }

            return new SynthesizeResponse(request.BlobKey, "audio/wav", 0.5);
        }

        public Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public async IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
