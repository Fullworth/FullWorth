using FullWorth.Core.Models;

namespace FullWorth.API.Services.Contracts;

public sealed record BillStreamReadRecord(
    Guid BillStreamId,
    string ProviderName,
    BillCategory Category);

public interface IBillStreamReadGateway
{
    Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<BillStreamReadRecord?> GetOwnedAsync(
        Guid userId,
        Guid billStreamId,
        CancellationToken cancellationToken = default);
}
