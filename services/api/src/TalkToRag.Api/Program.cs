using TalkToRag.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // The API contract as JSON at /openapi/v1.json, like FastAPI's /openapi.json.
    app.MapOpenApi();
}

app.MapControllers();

app.Run();
