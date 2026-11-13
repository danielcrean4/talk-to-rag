using System.Net;
using System.Text.Json;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Infrastructure.Ai;

namespace TalkToRag.UnitTests;

public class HttpAiClientSpeechTests
{
    [Fact]
    public async Task SynthesizeAsync_PostsSnakeCaseJson_AndReadsTheResponse()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK,
            """{"blob_key": "audio/u/a.wav", "format": "wav", "content_type": "audio/wav", "duration_seconds": 1.25}""");

        var result = await CreateClient(handler).SynthesizeAsync(new SynthesizeRequest("Hello.", "af_heart", "audio/u/a.wav"));

        Assert.Equal("/v1/speech/synthesize", handler.Path);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Hello.", body.RootElement.GetProperty("text").GetString());
        Assert.Equal("af_heart", body.RootElement.GetProperty("voice").GetString());
        Assert.Equal("audio/u/a.wav", body.RootElement.GetProperty("blob_key").GetString());
        Assert.Equal(new SynthesizeResponse("audio/u/a.wav", "audio/wav", 1.25), result);
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsAiServiceUnavailable_On503()
    {
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable,
            """{"detail": "Set TALK_TO_RAG_TTS to 'kokoro' to enable text-to-speech."}""");

        var error = await Assert.ThrowsAsync<AiServiceUnavailableException>(
            () => CreateClient(handler).SynthesizeAsync(new SynthesizeRequest("Hello.", null, "audio/u/a.wav")));

        Assert.Equal("Set TALK_TO_RAG_TTS to 'kokoro' to enable text-to-speech.", error.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsRequestValidation_On400()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"detail": "There is no text to speak."}""");

        var error = await Assert.ThrowsAsync<RequestValidationException>(
            () => CreateClient(handler).SynthesizeAsync(new SynthesizeRequest("[1]", null, "audio/u/a.wav")));

        Assert.Equal("There is no text to speak.", error.Message);
    }

    private static HttpAiClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8000") });

    private sealed class RecordingHandler(HttpStatusCode statusCode, string content) : HttpMessageHandler
    {
        public string? Path { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri?.AbsolutePath;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
