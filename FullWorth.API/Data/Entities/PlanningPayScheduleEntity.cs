using FullWorth.API.Data.Configurations;
using FullWorth.Core.Models.Planning;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Data.Entities;

[EntityTypeConfiguration(
    typeof(PlanningPayScheduleEntityConfiguration))]
public sealed class PlanningPayScheduleEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public Guid UserId { get; set; }

    public PayScheduleFrequency Frequency { get; set; }

    public DateOnly AnchorPayDate { get; set; }

    public int? SecondaryDayOfMonth { get; set; }

    public int DefaultPaychecksAhead { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser User { get; set; } =
        null!;
}
