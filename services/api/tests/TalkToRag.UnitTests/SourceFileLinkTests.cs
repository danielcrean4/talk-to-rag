using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Sources;
using TalkToRag.Domain.Entities;

namespace TalkToRag.UnitTests;

/// <summary>Which tokens open a source file through the real authentication setup in Program.cs.</summary>
public sealed class SourceFileLinkTests : IClassFixture<SourceFileLinkTests.App>, IDisposable
{
    private static readonly Guid Owner = Guid.Parse("0192d6a0-0000-7000-8000-000000000001");
    private static readonly Guid Book = Guid.Parse("0192d6a0-0000-7000-8000-0000000000b1");
    private static readonly Guid OtherBook = Guid.Parse("0192d6a0-0000-7000-8000-0000000000b2");

    private readonly App _app;
    private readonly HttpClient _client;

    public SourceFileLinkTests(App app)
    {
        _app = app;
        _client = app.CreateClient();
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task FileLink_OpensTheFile_WithoutASignInHeader()
    {
        var link = await CreateLinkAsync(Book);

        var response = await _client.GetAsync(link.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith($"/api/sources/{Book}/file?access=", link.Url);
        Assert.InRange(link.ExpiresAtUtc - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task FileLink_DoesNotOpenAnotherFile()
    {
        var link = await CreateLinkAsync(Book);

        var response = await _client.GetAsync(link.Url.Replace(Book.ToString(), OtherBook.ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FileLink_IsNotASignInToken()
    {
        var link = await CreateLinkAsync(Book);
        var token = Uri.UnescapeDataString(link.Url[(link.Url.IndexOf('=') + 1)..]);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sources");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("access")]
    public async Task SignInToken_IsRefusedInTheQueryString(string parameter)
    {
        var response = await _client.GetAsync($"/api/sources/{Book}/file?{parameter}={Uri.EscapeDataString(SignInToken())}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignInHeader_StillOpensTheFile()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/sources/{Book}/file");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SignInToken());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FileLink_IsOnlyGivenForTheCallersOwnSource()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sources/{OtherBook}/file-link");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SignInToken());

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<SourceFileLinkResponse> CreateLinkAsync(Guid sourceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sources/{sourceId}/file-link");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SignInToken());
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SourceFileLinkResponse>())!;
    }

    private string SignInToken() =>
        _app.Services.GetRequiredService<ITokenGenerator>()
            .CreateToken(new User { Id = Owner, Email = "dan@example.com", DisplayName = "Dan", PasswordHash = "x" })
            .Token;

    public sealed class App : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not Development, which loads the build's static web assets manifest and fails when it names a missing wwwroot.
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:TalkToRag", "Host=127.0.0.1;Database=unused");
            builder.UseSetting("Jwt:SigningKey", "test-signing-key-that-is-at-least-32-bytes");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddScoped<ISourceService, OwnersBooks>();
            });
        }
    }

    /// <summary>The owner has <see cref="Book"/>; <see cref="OtherBook"/> belongs to someone else.</summary>
    private sealed class OwnersBooks(ICurrentUser currentUser) : ISourceService
    {
        public Task<SourceDto> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new SourceDto(Require(id), "Book", "book.pdf", "application/pdf", 3, "Ready", null, 1, DateTimeOffset.UnixEpoch, []));

        public Task<SourceFileResponse> GetFileAsync(Guid id, CancellationToken cancellationToken)
        {
            Require(id);
            return Task.FromResult(new SourceFileResponse(new MemoryStream([1, 2, 3]), "application/pdf", "book.pdf"));
        }

        private Guid Require(Guid id) =>
            id == Book && currentUser.UserId == Owner ? id : throw new NotFoundException($"Source '{id}' not found.");

        public Task<CreateSourceResponse> UploadAsync(string fileName, Stream stream, string? license, string? sourceUrl, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<SourceDto>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceDto>>([]);

        public Task<SourceDto> UpdateAsync(Guid id, UpdateSourceRequest request, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task RetryAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
