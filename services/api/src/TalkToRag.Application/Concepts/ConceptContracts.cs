using TalkToRag.Application.Practice;

namespace TalkToRag.Application.Concepts;

public sealed record ConceptDto(Guid Id, string Name, string? Summary, string State, bool ProposedByModel, int PassageCount, MasteryDto Mastery);

/// <summary>"Understand PrerequisiteId before ConceptId."</summary>
public sealed record PrerequisiteDto(Guid ConceptId, Guid PrerequisiteId, string State);

/// <summary>Everything the graph view draws. Rejected concepts and prerequisites are left out.</summary>
public sealed record ConceptGraphDto(IReadOnlyList<ConceptDto> Concepts, IReadOnlyList<PrerequisiteDto> Prerequisites);

public sealed record ConceptPassageDto(Guid Id, Guid SourceId, string SourceTitle, int PageNumber, string? PageLabel, string? Section, string Snippet);

public sealed record ConceptDetailsDto(ConceptDto Concept, IReadOnlyList<ConceptPassageDto> Passages);

/// <summary>A passage search found for a concept; <see cref="Linked"/> says whether it is already attached.</summary>
public sealed record PassageSuggestionDto(Guid SourceId, string SourceTitle, int PageNumber, string? PageLabel, string? Section, string Snippet, bool Linked);

public sealed record CreateConceptRequest(string Name, string? Summary = null);

public sealed record UpdateConceptRequest(string? Name, string? Summary);

public sealed record SplitConceptRequest(IReadOnlyList<string> Names);

public sealed record AddPassageRequest(Guid SourceId, int PageNumber, string? PageLabel, string? Section, string Snippet);
