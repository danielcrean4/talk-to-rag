using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);

public interface ITokenGenerator
{
    AccessToken CreateToken(User user);
}
