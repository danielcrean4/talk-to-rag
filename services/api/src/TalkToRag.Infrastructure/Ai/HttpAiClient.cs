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
            throw new AiIngestionException(await ErrorMessageAsync(response, cancellationToken) ?? "Document rejected by AI service.");
        }

        return await ReadAsync<IngestSourceResponse>(response, "AI embedding service unavailable.", cancellationToken);
    }

    public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<ExpandQueryRequest, ExpandQueryResponse>("/v1/rag/expand-query", request, "AI embedding service unavailable.", cancellationToken);

    public Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<AnswerRequest, AnswerResponse>("/v1/rag/answer", request, "AI chat service unavailable.", cancellationToken);

    public async IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/v1/rag/answer/stream")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        using var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new AiServiceUnavailableException(await ErrorMessageAsync(response, cancellationToken) ?? "AI chat service unavailable.");
        }

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

    public Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<TranscribeRequest, TranscribeResponse>("/v1/speech/transcribe", request, "Speech-to-text is unavailable.", cancellationToken);

    public async Task<SynthesizeResponse> SynthesizeAsync(SynthesizeRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/v1/speech/synthesize", request, JsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new RequestValidationException(await ErrorMessageAsync(response, cancellationToken) ?? "The text cannot be spoken.");
        }

        return await ReadAsync<SynthesizeResponse>(response, "Text-to-speech is unavailable.", cancellationToken);
    }

    public Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<ProposePrerequisitesRequest, ProposePrerequisitesResponse>("/v1/concepts/propose-prerequisites", request, "AI chat service unavailable.", cancellationToken);

    public Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateQuestionsRequest, GenerateQuestionsResponse>("/v1/concepts/generate-questions", request, "AI chat service unavailable.", cancellationToken);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, string unavailableMessage, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(path, request, JsonOptions, cancellationToken);
        return await ReadAsync<TResponse>(response, unavailableMessage, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string unavailableMessage, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new AiServiceUnavailableException(await ErrorMessageAsync(response, cancellationToken) ?? unavailableMessage);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("AI service returned null response.");
    }

    private static async Task<string?> ErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        TryExtractErrorMessage(await response.Content.ReadAsStringAsync(cancellationToken));

    private static string? TryExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.ValueKind == JsonValueKind.String ? detail.GetString() : detail.ToString();
            }
        }
        catch (JsonException) { }

        return string.IsNullOrWhiteSpace(json) ? null : json;
    }
}
