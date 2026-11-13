namespace TalkToRag.Application.Concepts;

public interface IConceptService
{
    Task<ConceptGraphDto> GetGraphAsync(CancellationToken cancellationToken);
    Task<ConceptDetailsDto> GetDetailsAsync(Guid id, CancellationToken cancellationToken);
    Task<ConceptDto> CreateAsync(CreateConceptRequest request, CancellationToken cancellationToken);
    Task<ConceptDto> UpdateAsync(Guid id, UpdateConceptRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Takes up a proposed concept, along with its proposed links to concepts already accepted.</summary>
    Task<ConceptGraphDto> AcceptAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Hides a concept and stops the model proposing it again.</summary>
    Task<ConceptGraphDto> RejectAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds a prerequisite, or accepts a proposed one. Refuses links that would make a loop.</summary>
    Task<ConceptGraphDto> AddPrerequisiteAsync(Guid id, Guid prerequisiteId, CancellationToken cancellationToken);
    Task<ConceptGraphDto> RemovePrerequisiteAsync(Guid id, Guid prerequisiteId, CancellationToken cancellationToken);

    /// <summary>Breaks a concept into finer ones. The original stays, and the new ones become its prerequisites.</summary>
    Task<ConceptGraphDto> SplitAsync(Guid id, SplitConceptRequest request, CancellationToken cancellationToken);

    /// <summary>Asks the local model what to learn first, and adds its answers as proposals.</summary>
    Task<ConceptGraphDto> ProposePrerequisitesAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Searches the user's sources for passages about a concept, without saving a conversation.</summary>
    Task<IReadOnlyList<PassageSuggestionDto>> SuggestPassagesAsync(Guid id, CancellationToken cancellationToken);
    Task<ConceptPassageDto> AddPassageAsync(Guid id, AddPassageRequest request, CancellationToken cancellationToken);
    Task RemovePassageAsync(Guid id, Guid passageId, CancellationToken cancellationToken);
}
