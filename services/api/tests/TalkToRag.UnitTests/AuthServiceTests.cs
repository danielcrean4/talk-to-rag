using Microsoft.AspNetCore.Identity;
using TalkToRag.Application.Auth;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.UnitTests;

public class AuthServiceTests
{
    private readonly InMemoryUsers _users = new();
    private readonly StubCurrentUser _currentUser = new();

    private AuthService CreateService() =>
        new(_users, new PasswordHasher<User>(), new StubTokens(), _currentUser, new FixedClock());

    [Fact]
    public async Task Register_StoresNormalizedEmailAndHashedPassword()
    {
        var response = await CreateService().RegisterAsync(new("  Dan@Example.COM ", "Dan", "correct horse"), default);

        var stored = Assert.Single(_users.All);
        Assert.Equal("dan@example.com", stored.Email);
        Assert.NotEqual("correct horse", stored.PasswordHash);
        Assert.Equal(stored.Id, response.User.Id);
        Assert.Equal($"token-for-{stored.Id}", response.AccessToken);
    }

    [Fact]
    public async Task Register_RejectsAnEmailThatIsAlreadyTaken()
    {
        var service = CreateService();
        await service.RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RegisterAsync(new("DAN@example.com", "Someone", "another password"), default));
    }

    [Theory]
    [InlineData("not-an-email", "Dan", "correct horse")]
    [InlineData("dan@example.com", " ", "correct horse")]
    [InlineData("dan@example.com", "Dan", "short")]
    public async Task Register_RejectsInvalidInput(string email, string displayName, string password)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            CreateService().RegisterAsync(new(email, displayName, password), default));
        Assert.Empty(_users.All);
    }

    [Fact]
    public async Task Login_SucceedsWithTheRightPassword()
    {
        var service = CreateService();
        await service.RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default);

        var response = await service.LoginAsync(new("Dan@Example.com", "correct horse"), default);

        Assert.Equal("dan@example.com", response.User.Email);
    }

    [Theory]
    [InlineData("dan@example.com", "wrong password")]
    [InlineData("nobody@example.com", "correct horse")]
    public async Task Login_FailsTheSameWayForWrongPasswordAndUnknownEmail(string email, string password)
    {
        var service = CreateService();
        await service.RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default);

        var error = await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.LoginAsync(new(email, password), default));
        Assert.Equal("Email or password is incorrect.", error.Message);
    }

    [Fact]
    public async Task GetCurrentUser_ReturnsTheSignedInUser()
    {
        var service = CreateService();
        var registered = await service.RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default);
        _currentUser.UserId = registered.User.Id;

        var me = await service.GetCurrentUserAsync(default);

        Assert.Equal(registered.User, me);
    }

    private sealed class InMemoryUsers : IUserRepository
    {
        public List<User> All { get; } = [];

        public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(u => u.Id == id));

        public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(u => u.Email == email));

        public Task AddAsync(User user, CancellationToken cancellationToken)
        {
            All.Add(user);
            return Task.CompletedTask;
        }
    }

    private sealed class StubTokens : ITokenGenerator
    {
        public AccessToken CreateToken(User user) => new($"token-for-{user.Id}", DateTimeOffset.UnixEpoch);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public Guid UserId { get; set; }
    }
}

internal sealed class FixedClock(DateTimeOffset? now = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now ?? new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
}
