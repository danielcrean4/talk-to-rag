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

    [Fact]
    public void MissingSigningKey_IsRefused_WithWhatToSet()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new JwtOptions().CreateSigningKey());

        Assert.Contains("Jwt__SigningKey", error.Message);
        Assert.Contains("Jwt__SigningKeyFile", error.Message);
    }

    [Fact]
    public void SigningKeyFile_IsMadeOnce_AndReadBackAfterARestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"talk-to-rag-keys-{Guid.NewGuid()}", "jwt-signing-key");
        try
        {
            var first = new JwtOptions { SigningKeyFile = path };
            first.LoadSigningKey();
            var second = new JwtOptions { SigningKeyFile = path };
            second.LoadSigningKey();

            Assert.True(first.CreateSigningKey().KeySize >= 256);
            Assert.Equal(first.SigningKey, second.SigningKey);
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void SigningKeyFile_IsNotUsed_WhenAKeyIsSet()
    {
        var path = Path.Combine(Path.GetTempPath(), $"talk-to-rag-keys-{Guid.NewGuid()}", "jwt-signing-key");
        var options = new JwtOptions { SigningKey = Options.SigningKey, SigningKeyFile = path };

        options.LoadSigningKey();

        Assert.Equal(Options.SigningKey, options.SigningKey);
        Assert.False(File.Exists(path));
    }
}
