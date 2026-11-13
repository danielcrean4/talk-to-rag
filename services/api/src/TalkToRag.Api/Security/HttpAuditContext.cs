using Microsoft.IdentityModel.JsonWebTokens;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Api.Security;

public sealed class HttpAuditContext(IHttpContextAccessor accessor) : IAuditContext
{
    public Guid? UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;

    public string? RequestTraceId => accessor.HttpContext?.TraceIdentifier;
}
