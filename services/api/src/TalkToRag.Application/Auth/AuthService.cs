using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Auth;

public sealed class AuthService(
    IUserRepository users,
    IPasswordHasher<User> passwordHasher,
    ITokenGenerator tokens,
    ICurrentUser currentUser,
    TimeProvider clock,
    IOptions<AuthOptions> options) : IAuthService
{
    public const int MinimumPasswordLength = 8;
    // Each hash costs CPU for a password length the caller chooses, so the length is capped.
    public const int MaximumPasswordLength = 128;
    public const string LocalUserEmail = "local@talk-to-rag.local";

    private static readonly User NobodyUser = new() { Email = "", DisplayName = "", PasswordHash = "" };
    private static string? _unknownUserHash;

    public async Task<SessionResponse> GetSessionAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.SingleUser)
            return new SessionResponse(false, null);

        var user = await users.FindByEmailAsync(LocalUserEmail, cancellationToken);
        if (user is null)
        {
            // No password hash, so this user can never sign in with a password if accounts are turned on later.
            user = new User
            {
                Id = Guid.CreateVersion7(),
                Email = LocalUserEmail,
                DisplayName = "Local user",
                PasswordHash = "",
                CreatedAtUtc = clock.GetUtcNow(),
            };
            // Two first page loads at once both get here; the one that loses uses the user the other added.
            if (!await users.TryAddAsync(user, cancellationToken))
                user = await users.FindByEmailAsync(LocalUserEmail, cancellationToken)
                    ?? throw new InvalidOperationException("The local user was added by another request but cannot be read.");
        }

        return new SessionResponse(true, Respond(user));
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        RequireAccounts();
        var email = NormalizeEmail(request.Email);
        if (!MailAddress.TryCreate(email, out _))
            throw new RequestValidationException("Enter a valid email address.");
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new RequestValidationException("Enter a display name.");
        if ((request.Password ?? "").Length < MinimumPasswordLength)
            throw new RequestValidationException($"Use a password of at least {MinimumPasswordLength} characters.");
        if (request.Password!.Length > MaximumPasswordLength)
            throw new RequestValidationException($"Use a password of at most {MaximumPasswordLength} characters.");
        if (await users.FindByEmailAsync(email, cancellationToken) is not null)
            throw new ConflictException("An account with that email already exists.");

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            PasswordHash = "",
            CreatedAtUtc = clock.GetUtcNow(),
        };
        // Stores a salted, slow hash (PBKDF2), never the password itself.
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password!);
        if (!await users.TryAddAsync(user, cancellationToken))
            throw new ConflictException("An account with that email already exists.");

        return Respond(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        RequireAccounts();
        var password = request.Password ?? "";
        if (password.Length > MaximumPasswordLength)
            throw new AuthenticationFailedException();

        var user = await users.FindByEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        if (user is null)
        {
            // Hashing anyway makes an unknown email take as long to refuse as a wrong password.
            _unknownUserHash ??= passwordHasher.HashPassword(NobodyUser, "no account has this password");
            passwordHasher.VerifyHashedPassword(NobodyUser, _unknownUserHash, password);
            throw new AuthenticationFailedException();
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            throw new AuthenticationFailedException();

        return Respond(user);
    }

    public async Task<UserResponse> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException("This account no longer exists.");
        return ToResponse(user);
    }

    private void RequireAccounts()
    {
        if (options.Value.SingleUser)
            throw new NotFoundException("Accounts are turned off: this app runs in single-user mode.");
    }

    private AuthResponse Respond(User user)
    {
        var token = tokens.CreateToken(user);
        return new AuthResponse(token.Token, token.ExpiresAtUtc, ToResponse(user));
    }

    private static UserResponse ToResponse(User user) => new(user.Id, user.Email, user.DisplayName);

    private static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();
}
