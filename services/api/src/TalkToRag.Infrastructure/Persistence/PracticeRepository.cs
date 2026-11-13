using Microsoft.EntityFrameworkCore;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class PracticeRepository(TalkToRagDbContext db) : IPracticeRepository
{
    public Task<List<Question>> ListQuestionsAsync(Guid conceptId, Guid userId, CancellationToken cancellationToken) =>
        db.Questions
            .Where(q => q.ConceptId == conceptId && q.UserId == userId)
            .OrderBy(q => q.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<Question?> FindQuestionAsync(Guid id, Guid userId, CancellationToken cancellationToken) =>
        db.Questions.FirstOrDefaultAsync(q => q.Id == id && q.UserId == userId, cancellationToken);

    public Task<List<Attempt>> ListAttemptsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Attempts.AsNoTracking().Where(a => a.UserId == userId).ToListAsync(cancellationToken);

    public async Task<string> GetTextAtAsync(Guid sourceId, int pageNumber, string? section, int maxLength, CancellationToken cancellationToken)
    {
        var parts = await db.SourceChunks
            .Where(c => c.SourceId == sourceId && c.PageNumber == pageNumber && c.Section == section)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => c.Content)
            .ToListAsync(cancellationToken);
        var text = string.Join("\n\n", parts);
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    public void Add(Question question) => db.Questions.Add(question);

    public void Add(Attempt attempt) => db.Attempts.Add(attempt);

    public void Remove(Question question) => db.Questions.Remove(question);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
