using FullWorth.API.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Data.Entities;

[EntityTypeConfiguration(
    typeof(PlanningPaycheckPlanRunEntityConfiguration))]
public sealed class PlanningPaycheckPlanRunEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    /*
     * Opaque Plaid-owned transaction identity.
     *
     * Planning deliberately stores no database foreign key to Plaid. The
     * payroll owner contract verifies the transaction before this immutable
     * snapshot is created.
     */
    public Guid PayrollTransactionId { get; set; }

    public DateOnly PaycheckPostedDate { get; set; }

    public decimal PaycheckAmount { get; set; }

    public string CurrencyCode { get; set; } =
        string.Empty;

    public decimal RecommendedSetAside { get; set; }

    public decimal PaycheckRemainingAfterPlan { get; set; }

    public decimal Shortfall { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;
}
