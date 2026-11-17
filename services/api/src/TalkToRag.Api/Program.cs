using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TalkToRag.Api.Errors;
using TalkToRag.Api.Security;
using TalkToRag.Application;
using TalkToRag.Application.Auth;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Search;
using TalkToRag.Infrastructure;
using TalkToRag.Infrastructure.Persistence;
using TalkToRag.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

builder.Services.Configure<SearchOptions>(builder.Configuration.GetSection(SearchOptions.SectionName));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, ClaimsCurrentUser>();
builder.Services.AddSingleton<IAuditContext, HttpAuditContext>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => options.MapInboundClaims = false) // keep "sub" as "sub"
    .AddJwtBearer(SourceFileLink.Scheme, options => options.MapInboundClaims = false);
// Validate tokens with the same settings, and the same key, JwtTokenGenerator signs them with.
// A sign-in token is only read from the Authorization header, never from the URL.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwt) => options.TokenValidationParameters = jwt.Value.SignInValidation());
builder.Services
    .AddOptions<JwtBearerOptions>(SourceFileLink.Scheme)
    .Configure<IOptions<JwtOptions>>((options, jwt) => SourceFileLink.Configure(options, jwt.Value));
builder.Services.AddAuthorization();

var app = builder.Build();

// Fail now, not at the first sign-in, when there is no usable signing key.
app.Services.GetRequiredService<IOptions<JwtOptions>>().Value.CreateSigningKey();

// Compose turns this on so a new install has its schema before the first request and before
// the ingestion worker starts. It is off by default, so tests and `dotnet run` leave the schema alone.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<TalkToRagDbContext>().Database.Migrate();
}

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    // The API contract as JSON at /openapi/v1.json, like FastAPI's /openapi.json.
    app.MapOpenApi();
}

// The Angular app, when wwwroot holds its build (the Docker image does). Without it the API runs on
// its own and the Angular dev server is the UI. index.html names the hashed bundles of the current
// build, so browsers check it on every load instead of keeping one from before an upgrade.
var appFiles = new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        if (context.File.Name == "index.html")
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
};
// The app routes in the browser (/sources, /history/{id}), so a page request no endpoint matched
// gets its index.html. Routing has already run here: an unknown /api path stays a 404 and a wrong
// method a 405.
app.Use((context, next) =>
{
    if (context.GetEndpoint() is null && IsAppPageRequest(context.Request))
    {
        context.Request.Path = "/index.html";
    }
    return next(context);
});
app.UseStaticFiles(appFiles);
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static bool IsAppPageRequest(HttpRequest request) =>
    (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)) &&
    !request.Path.StartsWithSegments("/api") &&
    !Path.HasExtension(request.Path.Value);

/// <summary>Lets <see cref="WebApplicationFactory{TEntryPoint}"/> tests start the app.</summary>
public partial class Program;
