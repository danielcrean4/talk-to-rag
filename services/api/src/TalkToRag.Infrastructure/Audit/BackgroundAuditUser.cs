namespace TalkToRag.Infrastructure.Audit;

/// <summary>
/// The user that AI calls made outside a web request belong to, such as the owner of a source being ingested.
/// <see cref="AiAuditHandler"/> uses it when the request has no signed-in user.
/// </summary>
public static class BackgroundAuditUser
{
    private static readonly AsyncLocal<Guid?> Current = new();

    public static Guid? UserId => Current.Value;

    /// <summary>Sets the user for the calls made until the returned scope is disposed.</summary>
    public static IDisposable Begin(Guid userId)
    {
        var previous = Current.Value;
        Current.Value = userId;
        return new Scope(previous);
    }

    private sealed class Scope(Guid? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}
