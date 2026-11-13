using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Api.Controllers;
using TalkToRag.Api.Errors;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Speech;

namespace TalkToRag.UnitTests;

public class SpeechControllerTests
{
    [Fact]
    public async Task Synthesize_ReturnsTheAudioAsAWavFile()
    {
        var audio = new MemoryStream([1, 2, 3]);
        var service = new FakeSpeechService(new SynthesizedSpeechResponse(audio, "audio/wav"));

        var result = await new SpeechController(service).Synthesize(new SynthesizeSpeechRequest("Hello."), default);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("audio/wav", file.ContentType);
        Assert.Same(audio, file.FileStream);
        Assert.Equal("Hello.", service.Requests.Single().Text);
    }

    [Fact]
    public async Task ExceptionHandler_AnswersAiServiceUnavailableWith503()
    {
        var problems = new RecordingProblemDetailsService();
        var context = new DefaultHttpContext();

        var handled = await new ApplicationExceptionHandler(problems).TryHandleAsync(
            context, new AiServiceUnavailableException("Text-to-speech is not configured."), default);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal(503, problems.Written!.ProblemDetails.Status);
        Assert.Equal("Text-to-speech is not configured.", problems.Written.ProblemDetails.Detail);
    }

    [Fact]
    public async Task ExceptionHandler_LeavesUnexpectedErrorsToTheDefaultHandler()
    {
        var handled = await new ApplicationExceptionHandler(new RecordingProblemDetailsService()).TryHandleAsync(
            new DefaultHttpContext(), new InvalidOperationException("boom"), default);

        Assert.False(handled);
    }

    private sealed class FakeSpeechService(SynthesizedSpeechResponse response) : ISpeechService
    {
        public List<SynthesizeSpeechRequest> Requests { get; } = [];

        public Task<TranscriptionResponse> TranscribeAsync(string contentType, Stream audio, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();

        public Task<SynthesizedSpeechResponse> SynthesizeAsync(SynthesizeSpeechRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }
}
