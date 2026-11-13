using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace TalkToRag.Api.Controllers;

public sealed record HealthResponse(string Status, string Service, string Version);

/// <summary>Liveness only: the process is up and serving. It does not check Postgres or the AI service.</summary>
[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private static readonly string Version =
        typeof(HealthController).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    [HttpGet]
    public ActionResult<HealthResponse> Get() => Ok(new HealthResponse("ok", "talk-to-rag-api", Version));
}
