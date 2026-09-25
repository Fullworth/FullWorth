using System.Security.Cryptography;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class AccountBankDeletionGateway(
    FullWorthDbContext dbContext,
    PlaidConnectionDisconnectService disconnectService)
    : IAccountBankDeletionGateway
{
    public async Task RevokeExternalAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        var connectionIds =
            await dbContext.BankConnections
                .AsNoTracking()
                .Where(
                    connection =>
                        connection.UserId ==
                            userId &&
                        connection.Status !=
                            BankConnectionStatus.Disconnected)
                .Select(
                    connection =>
                        connection.Id)
                .ToListAsync(
                    cancellationToken);

        foreach (var connectionId in
                 connectionIds)
        {
            try
            {
                await disconnectService.DisconnectAsync(
                    userId,
                    connectionId,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is
                    PlaidApiException or
                    HttpRequestException or
                    CryptographicException or
                    InvalidOperationException)
            {
                throw new AccountBankRevocationException(
                    exception.GetType().Name,
                    exception);
            }
        }
    }

    public async Task ApplyOwnedDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        var transactions =
            await dbContext.BankTransactions
                .Where(
                    transaction =>
                        transaction.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.BankTransactions.RemoveRange(
            transactions);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        var accounts =
            await dbContext.BankAccounts
                .Where(
                    account =>
                        account.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        var linkSessions =
            await dbContext.PlaidLinkSessions
                .Where(
                    session =>
                        session.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.BankAccounts.RemoveRange(
            accounts);

        dbContext.PlaidLinkSessions.RemoveRange(
            linkSessions);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        var connections =
            await dbContext.BankConnections
                .Where(
                    connection =>
                        connection.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.BankConnections.RemoveRange(
            connections);

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
