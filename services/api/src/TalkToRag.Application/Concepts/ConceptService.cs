using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Application.Practice;
using TalkToRag.Domain.Entities;
using TalkToRag.Domain.Enums;

namespace TalkToRag.Application.Concepts;

public sealed class ConceptService(
    IConceptRepository conceptRepository,
    IPracticeRepository practiceRepository,
    ISourceRepository sourceRepository,
    ISearchRepository searchRepository,
    IAiClient aiClient,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IConceptService
{
    private const int MaxNameLength = 120;
    private const int MaxSnippetLength = 400;
    private const int SuggestionLimit = 8;

    /// <summary>One user's whole graph, loaded once per request. A personal graph is small enough for that.</summary>
    private sealed record Graph(List<Concept> Concepts, List<ConceptPrerequisite> Edges, ILookup<Guid, Attempt> Attempts)
    {
        public ConceptDto Describe(Concept concept) => new(
            concept.Id,
            concept.Name,
            concept.Summary,
            concept.State.ToString(),
            concept.ProposedByModel,
            concept.Passages.Count,
            MasteryDto.From(Attempts[concept.Id].ToList()));

        public Concept Require(Guid id) =>
            Concepts.FirstOrDefault(c => c.Id == id) ?? throw new NotFoundException($"Concept '{id}' not found.");

        public Concept? FindByName(string name) =>
            Concepts.FirstOrDefault(c => c.NormalizedName == Concept.Normalize(name));

        public ConceptPrerequisite? FindEdge(Guid conceptId, Guid prerequisiteId) =>
            Edges.FirstOrDefault(e => e.ConceptId == conceptId && e.PrerequisiteId == prerequisiteId);

        /// <summary>True when <paramref name="prerequisiteId"/> already depends on <paramref name="conceptId"/>.</summary>
        public bool WouldLoop(Guid conceptId, Guid prerequisiteId)
        {
            var pending = new Stack<Guid>([prerequisiteId]);
            var seen = new HashSet<Guid>();
            while (pending.TryPop(out var current))
            {
                if (current == conceptId)
                {
                    return true;
                }

                if (!seen.Add(current))
                {
                    continue;
                }

                foreach (var edge in Edges.Where(e => e.ConceptId == current && e.State != ProposalState.Rejected))
                {
                    pending.Push(edge.PrerequisiteId);
                }
            }

            return false;
        }

        public ConceptGraphDto ToDto()
        {
            var visible = Concepts.Where(c => c.State != ProposalState.Rejected).ToList();
            var visibleIds = visible.Select(c => c.Id).ToHashSet();
            return new ConceptGraphDto(
                visible.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(Describe).ToList(),
                Edges
                    .Where(e => e.State != ProposalState.Rejected && visibleIds.Contains(e.ConceptId) && visibleIds.Contains(e.PrerequisiteId))
                    .Select(e => new PrerequisiteDto(e.ConceptId, e.PrerequisiteId, e.State.ToString()))
                    .ToList());
        }
    }

    public async Task<ConceptGraphDto> GetGraphAsync(CancellationToken cancellationToken) =>
        (await LoadAsync(cancellationToken)).ToDto();

    public async Task<ConceptDetailsDto> GetDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        var concept = graph.Require(id);
        var titles = new Dictionary<Guid, string>();
        foreach (var sourceId in concept.Passages.Select(p => p.SourceId).Distinct())
        {
            var source = await sourceRepository.FindByIdForUserAsync(sourceId, currentUser.UserId, cancellationToken);
            titles[sourceId] = source?.Title ?? "Unknown source";
        }

        var passages = concept.Passages
            .OrderBy(p => titles[p.SourceId], StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.PageNumber)
            .Select(p => ToDto(p, titles[p.SourceId]))
            .ToList();
        return new ConceptDetailsDto(graph.Describe(concept), passages);
    }

    public async Task<ConceptDto> CreateAsync(CreateConceptRequest request, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        var concept = TakeUp(graph, request.Name, request.Summary, rejectDuplicates: true);
        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.Describe(concept);
    }

    public async Task<ConceptDto> UpdateAsync(Guid id, UpdateConceptRequest request, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        var concept = graph.Require(id);

        if (request.Name is not null)
        {
            var name = CleanName(request.Name);
            var clash = graph.FindByName(name);
            if (clash is not null && clash.Id != id)
            {
                throw new ConflictException($"Concept '{name}' already exists.");
            }

            concept.Name = name;
            concept.NormalizedName = Concept.Normalize(name);
        }

        if (request.Summary is not null)
        {
            concept.Summary = string.IsNullOrWhiteSpace(request.Summary) ? null : request.Summary.Trim();
        }

        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.Describe(concept);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var concept = (await LoadAsync(cancellationToken)).Require(id);
        conceptRepository.Remove(concept);
        await conceptRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConceptGraphDto> AcceptAsync(Guid id, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        var concept = graph.Require(id);
        concept.State = ProposalState.Accepted;

        var accepted = graph.Concepts.Where(c => c.State == ProposalState.Accepted).Select(c => c.Id).ToHashSet();
        foreach (var edge in graph.Edges.Where(e => e.State == ProposalState.Proposed
                     && (e.ConceptId == id || e.PrerequisiteId == id)
                     && accepted.Contains(e.ConceptId) && accepted.Contains(e.PrerequisiteId)))
        {
            edge.State = ProposalState.Accepted;
        }

        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.ToDto();
    }

    public async Task<ConceptGraphDto> RejectAsync(Guid id, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        graph.Require(id).State = ProposalState.Rejected;
        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.ToDto();
    }

    public async Task<ConceptGraphDto> AddPrerequisiteAsync(Guid id, Guid prerequisiteId, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        var concept = graph.Require(id);
        var prerequisite = graph.Require(prerequisiteId);
        Link(graph, concept, prerequisite, ProposalState.Accepted);
        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.ToDto();
    }

    public async Task<ConceptGraphDto> RemovePrerequisiteAsync(Guid id, Guid prerequisiteId, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        graph.Require(id);
        var edge = graph.FindEdge(id, prerequisiteId)
            ?? throw new NotFoundException("That prerequisite link does not exist.");

        // Kept as Rejected so the model does not propose the same link again.
        edge.State = ProposalState.Rejected;
        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.ToDto();
    }

    public async Task<ConceptGraphDto> SplitAsync(Guid id, SplitConceptRequest request, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        var concept = graph.Require(id);
        var names = (request.Names ?? []).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        if (names.Count == 0)
        {
            throw new RequestValidationException("Give at least one name to split the concept into.");
        }

        foreach (var name in names)
        {
            var part = TakeUp(graph, name, summary: null, rejectDuplicates: false);
            Link(graph, concept, part, ProposalState.Accepted);
        }

        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.ToDto();
    }

    public async Task<ConceptGraphDto> ProposePrerequisitesAsync(Guid id, CancellationToken cancellationToken)
    {
        var graph = await LoadAsync(cancellationToken);
        var concept = graph.Require(id);
        var known = graph.Concepts
            .Where(c => c.State == ProposalState.Accepted && c.Id != id)
            .Select(c => c.Name)
            .ToList();

        var response = await aiClient.ProposePrerequisitesAsync(
            new ProposePrerequisitesRequest(concept.Name, concept.Summary, known), cancellationToken);

        foreach (var proposal in response.Prerequisites)
        {
            if (string.IsNullOrWhiteSpace(proposal.Name))
            {
                continue;
            }

            var name = CleanName(proposal.Name);
            var prerequisite = graph.FindByName(name);
            if (prerequisite is null)
            {
                prerequisite = NewConcept(name, proposal.Summary, ProposalState.Proposed, proposedByModel: true);
                graph.Concepts.Add(prerequisite);
                conceptRepository.Add(prerequisite);
            }

            var alreadyDecided = prerequisite.State == ProposalState.Rejected || graph.FindEdge(id, prerequisite.Id) is not null;
            if (alreadyDecided || prerequisite.Id == id || graph.WouldLoop(id, prerequisite.Id))
            {
                continue;
            }

            AddEdge(graph, id, prerequisite.Id, ProposalState.Proposed);
        }

        await conceptRepository.SaveChangesAsync(cancellationToken);
        return graph.ToDto();
    }

    public async Task<IReadOnlyList<PassageSuggestionDto>> SuggestPassagesAsync(Guid id, CancellationToken cancellationToken)
    {
        var concept = (await LoadAsync(cancellationToken)).Require(id);
        var query = string.IsNullOrWhiteSpace(concept.Summary) ? concept.Name : $"{concept.Name}: {concept.Summary}";

        var expanded = await aiClient.ExpandQueryAsync(new ExpandQueryRequest(query, [], Expand: false), cancellationToken);
        var hits = await searchRepository.SearchAsync(
            new SearchQueryParameters(
                currentUser.UserId,
                query,
                expanded.Queries.Select(q => q.Embedding).ToList(),
                AllowedSourceIds: null,
                Mode: "hybrid",
                MaxCosineDistance: null,
                Limit: SuggestionLimit),
            cancellationToken);

        return hits
            .Select(hit => new PassageSuggestionDto(
                hit.SourceId,
                hit.SourceTitle,
                hit.PageNumber,
                hit.PageLabel,
                hit.Section,
                Shorten(hit.Content),
                Linked: concept.Passages.Any(p => IsSamePlace(p, hit.SourceId, hit.PageNumber, hit.Section))))
            .ToList();
    }

    public async Task<ConceptPassageDto> AddPassageAsync(Guid id, AddPassageRequest request, CancellationToken cancellationToken)
    {
        var concept = (await LoadAsync(cancellationToken)).Require(id);
        var source = await sourceRepository.FindByIdForUserAsync(request.SourceId, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Source '{request.SourceId}' not found.");
        if (request.PageNumber < 1)
        {
            throw new RequestValidationException("Page number starts at 1.");
        }

        if (concept.Passages.Any(p => IsSamePlace(p, request.SourceId, request.PageNumber, request.Section)))
        {
            throw new ConflictException("That passage is already linked to this concept.");
        }

        var passage = new ConceptPassage
        {
            Id = Guid.CreateVersion7(),
            ConceptId = id,
            SourceId = request.SourceId,
            PageNumber = request.PageNumber,
            PageLabel = request.PageLabel,
            Section = request.Section,
            Snippet = Shorten(request.Snippet ?? string.Empty),
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        conceptRepository.Add(passage);
        await conceptRepository.SaveChangesAsync(cancellationToken);
        return ToDto(passage, source.Title);
    }

    public async Task RemovePassageAsync(Guid id, Guid passageId, CancellationToken cancellationToken)
    {
        var concept = (await LoadAsync(cancellationToken)).Require(id);
        var passage = concept.Passages.FirstOrDefault(p => p.Id == passageId)
            ?? throw new NotFoundException($"Passage '{passageId}' not found.");
        conceptRepository.Remove(passage);
        await conceptRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<Graph> LoadAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var attempts = await practiceRepository.ListAttemptsAsync(userId, cancellationToken);
        return new Graph(
            await conceptRepository.ListForUserAsync(userId, cancellationToken),
            await conceptRepository.ListEdgesForUserAsync(userId, cancellationToken),
            attempts.ToLookup(a => a.ConceptId));
    }

    /// <summary>Returns the accepted concept with this name, creating it or reviving a proposed or rejected one.</summary>
    private Concept TakeUp(Graph graph, string rawName, string? summary, bool rejectDuplicates)
    {
        var name = CleanName(rawName);
        var existing = graph.FindByName(name);
        if (existing is null)
        {
            var concept = NewConcept(name, summary, ProposalState.Accepted, proposedByModel: false);
            graph.Concepts.Add(concept);
            conceptRepository.Add(concept);
            return concept;
        }

        if (existing.State == ProposalState.Accepted && rejectDuplicates)
        {
            throw new ConflictException($"Concept '{name}' already exists.");
        }

        existing.State = ProposalState.Accepted;
        return existing;
    }

    private void Link(Graph graph, Concept concept, Concept prerequisite, ProposalState state)
    {
        if (concept.Id == prerequisite.Id)
        {
            throw new RequestValidationException("A concept cannot be its own prerequisite.");
        }

        if (graph.WouldLoop(concept.Id, prerequisite.Id))
        {
            throw new ConflictException($"'{prerequisite.Name}' already builds on '{concept.Name}', so this link would make a loop.");
        }

        var edge = graph.FindEdge(concept.Id, prerequisite.Id);
        if (edge is null)
        {
            AddEdge(graph, concept.Id, prerequisite.Id, state);
        }
        else
        {
            edge.State = state;
        }
    }

    private void AddEdge(Graph graph, Guid conceptId, Guid prerequisiteId, ProposalState state)
    {
        var edge = new ConceptPrerequisite
        {
            ConceptId = conceptId,
            PrerequisiteId = prerequisiteId,
            State = state,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };
        graph.Edges.Add(edge);
        conceptRepository.Add(edge);
    }

    private Concept NewConcept(string name, string? summary, ProposalState state, bool proposedByModel) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = currentUser.UserId,
        Name = name,
        NormalizedName = Concept.Normalize(name),
        Summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
        State = state,
        ProposedByModel = proposedByModel,
        CreatedAtUtc = timeProvider.GetUtcNow()
    };

    private static string CleanName(string? rawName)
    {
        var name = string.Join(' ', (rawName ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (name.Length == 0)
        {
            throw new RequestValidationException("Concept name cannot be empty.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new RequestValidationException($"Concept name cannot be longer than {MaxNameLength} characters.");
        }

        return name;
    }

    private static bool IsSamePlace(ConceptPassage passage, Guid sourceId, int pageNumber, string? section) =>
        passage.SourceId == sourceId && passage.PageNumber == pageNumber && passage.Section == section;

    private static string Shorten(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= MaxSnippetLength ? flat : flat[..MaxSnippetLength].TrimEnd() + "…";
    }

    private static ConceptPassageDto ToDto(ConceptPassage passage, string sourceTitle) =>
        new(passage.Id, passage.SourceId, sourceTitle, passage.PageNumber, passage.PageLabel, passage.Section, passage.Snippet);
}
