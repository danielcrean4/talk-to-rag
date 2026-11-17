using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

/// <summary>Only the user lookups auth needs, so AuthService can be tested without a database.</summary>
public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    /// <summary>Adds the user, or returns false when another user already has that email.</summary>
    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken);
}
