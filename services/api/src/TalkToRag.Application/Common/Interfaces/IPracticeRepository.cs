using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public interface IPracticeRepository
{
    Task<List<Question>> ListQuestionsAsync(Guid conceptId, Guid userId, CancellationToken cancellationToken);
    Task<Question?> FindQuestionAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task<List<Attempt>> ListAttemptsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The text of a source at one place, joined from its chunks and cut to <paramref name="maxLength"/>.</summary>
    Task<string> GetTextAtAsync(Guid sourceId, int pageNumber, string? section, int maxLength, CancellationToken cancellationToken);

    void Add(Question question);
    void Add(Attempt attempt);
    void Remove(Question question);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
