using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);

public interface ITokenGenerator
{
    AccessToken CreateToken(User user);

    /// <summary>A short-lived token that opens one source's file and nothing else.</summary>
    AccessToken CreateFileToken(Guid userId, Guid sourceId);
}
