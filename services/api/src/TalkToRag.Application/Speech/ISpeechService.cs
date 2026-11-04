using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.Application.Speech;

public sealed record TranscribeResponse(string Text, string BlobKey);

public sealed record PullUpCandidateDto(
    int Index,
    Guid SourceId,
    string SourceTitle,
    int PageNumber,
    string? PageLabel,
    string? Section,
    string Snippet);

public sealed record PullUpResponseDto(
    string Topic,
    string SpokenPrompt,
    IReadOnlyList<PullUpCandidateDto> Candidates);

public interface ISpeechService
{
    Task<TranscribeResponse> TranscribeAsync(Stream audioStream, string contentType, CancellationToken cancellationToken);
    Task<Stream> SynthesizeAsync(string text, CancellationToken cancellationToken);
    Task<RouteIntentResponseDto> RouteIntentAsync(string transcript, ScreenContextDto? context, CancellationToken cancellationToken);
    Task<PullUpResponseDto> PullUpTopicAsync(string topic, Guid? notebookId, CancellationToken cancellationToken);
}
