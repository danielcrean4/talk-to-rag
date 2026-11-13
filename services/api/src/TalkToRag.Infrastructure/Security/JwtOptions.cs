using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace TalkToRag.Infrastructure.Security;

/// <summary>The "Jwt" config section. The API's token validation reads the same values.</summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "talk-to-rag";
    public string Audience { get; set; } = "talk-to-rag";
    public int LifetimeMinutes { get; set; } = 60;

    /// <summary>Secret for signing tokens. Never commit a real one: set Jwt__SigningKey in the environment.</summary>
    public string SigningKey { get; set; } = "";

    public SymmetricSecurityKey CreateSigningKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        // HMAC-SHA256 needs at least 256 bits of key.
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }
}
