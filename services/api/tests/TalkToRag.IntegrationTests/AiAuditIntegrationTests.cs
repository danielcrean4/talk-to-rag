using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Testcontainers.PostgreSql;
using TalkToRag.Api.Security;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Speech;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;
using TalkToRag.Infrastructure.Ai;
using TalkToRag.Infrastructure.Audit;
using TalkToRag.Infrastructure.Ingestion;
using TalkToRag.Infrastructure.Persistence;
using TalkToRag.Infrastructure.Storage;

namespace TalkToRag.IntegrationTests;

/// <summary>Which user the rows that <see cref="AiAuditHandler"/> writes to ai_calls belong to.</summary>
public class AiAuditIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg17")
        .Build();

    private readonly HttpContextAccessor _httpContext = new();
    private readonly StubAiService _aiService = new();
    private readonly string _blobRoot = Path.Combine(Path.GetTempPath(), $"talk-to-rag-audit-{Guid.NewGuid()}");
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        _services = new ServiceCollection()
            .AddDbContext<TalkToRagDbContext>(options => options
                .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.UseVector())
                .UseSnakeCaseNamingConvention())
            .AddSingleton<IHttpContextAccessor>(_httpContext)
            .AddSingleton<IAuditContext, HttpAuditContext>()
            .AddScoped<ISourceRepository, SourceRepository>()
            .AddScoped<IAiClient>(provider =>
            {
                var audit = new AiAuditHandler(
                    provider.GetRequiredService<IServiceScopeFactory>(),
                    provider.GetRequiredService<IAuditContext>(),
                    TimeProvider.System,
                    NullLogger<AiAuditHandler>.Instance) { InnerHandler = _aiService };
                return new HttpAiClient(new HttpClient(audit, disposeHandler: false) { BaseAddress = new Uri("http://ai.test") });
            })
            .BuildServiceProvider();

        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TalkToRagDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
        if (Directory.Exists(_blobRoot))
        {
            Directory.Delete(_blobRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Ingestion_OutsideARequest_IsRecordedForTheSourceOwner()
    {
        var owner = await AddUserAsync("owner@test.com");
        var source = new Source
        {
            Id = Guid.NewGuid(),
            UserId = owner.Id,
            Title = "Linear Algebra",
            FileName = "la.txt",
            ContentType = "text/plain",
            BlobKey = "sources/la.txt",
            Status = SourceStatus.Pending
        };
        await using (var db = NewDbContext())
        {
            db.Sources.Add(source);
            await db.SaveChangesAsync();
        }

        // The worker queues every Pending source when it starts.
        var worker = new SourceIngestionWorker(
            _services.GetRequiredService<IServiceScopeFactory>(),
            new ChannelSourceIngestionQueue(),
            NullLogger<SourceIngestionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && await StatusAsync(source.Id) != SourceStatus.Ready)
        {
            await Task.Delay(100);
        }

        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(SourceStatus.Ready, await StatusAsync(source.Id));
        await using var check = NewDbContext();
        var call = await check.AiCalls.SingleAsync(c => c.Operation == "/v1/sources/ingest");
        Assert.Equal(owner.Id, call.UserId);
        Assert.Null(call.RequestTraceId);
        var chunk = await check.SourceChunks.SingleAsync(c => c.SourceId == source.Id);
        Assert.Equal("test-embed", chunk.EmbeddingModel);
    }

    [Fact]
    public async Task Transcription_InARequest_IsRecordedForTheSignedInUser()
    {
        var user = await AddUserAsync("speaker@test.com");
        SignIn(user.Id);
        var speech = new SpeechService(new LocalBlobStorage(_blobRoot), CreateAiClient(), new ClaimsCurrentUser(_httpContext));

        var result = await speech.TranscribeAsync("audio/webm;codecs=opus", new MemoryStream([1, 2, 3]));

        Assert.Equal("hello there", result.Text);
        await using var check = NewDbContext();
        var call = await check.AiCalls.SingleAsync();
        Assert.Equal("/v1/speech/transcribe", call.Operation);
        Assert.Equal(user.Id, call.UserId);
        Assert.Equal(_httpContext.HttpContext!.TraceIdentifier, call.RequestTraceId);
        Assert.Equal(200, call.StatusCode);
    }

    [Fact]
    public async Task SignedInUser_IsPreferredOverTheBackgroundUser()
    {
        var signedIn = await AddUserAsync("signed-in@test.com");
        var other = await AddUserAsync("other@test.com");
        SignIn(signedIn.Id);

        using (BackgroundAuditUser.Begin(other.Id))
        {
            await CreateAiClient().TranscribeAsync(new TranscribeRequest("audio/x.webm"));
        }

        await using var check = NewDbContext();
        Assert.Equal(signedIn.Id, (await check.AiCalls.SingleAsync()).UserId);
    }

    private IAiClient CreateAiClient() => _services.CreateScope().ServiceProvider.GetRequiredService<IAiClient>();

    private TalkToRagDbContext NewDbContext() => _services.CreateScope().ServiceProvider.GetRequiredService<TalkToRagDbContext>();

    private void SignIn(Guid userId) =>
        _httpContext.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())], "Bearer"))
        };

    private async Task<User> AddUserAsync(string email)
    {
        var user = new User { Id = Guid.NewGuid(), Email = email, DisplayName = email, PasswordHash = "hash" };
        await using var db = NewDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<SourceStatus> StatusAsync(Guid sourceId)
    {
        await using var db = NewDbContext();
        return await db.Sources.Where(s => s.Id == sourceId).Select(s => s.Status).SingleAsync();
    }

    private sealed class StubAiService : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/v1/sources/ingest" =>
                    $$"""
                    {
                      "embedding_model": "test-embed",
                      "chunks": [{
                        "page_number": 1, "page_label": null, "section": null, "chunk_index": 0,
                        "content": "Eigenvectors keep their direction.",
                        "embedding": [{{string.Join(",", Enumerable.Repeat("0.1", SourceChunk.EmbeddingDimensions))}}]
                      }],
                      "trace": []
                    }
                    """,
                "/v1/speech/transcribe" => """{"text": " hello there "}""",
                _ => throw new InvalidOperationException($"Unexpected AI call {request.RequestUri}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
