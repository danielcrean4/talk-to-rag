using System.Reflection;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Api.Controllers;

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
}
