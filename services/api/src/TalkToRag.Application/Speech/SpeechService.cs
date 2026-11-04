using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Search;

namespace TalkToRag.Application.Speech;

public sealed class SpeechService(
    IBlobStorage blobStorage,
    IAiClient aiClient,
    ISearchService searchService,
    ICurrentUser currentUser) : ISpeechService
{
    private const long MaxAudioSizeBytes = 25 * 1024 * 1024; // 25 MB

    public async Task<TranscribeResponse> TranscribeAsync(Stream audioStream, string contentType, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId; // Enforce user auth

        if (audioStream.CanSeek && audioStream.Length == 0)
        {
            throw new RequestValidationException("Audio stream cannot be empty.");
        }

        if (audioStream.CanSeek && audioStream.Length > MaxAudioSizeBytes)
        {
            throw new RequestValidationException("Audio file exceeds 25 MB limit.");
        }

        var speechId = Guid.CreateVersion7();
        var extension = contentType.Contains("wav") ? ".wav" : ".webm";
        var blobKey = $"speech/{speechId:N}{extension}";

        await blobStorage.WriteAsync(blobKey, audioStream, cancellationToken);
        var transcript = await aiClient.TranscribeAsync(blobKey, cancellationToken);

        return new TranscribeResponse(transcript, blobKey);
    }

    public async Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId; // Enforce user auth

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RequestValidationException("Text to synthesize cannot be empty.");
        }

        return await aiClient.SynthesizeAsync(text, cancellationToken);
    }

    public async Task<RouteIntentResponseDto> RouteIntentAsync(string transcript, ScreenContextDto? context, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId;

        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new RequestValidationException("Transcript cannot be empty.");
        }

        var request = new RouteIntentRequestDto(transcript, context);
        return await aiClient.RouteIntentAsync(request, cancellationToken);
    }

    public async Task<PullUpResponseDto> PullUpTopicAsync(string topic, Guid? notebookId, CancellationToken cancellationToken)
    {
        _ = currentUser.UserId;

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new RequestValidationException("Topic cannot be empty.");
        }

        var searchRequest = new SearchRequest(
            Query: topic,
            NotebookIds: notebookId.HasValue ? [notebookId.Value] : null,
            Limit: 3);

        var searchResponse = await searchService.SearchAsync(searchRequest, cancellationToken);

        var candidates = new List<PullUpCandidateDto>();
        var idx = 1;

        foreach (var group in searchResponse.Groups)
        {
            foreach (var passage in group.Passages)
            {
                candidates.Add(new PullUpCandidateDto(
                    Index: idx++,
                    SourceId: group.Source.Id,
                    SourceTitle: group.Source.Title,
                    PageNumber: passage.PageNumber,
                    PageLabel: passage.PageLabel,
                    Section: passage.Section,
                    Snippet: passage.Snippet));

                if (candidates.Count >= 3)
                {
                    break;
                }
            }

            if (candidates.Count >= 3)
            {
                break;
            }
        }

        var spokenPrompt = candidates.Count switch
        {
            0 => $"No sources found covering {topic}.",
            1 => $"Found 1: {CleanTitle(candidates[0].SourceTitle)} page {candidates[0].PageNumber}. Would you like to open it?",
            2 => $"Found 2: {CleanTitle(candidates[0].SourceTitle)} page {candidates[0].PageNumber}, and {CleanTitle(candidates[1].SourceTitle)} page {candidates[1].PageNumber}. Which one?",
            _ => $"Found 3: {CleanTitle(candidates[0].SourceTitle)} page {candidates[0].PageNumber}, {CleanTitle(candidates[1].SourceTitle)} page {candidates[1].PageNumber}, and {CleanTitle(candidates[2].SourceTitle)} page {candidates[2].PageNumber}. Which one?"
        };

        return new PullUpResponseDto(topic, spokenPrompt, candidates);
    }

    private static string CleanTitle(string title)
    {
        if (title.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return title[..^4];
        }
        return title;
    }
}
