using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using TalkToRag.Infrastructure;
using TalkToRag.Infrastructure.Persistence;

namespace TalkToRag.IntegrationTests;

/// <summary>The auditor role logs in only with the password the API is configured with.</summary>
public class AuditorLoginIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    [Fact]
    public async Task Migrating_GivesTheAuditorTheConfiguredPassword_AndNoOther()
    {
        await MigrateAsync(auditorPassword: "it's {configured}");

        await LogInAsAuditorAsync("it's {configured}");
        await Assert.ThrowsAsync<PostgresException>(() => LogInAsAuditorAsync("auditor-local-only"));
    }

    [Fact]
    public async Task WithoutAPassword_TheAuditorCannotLogIn()
    {
        await MigrateAsync(auditorPassword: null);

        await Assert.ThrowsAsync<PostgresException>(() => LogInAsAuditorAsync("auditor-local-only"));
    }

    private async Task MigrateAsync(string? auditorPassword)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TalkToRag"] = _container.GetConnectionString(),
                ["Audit:AuditorPassword"] = auditorPassword,
            })
            .Build();
        await using var services = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TalkToRagDbContext>().Database.MigrateAsync();
    }

    private async Task LogInAsAuditorAsync(string password)
    {
        var login = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Username = "talk_to_rag_auditor",
            Password = password,
        };
        await using var connection = new NpgsqlConnection(login.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM audit.sources", connection);
        await command.ExecuteScalarAsync();
    }
}
