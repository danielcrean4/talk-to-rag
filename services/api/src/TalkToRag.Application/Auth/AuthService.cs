using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Auth;

public sealed class AuthService(
    IUserRepository users,
    IPasswordHasher<User> passwordHasher,
    ITokenGenerator tokens,
    ICurrentUser currentUser,
    TimeProvider clock) : IAuthService
{
    public const int MinimumPasswordLength = 8;

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (!MailAddress.TryCreate(email, out _))
            throw new RequestValidationException("Enter a valid email address.");
        if (string.IsNullOrWhiteSpace(request.DisplayName))
            throw new RequestValidationException("Enter a display name.");
        if ((request.Password ?? "").Length < MinimumPasswordLength)
            throw new RequestValidationException($"Use a password of at least {MinimumPasswordLength} characters.");
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
        await users.AddAsync(user, cancellationToken);

        return Respond(user);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(NormalizeEmail(request.Email), cancellationToken);
        if (user is null)
            throw new AuthenticationFailedException();

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? "");
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

    private AuthResponse Respond(User user)
    {
        var token = tokens.CreateToken(user);
        return new AuthResponse(token.Token, token.ExpiresAtUtc, ToResponse(user));
    }

    private static UserResponse ToResponse(User user) => new(user.Id, user.Email, user.DisplayName);

    private static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();
}
