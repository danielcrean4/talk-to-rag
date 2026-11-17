using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Infrastructure.Audit;
using TalkToRag.Infrastructure.Ai;
using TalkToRag.Infrastructure.Ingestion;
using TalkToRag.Infrastructure.Persistence;
using TalkToRag.Infrastructure.Security;
using TalkToRag.Infrastructure.Storage;

namespace TalkToRag.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("TalkToRag")
            ?? throw new InvalidOperationException("Set ConnectionStrings:TalkToRag.");

        // Applied after every migration, by the API at startup and by `dotnet ef database update`.
        var auditorPassword = configuration["Audit:AuditorPassword"];
        services.AddDbContext<TalkToRagDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention()
            .UseSeeding((context, _) => AuditorLogin.SetPassword(context, auditorPassword))
            .UseAsyncSeeding((context, _, cancellationToken) => AuditorLogin.SetPasswordAsync(context, auditorPassword, cancellationToken)));
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<INotebookRepository, NotebookRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IConceptRepository, ConceptRepository>();
        services.AddScoped<IPracticeRepository, PracticeRepository>();

        var blobRoot = configuration["BlobStorage:Root"] ?? "/var/talk-to-rag/blobs";
        services.AddSingleton<IBlobStorage>(new LocalBlobStorage(blobRoot));

        services.AddSingleton<ISourceIngestionQueue, ChannelSourceIngestionQueue>();

        var aiSection = configuration.GetSection(AiServiceOptions.Section);
        var aiOptions = aiSection.Get<AiServiceOptions>() ?? new AiServiceOptions();
        services.Configure<AiServiceOptions>(aiSection);
        services.AddHttpClient<IAiClient, HttpAiClient>(client =>
        {
            client.BaseAddress = new Uri(aiOptions.BaseUrl);
            // HttpAiClient gives each call its own limit.
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler<AiAuditHandler>();
        services.AddTransient<AiAuditHandler>();

        services.AddHostedService<SourceIngestionWorker>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.Section))
            .PostConfigure(jwt => jwt.LoadSigningKey());
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();

        return services;
    }
}
