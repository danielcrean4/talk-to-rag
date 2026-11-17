using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TalkToRag.Domain.Entities;
using TalkToRag.Infrastructure.Persistence;

namespace TalkToRag.IntegrationTests;

public class UserRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    private DbContextOptions<TalkToRagDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _options = new DbContextOptionsBuilder<TalkToRagDbContext>()
            .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .Options;

        await using var db = new TalkToRagDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task TryAddAsync_WhenAnotherRequestAddedTheEmailFirst_ReturnsFalseAndLeavesTheContextUsable()
    {
        await using var first = new TalkToRagDbContext(_options);
        await using var second = new TalkToRagDbContext(_options);

        Assert.True(await new UserRepository(first).TryAddAsync(NewUser("local@test.com"), default));
        Assert.False(await new UserRepository(second).TryAddAsync(NewUser("local@test.com"), default));

        Assert.True(await new UserRepository(second).TryAddAsync(NewUser("other@test.com"), default));
        Assert.Equal(2, await second.Users.CountAsync());
    }

    private static User NewUser(string email) => new()
    {
        Id = Guid.CreateVersion7(),
        Email = email,
        DisplayName = "User",
        PasswordHash = "",
    };
}
