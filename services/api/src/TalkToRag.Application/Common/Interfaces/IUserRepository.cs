using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Common.Interfaces;

/// <summary>Only the user lookups auth needs, so AuthService can be tested without a database.</summary>
public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);
    Task AddAsync(User user, CancellationToken cancellationToken);
}
