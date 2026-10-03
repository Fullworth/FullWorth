using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20261003040000_AddStripeWebhookEventReceipts")]
public sealed class AddStripeWebhookEventReceipts : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StripeWebhookEvents",
            columns: table => new
            {
                EventId = table.Column<string>(
                    type: "character varying(255)",
                    maxLength: 255,
                    nullable: false),
                ProcessedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_StripeWebhookEvents",
                    candidate => candidate.EventId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StripeWebhookEvents_ProcessedAtUtc",
            table: "StripeWebhookEvents",
            column: "ProcessedAtUtc");
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "StripeWebhookEvents");
    }
}
