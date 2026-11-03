using TalkToRag.Application.Common.Interfaces;

namespace TalkToRag.UnitTests;

internal sealed class TestCurrentUser : ICurrentUser
{
    public Guid UserId { get; set; }
    public bool ThrowOnAccess { get; set; }
    Guid ICurrentUser.UserId => ThrowOnAccess ? throw new InvalidOperationException("Unauthenticated") : UserId;
}
