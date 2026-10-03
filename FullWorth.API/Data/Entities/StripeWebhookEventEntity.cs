using FullWorth.API.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Data.Entities;

[EntityTypeConfiguration(
    typeof(StripeWebhookEventEntityConfiguration))]
public sealed class StripeWebhookEventEntity
{
    public string EventId { get; set; } =
        string.Empty;

    public DateTimeOffset ProcessedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;
}
