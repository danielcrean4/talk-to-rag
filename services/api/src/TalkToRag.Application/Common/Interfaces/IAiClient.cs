namespace TalkToRag.Application.Common.Interfaces;

public sealed record IngestSourceRequest(
    Guid SourceId,
    string BlobKey,
    string ContentType);

public sealed record IngestedChunkDto(
    int PageNumber,
    string? PageLabel,
    string? Section,
    int ChunkIndex,
    string Content,
    float[] Embedding);

/// <param name="EmbeddingModel">The id the embedding server knows the model by, such as "nomic-embed-text". Null from an AI service older than this field.</param>
public sealed record IngestSourceResponse(
    string? EmbeddingModel,
    IReadOnlyList<IngestedChunkDto> Chunks);

public sealed record RecentMessageDto(string Role, string Content);

public sealed record ExpandQueryRequest(
    string Question,
    IReadOnlyList<RecentMessageDto> RecentMessages,
    bool Expand);

public sealed record QueryItemDto(
    string Text,
    float[] Embedding);

public sealed record ExpandQueryResponse(
    IReadOnlyList<QueryItemDto> Queries);

public sealed record PassageItemDto(
    int N,
    string Content,
    string? SourceTitle = null,
    int? PageNumber = null,
    string? PageLabel = null,
    string? Section = null);

public sealed record AnswerRequest(
    string Question,
    IReadOnlyList<PassageItemDto> Passages);

public sealed record AnswerResponse(
    string Answer,
    IReadOnlyList<int> Cited);

public sealed record TranscribeRequest(string BlobKey);

public sealed record TranscribeResponse(string Text);

public sealed record SynthesizeRequest(string Text, string? Voice, string BlobKey);

public sealed record SynthesizeResponse(string BlobKey, string ContentType, double DurationSeconds);

public sealed record ProposePrerequisitesRequest(string Concept, string? Summary, IReadOnlyList<string> KnownConcepts);

public sealed record ProposedConceptDto(string Name, string? Summary);

public sealed record ProposePrerequisitesResponse(IReadOnlyList<ProposedConceptDto> Prerequisites);

public sealed record QuestionPassageDto(int N, string Text);

public sealed record GenerateQuestionsRequest(string Concept, IReadOnlyList<QuestionPassageDto> Passages, int Count);

public sealed record GeneratedQuestionDto(string Prompt, string Answer, int? Passage);

public sealed record GenerateQuestionsResponse(IReadOnlyList<GeneratedQuestionDto> Questions);

public interface IAiClient
{
    Task<IngestSourceResponse> IngestSourceAsync(IngestSourceRequest request, CancellationToken cancellationToken = default);
    Task<ExpandQueryResponse> ExpandQueryAsync(ExpandQueryRequest request, CancellationToken cancellationToken = default);
    Task<AnswerResponse> GenerateAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAnswerAsync(AnswerRequest request, CancellationToken cancellationToken = default);
    Task<TranscribeResponse> TranscribeAsync(TranscribeRequest request, CancellationToken cancellationToken = default);
    Task<SynthesizeResponse> SynthesizeAsync(SynthesizeRequest request, CancellationToken cancellationToken = default);
    Task<ProposePrerequisitesResponse> ProposePrerequisitesAsync(ProposePrerequisitesRequest request, CancellationToken cancellationToken = default);
    Task<GenerateQuestionsResponse> GenerateQuestionsAsync(GenerateQuestionsRequest request, CancellationToken cancellationToken = default);
}
