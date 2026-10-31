using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TalkToRag.Application.Auth;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthService, AuthService>();
        return services;
    }
}
