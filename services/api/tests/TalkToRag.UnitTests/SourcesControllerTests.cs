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
    public void Upload_HasRequestSizeLimits_ConfiguredAbove50Mb()
    {
        var method = typeof(SourcesController).GetMethod(nameof(SourcesController.Upload));
        Assert.NotNull(method);

        var requestSizeLimit = method.GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(requestSizeLimit);
        Assert.True(((IRequestSizeLimitMetadata)requestSizeLimit).MaxRequestBodySize >= 50 * 1024 * 1024, "RequestSizeLimit must be at least 50 MB");

        var formLimits = method.GetCustomAttribute<RequestFormLimitsAttribute>();
        Assert.NotNull(formLimits);
        Assert.True(formLimits.MultipartBodyLengthLimit >= 50 * 1024 * 1024, "MultipartBodyLengthLimit must be at least 50 MB");
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

    private sealed class FakeSourceService : ISourceService
    {
        public Task<CreateSourceResponse> UploadAsync(string fileName, string contentType, Stream stream, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<SourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<SourceFileResponse> GetFileAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new SourceFileResponse(new MemoryStream([1, 2, 3]), "application/pdf", "cs229.pdf"));

        public Task RetryAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
