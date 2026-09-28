using FullWorth.API.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Data.Entities;

[EntityTypeConfiguration(
    typeof(PlanningPaycheckAllocationEntityConfiguration))]
public sealed class PlanningPaycheckAllocationEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    /*
     * Opaque cross-domain identifiers only.
     *
     * Planning must resolve ownership/evidence through explicit owner
     * contracts. These values deliberately have no database foreign keys to
     * Plaid, Bills, or Statements.
     */
    public Guid PayrollTransactionId { get; set; }

    public Guid BillStreamId { get; set; }

    public Guid SourceStatementId { get; set; }

    public DateOnly PaycheckPostedDate { get; set; }

    public DateOnly BillPeriodEnd { get; set; }

    public DateOnly BillDueDate { get; set; }

    /*
     * This is the amount FullWorth recommended planning from the paycheck.
     * It is not evidence that money was moved, reserved, protected, or held.
     */
    public decimal PlannedAmount { get; set; }

    public string CurrencyCode { get; set; } =
        string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;
}
