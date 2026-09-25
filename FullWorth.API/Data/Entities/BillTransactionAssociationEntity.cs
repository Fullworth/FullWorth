namespace FullWorth.API.Data.Entities;

public sealed class BillTransactionAssociationEntity
{
    public Guid UserId { get; set; }

    public Guid BankTransactionId { get; set; }

    public Guid BillStreamId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } = null!;

    public BillStreamEntity BillStream { get; set; } = null!;
}
