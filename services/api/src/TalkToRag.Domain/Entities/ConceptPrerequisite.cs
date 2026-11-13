using TalkToRag.Domain.Enums;

namespace TalkToRag.Domain.Entities;

/// <summary>"Understand <see cref="PrerequisiteId"/> before <see cref="ConceptId"/>."</summary>
public class ConceptPrerequisite
{
    public Guid ConceptId { get; set; }
    public Guid PrerequisiteId { get; set; }
    public ProposalState State { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
