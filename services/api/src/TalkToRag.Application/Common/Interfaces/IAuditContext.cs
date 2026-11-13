namespace TalkToRag.Application.Common.Interfaces;

/// <summary>Who and which web request an audited action belongs to. Both are null for background work.</summary>
public interface IAuditContext
{
    Guid? UserId { get; }
    string? RequestTraceId { get; }
}
