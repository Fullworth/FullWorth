using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class AccountBillDeletionGateway(
    FullWorthDbContext dbContext)
    : IAccountBillDeletionGateway
{
    public async Task ApplyDependentDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        var transactionLinks =
            await dbContext.BillTransactionLinks
                .Where(
                    link =>
                        link.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        var alerts =
            await dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.BillTransactionLinks.RemoveRange(
            transactionLinks);

        dbContext.BillAlerts.RemoveRange(
            alerts);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    public async Task ApplyRootDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        var streams =
            await dbContext.BillStreams
                .Where(
                    stream =>
                        stream.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.BillStreams.RemoveRange(
            streams);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static void ValidateUserId(
        Guid userId)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }
}
