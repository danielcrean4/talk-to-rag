using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalkToRag.Infrastructure.Persistence;

namespace TalkToRag.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TalkToRag")
            ?? throw new InvalidOperationException("Set ConnectionStrings:TalkToRag.");

        services.AddDbContext<TalkToRagDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention());

        return services;
    }
}
