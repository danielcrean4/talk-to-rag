using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Infrastructure.Ai;

public sealed class HttpAiClient(HttpClient httpClient) : IAiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public async Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/v1/sources/ingest", request, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = TryExtractErrorMessage(errorBody) ?? "Document rejected by AI service.";
            throw new AiIngestionException(message);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = TryExtractErrorMessage(errorBody) ?? "AI embedding service unavailable.";
            throw new AiServiceUnavailableException(message);
        }

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<IngestSourceResponse>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("AI service returned null response.");
    }

    private static string? TryExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.ValueKind == JsonValueKind.String ? detail.GetString() : detail.ToString();
            }
        }
        catch
        {
            // Not valid JSON
        }

        return string.IsNullOrWhiteSpace(json) ? null : json;
    }
}
