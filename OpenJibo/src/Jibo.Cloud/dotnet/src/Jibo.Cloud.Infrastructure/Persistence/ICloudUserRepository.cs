using Jibo.Cloud.Domain.Models;

namespace Jibo.Cloud.Infrastructure.Persistence;

public interface ICloudUserRepository
{
    Task<UserRecord?> CreateAsync(string email, string password, string? firstName, string? lastName,
        CancellationToken cancellationToken = default);
    Task<UserRecord?> AuthenticateAsync(string email, string password,
        CancellationToken cancellationToken = default);
    Task<UserRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<UserRecord> UpdateProfileAsync(string userId, string? firstName, string? lastName,
        string? gender, long? birthday, CancellationToken cancellationToken = default);
}
