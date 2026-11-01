namespace TalkToRag.Application.Auth;

public sealed record RegisterRequest(string Email, string DisplayName, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, string DisplayName);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserResponse User);
