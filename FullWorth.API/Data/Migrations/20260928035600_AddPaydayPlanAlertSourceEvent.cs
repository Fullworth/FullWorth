using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260928035600_AddPaydayPlanAlertSourceEvent")]
public sealed class AddPaydayPlanAlertSourceEvent : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "SourceEventId",
            table: "BillAlerts",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_BillAlerts_UserId_AlertType_SourceEventId",
            table: "BillAlerts",
            columns:
            [
                "UserId",
                "AlertType",
                "SourceEventId"
            ],
            unique: true,
            filter: "\"SourceEventId\" IS NOT NULL");
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_BillAlerts_UserId_AlertType_SourceEventId",
            table: "BillAlerts");

        migrationBuilder.DropColumn(
            name: "SourceEventId",
            table: "BillAlerts");
    }
}
