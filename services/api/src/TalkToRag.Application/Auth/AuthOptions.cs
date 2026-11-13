namespace TalkToRag.Application.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When true there are no accounts: every client is handed a session for one built-in user.
    /// Only for an API that listens on the local machine, since anyone who can reach it is that user.
    /// </summary>
    public bool SingleUser { get; set; }
}
