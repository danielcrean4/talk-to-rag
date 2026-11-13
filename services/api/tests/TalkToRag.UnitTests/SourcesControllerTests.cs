using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Api.Controllers;
using TalkToRag.Application.Sources;

namespace TalkToRag.UnitTests;

public class SourcesControllerTests
{
    [Fact]
    public void Upload_HasRequestSizeLimits_AboveTheLargestAcceptedFile()
    {
        var method = typeof(SourcesController).GetMethod(nameof(SourcesController.Upload));
        Assert.NotNull(method);

        var requestSizeLimit = method.GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(requestSizeLimit);
        Assert.True(((IRequestSizeLimitMetadata)requestSizeLimit).MaxRequestBodySize > SourceService.MaxSizeBytes, "RequestSizeLimit must exceed the service limit");

        var formLimits = method.GetCustomAttribute<RequestFormLimitsAttribute>();
        Assert.NotNull(formLimits);
        Assert.True(formLimits.MultipartBodyLengthLimit > SourceService.MaxSizeBytes, "MultipartBodyLengthLimit must exceed the service limit");
    }

    [Fact]
    public async Task GetFile_ReturnsFileStreamResult_WithInlineHeader_AndRangeProcessing()
    {
        var fakeService = new FakeSourceService();
        var controller = new SourcesController(fakeService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.GetFile(Guid.NewGuid(), default);

        var fileResult = Assert.IsType<FileStreamResult>(result);
        Assert.True(fileResult.EnableRangeProcessing);
        Assert.Equal("application/pdf", fileResult.ContentType);
        Assert.Contains("inline", controller.Response.Headers.ContentDisposition.ToString());
    }

    [Fact]
    public async Task GetFile_EncodesQuotesAndNonAsciiInTheFileName()
    {
        var controller = new SourcesController(new FakeSourceService("Sutton \"RL\" – 2e.pdf"))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        await controller.GetFile(Guid.NewGuid(), default);

        var header = Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.Parse(
            controller.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("inline", header.DispositionType.ToString());
        Assert.Equal("Sutton \"RL\" – 2e.pdf", header.FileNameStar.ToString());
    }

    private sealed class FakeSourceService(string fileName = "cs229.pdf") : ISourceService
    {
        public Task<CreateSourceResponse> UploadAsync(string fileName, Stream stream, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<SourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<SourceFileResponse> GetFileAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new SourceFileResponse(new MemoryStream([1, 2, 3]), "application/pdf", fileName));

        public Task RetryAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
