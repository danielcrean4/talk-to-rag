using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TalkToRag.Application.Auth;
using TalkToRag.Application.Conversations;
using TalkToRag.Application.Notebooks;
using TalkToRag.Application.Search;
using TalkToRag.Application.Sources;
using TalkToRag.Application.Speech;
using TalkToRag.Application.Tags;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISourceService, SourceService>();
        services.AddScoped<INotebookService, NotebookService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<ISpeechService, SpeechService>();
        return services;
    }
}
