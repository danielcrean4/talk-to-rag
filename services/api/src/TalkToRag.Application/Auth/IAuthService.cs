namespace TalkToRag.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<SessionResponse> GetSessionAsync(CancellationToken cancellationToken);
    Task<UserResponse> GetCurrentUserAsync(CancellationToken cancellationToken);
}
