using Microsoft.AspNetCore.Authentication.JwtBearer;
using TalkToRag.Infrastructure.Security;

namespace TalkToRag.Api.Security;

/// <summary>
/// Signs in a request for a source file from the short-lived token in its URL. A new tab cannot send the
/// sign-in header, so <c>POST /api/sources/{id}/file-link</c> hands out a token that opens that one file.
/// </summary>
public static class SourceFileLink
{
    public const string Scheme = "SourceFileLink";
    public const string QueryParameter = "access";

    public static void Configure(JwtBearerOptions options, JwtOptions jwt)
    {
        options.TokenValidationParameters = jwt.FileLinkValidation();
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Query[QueryParameter].FirstOrDefault();
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var linked = context.Principal?.FindFirst(JwtOptions.SourceClaim)?.Value;
                var requested = context.Request.RouteValues["id"]?.ToString();
                if (linked is null || !string.Equals(linked, requested, StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("This link is for another file.");
                }
                return Task.CompletedTask;
            },
        };
    }
}
