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
}
