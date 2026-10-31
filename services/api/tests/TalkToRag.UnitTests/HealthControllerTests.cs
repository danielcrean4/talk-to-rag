using Microsoft.AspNetCore.Mvc;
using TalkToRag.Api.Controllers;

namespace TalkToRag.UnitTests;

public class HealthControllerTests
{
    [Fact]
    public void Get_ReportsOk()
    {
        var result = new HealthController().Get();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<HealthResponse>(ok.Value);
        Assert.Equal("ok", body.Status);
        Assert.Equal("talk-to-rag-api", body.Service);
        Assert.False(string.IsNullOrEmpty(body.Version));
    }
}
