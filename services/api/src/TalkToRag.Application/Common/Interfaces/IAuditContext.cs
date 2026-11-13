namespace TalkToRag.Application.Common.Interfaces;

/// <summary>Who and which web request an audited action belongs to. Both are null outside a web request.</summary>
public interface IAuditContext
{
    Guid? UserId { get; }
    string? RequestTraceId { get; }
}
