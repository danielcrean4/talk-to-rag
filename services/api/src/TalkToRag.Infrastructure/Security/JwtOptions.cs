using System.Security.Cryptography;
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

    /// <summary>Where to keep a generated key when <see cref="SigningKey"/> is not set. Made on first use.</summary>
    public string SigningKeyFile { get; set; } = "";

    /// <summary>Fills <see cref="SigningKey"/> from <see cref="SigningKeyFile"/> when it is not set, writing a new random key there first if the file does not exist.</summary>
    public void LoadSigningKey()
    {
        if (!string.IsNullOrEmpty(SigningKey) || string.IsNullOrEmpty(SigningKeyFile))
            return;

        if (!File.Exists(SigningKeyFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(SigningKeyFile))!);
            using var file = new FileStream(SigningKeyFile, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                UnixCreateMode = OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
            using var writer = new StreamWriter(file);
            writer.Write(Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
        }
        SigningKey = File.ReadAllText(SigningKeyFile).Trim();
    }

    public SymmetricSecurityKey CreateSigningKey()
    {
        if (string.IsNullOrEmpty(SigningKey))
            throw new InvalidOperationException(
                "No key to sign sign-in tokens with. Set Jwt__SigningKey to a secret of at least 32 bytes, " +
                "or Jwt__SigningKeyFile to a file the API may create one in.");
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        // HMAC-SHA256 needs at least 256 bits of key.
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }
}
