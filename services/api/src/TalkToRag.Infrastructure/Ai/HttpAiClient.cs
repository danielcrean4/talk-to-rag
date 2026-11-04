using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
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

    public async Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/v1/rag/expand-query", request, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = TryExtractErrorMessage(errorBody) ?? "AI embedding service unavailable.";
            throw new AiServiceUnavailableException(message);
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ExpandQueryResponse>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("AI service returned null response.");
    }

    public async Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/v1/rag/answer", request, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var message = TryExtractErrorMessage(errorBody) ?? "AI chat service unavailable.";
            throw new AiServiceUnavailableException(message);
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AnswerResponse>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("AI service returned null response.");
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/v1/rag/answer/stream")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrEmpty(line))
            {
                yield return line;
            }
        }
    }

    public async Task<string> TranscribeAsync(string blobKey, CancellationToken cancellationToken = default)
    {
        var payload = new { blob_key = blobKey };
        using var response = await httpClient.PostAsJsonAsync("/v1/speech/transcribe", payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
        return doc?.RootElement.GetProperty("text").GetString() ?? "";
    }

    public async Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new { text };
        var response = await httpClient.PostAsJsonAsync("/v1/speech/synthesize", payload, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
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
