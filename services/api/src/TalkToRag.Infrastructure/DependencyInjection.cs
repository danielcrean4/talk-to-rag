using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Infrastructure.Persistence;
using TalkToRag.Infrastructure.Security;

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
        services.AddScoped<IUserRepository, UserRepository>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();

        return services;
    }
}
