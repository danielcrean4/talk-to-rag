namespace TalkToRag.Application.Practice;

public interface IPracticeService
{
    Task<IReadOnlyList<QuestionDto>> ListQuestionsAsync(Guid conceptId, CancellationToken cancellationToken);
    Task<QuestionDto> AddQuestionAsync(Guid conceptId, CreateQuestionRequest request, CancellationToken cancellationToken);

    /// <summary>Has the local model write flashcards from the passages linked to a concept.</summary>
    Task<IReadOnlyList<QuestionDto>> GenerateQuestionsAsync(Guid conceptId, CancellationToken cancellationToken);
    Task DeleteQuestionAsync(Guid id, CancellationToken cancellationToken);
    Task<AttemptResultDto> RecordAttemptAsync(RecordAttemptRequest request, CancellationToken cancellationToken);
}
