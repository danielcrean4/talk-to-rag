using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TalkToRag.Domain.Entities;
using TalkToRag.Infrastructure.Security;

namespace TalkToRag.UnitTests;

public class JwtTokenGeneratorTests
{
    private static readonly JwtOptions Options = new() { SigningKey = "test-signing-key-that-is-at-least-32-bytes", LifetimeMinutes = 30 };
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly User Dan = new()
    {
        Id = Guid.Parse("0192d6a0-0000-7000-8000-000000000001"),
        Email = "dan@example.com",
        DisplayName = "Dan",
        PasswordHash = "x",
    };

    [Fact]
    public async Task Token_IsSignedAndCarriesTheUserClaims()
    {
        var token = new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock(Now)).CreateToken(Dan);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = Options.CreateSigningKey(),
        });

        Assert.True(result.IsValid, result.Exception?.Message);
        Assert.Equal(Dan.Id.ToString(), result.Claims[JwtRegisteredClaimNames.Sub]);
        Assert.Equal("dan@example.com", result.Claims[JwtRegisteredClaimNames.Email]);
        Assert.Equal(Now.AddMinutes(30), token.ExpiresAtUtc);
    }

    [Fact]
    public async Task Token_IsRejectedWithADifferentKey()
    {
        var token = new JwtTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options), new FixedClock(Now)).CreateToken(Dan);
        var otherKey = new JwtOptions { SigningKey = "a-completely-different-key-of-32-bytes!" }.CreateSigningKey();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Token, new TokenValidationParameters
        {
            ValidIssuer = Options.Issuer,
            ValidAudience = Options.Audience,
            IssuerSigningKey = otherKey,
        });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ShortSigningKey_IsRefused() =>
        Assert.Throws<InvalidOperationException>(() => new JwtOptions { SigningKey = "too-short" }.CreateSigningKey());
}
