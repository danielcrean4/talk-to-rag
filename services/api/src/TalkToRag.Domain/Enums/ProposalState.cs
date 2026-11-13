namespace TalkToRag.Domain.Enums;

/// <summary>Whether the user has taken up something a model suggested. Rejected rows are kept so they are not suggested again.</summary>
public enum ProposalState
{
    Proposed,
    Accepted,
    Rejected
}
