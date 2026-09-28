using FullWorth.API.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Data.Entities;

[EntityTypeConfiguration(
    typeof(PlanningBillFundingPreferenceEntityConfiguration))]
public sealed class PlanningBillFundingPreferenceEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    /*
     * Opaque cross-domain identifier only.
     *
     * Planning deliberately does not hold a database foreign key or
     * navigation to Bills. Ownership/existence must be verified through the
     * explicit Bills owner contract before this value is written or used.
     */
    public Guid BillStreamId { get; set; }

    public int? PaychecksAheadOverride { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;
}
