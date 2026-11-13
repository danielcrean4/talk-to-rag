using Microsoft.IdentityModel.JsonWebTokens;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Api.Security;

/// <summary>Reads the user id from the validated token's "sub" claim. Lives in Api because it needs the HTTP request.</summary>
public sealed class ClaimsCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid UserId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id
            : throw new InvalidOperationException("No signed-in user on this request; is the endpoint missing [Authorize]?");
}
