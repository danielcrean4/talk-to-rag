namespace TalkToRag.Application.Auth;

public sealed record RegisterRequest(string Email, string DisplayName, string Password);

public sealed record LoginRequest(string Email, string Password);

public sealed record UserResponse(Guid Id, string Email, string DisplayName);

public sealed record AuthResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, UserResponse User);

/// <summary>What a client needs before it shows a sign-in form: in single-user mode it is already signed in.</summary>
public sealed record SessionResponse(bool SingleUser, AuthResponse? Session);
