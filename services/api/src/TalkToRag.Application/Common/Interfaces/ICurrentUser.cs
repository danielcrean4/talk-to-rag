namespace TalkToRag.Application.Common.Interfaces;

/// <summary>Who is making the current request. Every user-owned query filters on <see cref="UserId"/>.</summary>
public interface ICurrentUser
{
    Guid UserId { get; }
}
