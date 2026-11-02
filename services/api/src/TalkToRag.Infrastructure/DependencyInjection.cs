using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TalkToRag.Application.Common.Interfaces;
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

        services.AddDbContext<TalkToRagDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ISourceRepository, SourceRepository>();
        services.AddScoped<INotebookRepository, NotebookRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ISearchRepository, SearchRepository>();

        var blobRoot = configuration["BlobStorage:Root"] ?? "/var/talk-to-rag/blobs";
        services.AddSingleton<IBlobStorage>(new LocalBlobStorage(blobRoot));

        services.AddSingleton<ISourceIngestionQueue, ChannelSourceIngestionQueue>();

        var aiBaseUrl = configuration["AiService:BaseUrl"] ?? "http://localhost:8000";
        services.AddHttpClient<IAiClient, HttpAiClient>(client =>
        {
            client.BaseAddress = new Uri(aiBaseUrl);
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddHostedService<SourceIngestionWorker>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.AddSingleton<ITokenGenerator, JwtTokenGenerator>();

        return services;
    }
}
