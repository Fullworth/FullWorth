using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260924150500_DecoupleBillAlertChangeForeignKey")]
public sealed class DecoupleBillAlertChangeForeignKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_BillAlerts_BillChanges_BillChangeId_UserId",
            table: "BillAlerts");

        migrationBuilder.DropIndex(
            name: "IX_BillAlerts_BillChangeId_UserId",
            table: "BillAlerts");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_BillAlerts_BillChangeId_UserId",
            table: "BillAlerts",
            columns: new[]
            {
                "BillChangeId",
                "UserId"
            });

        migrationBuilder.AddForeignKey(
            name: "FK_BillAlerts_BillChanges_BillChangeId_UserId",
            table: "BillAlerts",
            columns: new[]
            {
                "BillChangeId",
                "UserId"
            },
            principalTable: "BillChanges",
            principalColumns: new[]
            {
                "Id",
                "UserId"
            },
            onDelete: ReferentialAction.Restrict);
    }
}
