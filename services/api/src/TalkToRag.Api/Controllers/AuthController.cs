using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Auth;

namespace TalkToRag.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await auth.RegisterAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Me), response);
    }

    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        auth.LoginAsync(request, cancellationToken);

    /// <summary>Tells a client whether to show a sign-in form. In single-user mode it returns a session to use right away.</summary>
    [HttpGet("session")]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public Task<SessionResponse> Session(CancellationToken cancellationToken) =>
        auth.GetSessionAsync(cancellationToken);

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<UserResponse> Me(CancellationToken cancellationToken) =>
        auth.GetCurrentUserAsync(cancellationToken);
}
