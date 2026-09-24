using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class BillStreamReadGateway(
    FullWorthDbContext dbContext)
    : IBillStreamReadGateway
{
    public async Task<BillStreamReadRecord?> GetOwnedAsync(
        Guid userId,
        Guid billStreamId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (billStreamId == Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        return await dbContext.BillStreams
            .AsNoTracking()
            .Where(
                stream =>
                    stream.Id ==
                        billStreamId &&
                    stream.UserId ==
                        userId)
            .Select(
                stream =>
                    new BillStreamReadRecord(
                        stream.Id,
                        stream.ProviderName,
                        stream.Category))
            .SingleOrDefaultAsync(
                cancellationToken);
    }
}
