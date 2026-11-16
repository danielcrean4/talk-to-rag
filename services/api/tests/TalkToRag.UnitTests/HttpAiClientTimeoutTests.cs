using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Infrastructure.Ai;

namespace TalkToRag.UnitTests;

public class HttpAiClientTimeoutTests
{
    private static readonly AnswerRequest Answer = new("Why?", []);

    [Fact]
    public async Task ExpandQueryAsync_ThrowsAiServiceUnavailable_WhenTheServiceNeverAnswers()
    {
        var client = CreateClient(new NeverAnswers(), interactiveSeconds: 1);

        var error = await Assert.ThrowsAsync<AiServiceUnavailableException>(
            () => client.ExpandQueryAsync(new ExpandQueryRequest("Why?", [], false)));

        Assert.Equal(HttpAiClient.TimedOutMessage, error.Message);
    }

    [Fact]
    public async Task GenerateQuestionsAsync_ThrowsAiServiceUnavailable_WhenTheServiceNeverAnswers()
    {
        var client = CreateClient(new NeverAnswers(), interactiveSeconds: 1);

        await Assert.ThrowsAsync<AiServiceUnavailableException>(
            () => client.GenerateQuestionsAsync(new GenerateQuestionsRequest("Concept", [], 3)));
    }

    [Fact]
    public async Task ExpandQueryAsync_StillCancels_WhenTheCallerGivesUp()
    {
        var client = CreateClient(new NeverAnswers(), interactiveSeconds: 60);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExpandQueryAsync(new ExpandQueryRequest("Why?", [], false), caller.Token));
    }

    [Fact]
    public async Task IngestSourceAsync_UsesTheIngestLimit_NotTheInteractiveOne()
    {
        var handler = new AnswersAfter(TimeSpan.FromMilliseconds(1500), """{"chunks": [], "embedding_model": "fake"}""");
        var client = CreateClient(handler, interactiveSeconds: 1, ingestSeconds: 60);

        var response = await client.IngestSourceAsync(new IngestSourceRequest(Guid.Empty, "key", "text/plain"));

        Assert.Equal("fake", response.EmbeddingModel);
    }

    [Fact]
    public async Task StreamAnswerAsync_ThrowsAiServiceUnavailable_WhenTheServiceNeverAnswers()
    {
        var client = CreateClient(new NeverAnswers(), interactiveSeconds: 1);

        await Assert.ThrowsAsync<AiServiceUnavailableException>(async () =>
        {
            await foreach (var _ in client.StreamAnswerAsync(Answer)) { }
        });
    }

    [Fact]
    public async Task StreamAnswerAsync_EndsWithAnErrorEvent_WhenTheStreamStalls()
    {
        var client = CreateClient(new StallingStream("data: {\"token\": \"Partly\"}\n\n"), interactiveSeconds: 1);

        var lines = new List<string>();
        await foreach (var line in client.StreamAnswerAsync(Answer))
        {
            lines.Add(line);
        }

        Assert.Equal(["data: {\"token\": \"Partly\"}", "data: {\"error\":\"The model did not answer in time.\"}"], lines);
    }

    private static HttpAiClient CreateClient(HttpMessageHandler handler, int interactiveSeconds, int ingestSeconds = 60) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:8000"), Timeout = Timeout.InfiniteTimeSpan },
            Options.Create(new AiServiceOptions { InteractiveTimeoutSeconds = interactiveSeconds, IngestTimeoutSeconds = ingestSeconds }));

    private sealed class NeverAnswers : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }

    private sealed class AnswersAfter(TimeSpan delay, string json) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>Answers at once with an event stream that sends one event and then never another.</summary>
    private sealed class StallingStream(string firstEvent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new StallAfter(Encoding.UTF8.GetBytes(firstEvent)));
            content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class StallAfter(byte[] first) : Stream
    {
        private bool _sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_sent)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            _sent = true;
            first.CopyTo(buffer);
            return first.Length;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
