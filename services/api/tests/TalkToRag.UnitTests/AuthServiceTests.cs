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
    private readonly CountingHasher _hasher = new();

    private AuthService CreateService(bool singleUser = false) =>
        new(_users, _hasher, new StubTokens(), _currentUser, new FixedClock(),
            Microsoft.Extensions.Options.Options.Create(new AuthOptions { SingleUser = singleUser }));

    [Fact]
    public async Task Session_WithAccounts_SignsNobodyIn()
    {
        var session = await CreateService().GetSessionAsync(default);

        Assert.False(session.SingleUser);
        Assert.Null(session.Session);
        Assert.Empty(_users.All);
    }

    [Fact]
    public async Task Session_InSingleUserMode_CreatesTheLocalUserOnce_AndSignsItIn()
    {
        var service = CreateService(singleUser: true);

        var first = await service.GetSessionAsync(default);
        var second = await service.GetSessionAsync(default);

        var local = Assert.Single(_users.All);
        Assert.Equal(AuthService.LocalUserEmail, local.Email);
        Assert.True(first.SingleUser);
        Assert.Equal($"token-for-{local.Id}", first.Session!.AccessToken);
        Assert.Equal(local.Id, second.Session!.User.Id);
    }

    [Fact]
    public async Task Session_WhenAnotherFirstLoadAddsTheLocalUserFirst_UsesThatUser()
    {
        var other = new User { Id = Guid.NewGuid(), Email = AuthService.LocalUserEmail, DisplayName = "Local user", PasswordHash = "" };
        _users.AfterNextFind = () => _users.All.Add(other);

        var session = await CreateService(singleUser: true).GetSessionAsync(default);

        Assert.Same(other, Assert.Single(_users.All));
        Assert.Equal(other.Id, session.Session!.User.Id);
    }

    [Fact]
    public async Task SingleUserMode_TurnsOffRegisterAndLogin()
    {
        var service = CreateService(singleUser: true);

        await Assert.ThrowsAsync<NotFoundException>(() => service.RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default));
        await Assert.ThrowsAsync<NotFoundException>(() => service.LoginAsync(new("dan@example.com", "correct horse"), default));
    }

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

    [Fact]
    public async Task Register_WhenAnotherRequestTakesTheEmailFirst_ReportsAConflict()
    {
        _users.AfterNextFind = () => _users.All.Add(
            new User { Id = Guid.NewGuid(), Email = "dan@example.com", DisplayName = "Dan", PasswordHash = "hash" });

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService().RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default));
        Assert.Single(_users.All);
    }

    [Fact]
    public async Task Register_AcceptsAPasswordOfTheMaximumLength()
    {
        await CreateService().RegisterAsync(new("dan@example.com", "Dan", new string('x', AuthService.MaximumPasswordLength)), default);

        Assert.Single(_users.All);
    }

    [Theory]
    [InlineData("not-an-email", "Dan", "correct horse")]
    [InlineData("dan@example.com", " ", "correct horse")]
    [InlineData("dan@example.com", "Dan", "short")]
    [InlineData("dan@example.com", "Dan", "a password far too long to be worth hashing: ....................................................................................")]
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
    public async Task Login_ChecksAHashForAnUnknownEmailToo()
    {
        var service = CreateService();
        await service.RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default);
        _hasher.Verifications = 0;

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.LoginAsync(new("nobody@example.com", "correct horse"), default));
        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.LoginAsync(new("dan@example.com", "wrong password"), default));

        Assert.Equal(2, _hasher.Verifications);
    }

    [Fact]
    public async Task Login_RefusesAnOverlongPasswordWithoutHashingIt()
    {
        var service = CreateService();
        await service.RegisterAsync(new("dan@example.com", "Dan", "correct horse"), default);
        _hasher.Verifications = 0;

        await Assert.ThrowsAsync<AuthenticationFailedException>(() =>
            service.LoginAsync(new("dan@example.com", new string('x', 100_000)), default));

        Assert.Equal(0, _hasher.Verifications);
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

        /// <summary>Runs once after the next lookup by email, as another request racing this one would.</summary>
        public Action? AfterNextFind { get; set; }

        public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(All.FirstOrDefault(u => u.Id == id));

        public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
        {
            var found = All.FirstOrDefault(u => u.Email == email);
            var after = AfterNextFind;
            AfterNextFind = null;
            after?.Invoke();
            return Task.FromResult(found);
        }

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken)
        {
            if (All.Any(u => u.Email == user.Email))
                return Task.FromResult(false);
            All.Add(user);
            return Task.FromResult(true);
        }
    }

    private sealed class CountingHasher : PasswordHasher<User>
    {
        public int Verifications { get; set; }

        public override PasswordVerificationResult VerifyHashedPassword(User user, string hashedPassword, string providedPassword)
        {
            Verifications++;
            return base.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    private sealed class StubTokens : ITokenGenerator
    {
        public AccessToken CreateToken(User user) => new($"token-for-{user.Id}", DateTimeOffset.UnixEpoch);

        public AccessToken CreateFileToken(Guid userId, Guid sourceId) => throw new NotSupportedException();
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
