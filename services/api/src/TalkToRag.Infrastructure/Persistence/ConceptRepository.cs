using Microsoft.EntityFrameworkCore;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class ConceptRepository(TalkToRagDbContext db) : IConceptRepository
{
    public Task<List<Concept>> ListForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Concepts
            .Include(c => c.Passages)
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

    public Task<List<ConceptPrerequisite>> ListEdgesForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        db.ConceptPrerequisites
            .Where(e => db.Concepts.Any(c => c.Id == e.ConceptId && c.UserId == userId))
            .ToListAsync(cancellationToken);

    public void Add(Concept concept) => db.Concepts.Add(concept);

    public void Add(ConceptPrerequisite edge) => db.ConceptPrerequisites.Add(edge);

    public void Add(ConceptPassage passage) => db.ConceptPassages.Add(passage);

    public void Remove(Concept concept) => db.Concepts.Remove(concept);

    public void Remove(ConceptPassage passage) => db.ConceptPassages.Remove(passage);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
