using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Infrastructure.Ai;

/// <summary>
/// Calls the AI service. Each call has its own time limit (<see cref="AiServiceOptions"/>), and running out of time
/// is reported as <see cref="AiServiceUnavailableException"/>, as when the service answers 503.
/// </summary>
public sealed class HttpAiClient(HttpClient httpClient, IOptions<AiServiceOptions>? options = null) : IAiClient
{
    public const string TimedOutMessage = "The model did not answer in time.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly TimeSpan _ingestTimeout = TimeSpan.FromSeconds((options?.Value ?? new AiServiceOptions()).IngestTimeoutSeconds);
    private readonly TimeSpan _interactiveTimeout = TimeSpan.FromSeconds((options?.Value ?? new AiServiceOptions()).InteractiveTimeoutSeconds);

    public Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default) =>
        WithTimeoutAsync(_ingestTimeout, cancellationToken, async token =>
        {
            using var response = await httpClient.PostAsJsonAsync("/v1/sources/ingest", request, JsonOptions, token);
            if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
            {
                throw new AiIngestionException(await ErrorMessageAsync(response, token) ?? "Document rejected by AI service.");
            }

            return await ReadAsync<IngestSourceResponse>(response, "AI embedding service unavailable.", token);
        });

    public Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<ExpandQueryRequest, ExpandQueryResponse>("/v1/rag/expand-query", request, "AI embedding service unavailable.", cancellationToken);

    public Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<AnswerRequest, AnswerResponse>("/v1/rag/answer", request, "AI chat service unavailable.", cancellationToken);

    /// <summary>
    /// The time limit covers the whole stream. Running out before the AI service answers throws; running out once
    /// it is streaming ends the stream with an error event, as the AI service does when the model fails.
    /// </summary>
    public async IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_interactiveTimeout);

        using var response = await StartStreamAsync(request, timeout.Token, cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (line is null)
            {
                yield break;
            }

            if (line.Length > 0)
            {
                yield return line;
            }
        }

        yield return "data: " + JsonSerializer.Serialize(new { error = TimedOutMessage });
    }

    private async Task<HttpResponseMessage> StartStreamAsync(AnswerRequest request, CancellationToken timeoutToken, CancellationToken cancellationToken)
    {
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/v1/rag/answer/stream")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, timeoutToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceUnavailableException(TimedOutMessage);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            using (response)
            {
                throw new AiServiceUnavailableException(await ErrorMessageAsync(response, cancellationToken) ?? "AI chat service unavailable.");
            }
        }

        return response.EnsureSuccessStatusCode();
    }

    public Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken cancellationToken = default) =>
        WithTimeoutAsync(_interactiveTimeout, cancellationToken, async token =>
        {
            using var response = await httpClient.PostAsJsonAsync("/v1/speech/transcribe", request, JsonOptions, token);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new RequestValidationException(await ErrorMessageAsync(response, token) ?? "The audio could not be read.");
            }

            return await ReadAsync<TranscribeResponse>(response, "Speech-to-text is unavailable.", token);
        });

    public Task<SynthesizeResponse> SynthesizeAsync(SynthesizeRequest request, CancellationToken cancellationToken = default) =>
        WithTimeoutAsync(_interactiveTimeout, cancellationToken, async token =>
        {
            using var response = await httpClient.PostAsJsonAsync("/v1/speech/synthesize", request, JsonOptions, token);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                throw new RequestValidationException(await ErrorMessageAsync(response, token) ?? "The text cannot be spoken.");
            }

            return await ReadAsync<SynthesizeResponse>(response, "Text-to-speech is unavailable.", token);
        });

    public Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<ProposePrerequisitesRequest, ProposePrerequisitesResponse>("/v1/concepts/propose-prerequisites", request, "AI chat service unavailable.", cancellationToken);

    public Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<GenerateQuestionsRequest, GenerateQuestionsResponse>("/v1/concepts/generate-questions", request, "AI chat service unavailable.", cancellationToken);

    private Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest request, string unavailableMessage, CancellationToken cancellationToken) =>
        WithTimeoutAsync(_interactiveTimeout, cancellationToken, async token =>
        {
            using var response = await httpClient.PostAsJsonAsync(path, request, JsonOptions, token);
            return await ReadAsync<TResponse>(response, unavailableMessage, token);
        });

    private static async Task<T> WithTimeoutAsync<T>(TimeSpan limit, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> call)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        try
        {
            return await call(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceUnavailableException(TimedOutMessage);
        }
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
