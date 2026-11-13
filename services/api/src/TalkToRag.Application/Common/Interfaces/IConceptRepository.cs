using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

public interface IConceptRepository
{
    /// <summary>Every concept of the user, in any state, with its passages.</summary>
    Task<List<Concept>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every prerequisite edge between the user's concepts, in any state.</summary>
    Task<List<ConceptPrerequisite>> ListEdgesForUserAsync(Guid userId, CancellationToken cancellationToken);

    void Add(Concept concept);
    void Add(ConceptPrerequisite edge);
    void Add(ConceptPassage passage);
    void Remove(Concept concept);
    void Remove(ConceptPassage passage);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
