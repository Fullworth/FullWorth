using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Identity;

public sealed class IdentityUserExistenceGateway(
    FullWorthDbContext dbContext)
    : IIdentityUserExistenceGateway
{
    public Task<bool> ExistsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        return dbContext.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Id == userId,
                cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> GetExistingUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            userIds);

        if (userIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids =
            userIds
                .Where(userId => userId != Guid.Empty)
                .Distinct()
                .ToArray();

        if (ids.Length == 0)
        {
            return new HashSet<Guid>();
        }

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .Select(user => user.Id)
            .ToHashSetAsync(cancellationToken);
    }
}
