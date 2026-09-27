using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260927231500_AddPlanningPaycheckAllocationLedger")]
public sealed class AddPlanningPaycheckAllocationLedger : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlanningPaycheckAllocations",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                UserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                PayrollTransactionId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                BillStreamId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                SourceStatementId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                PaycheckPostedDate = table.Column<DateOnly>(
                    type: "date",
                    nullable: false),
                BillDueDate = table.Column<DateOnly>(
                    type: "date",
                    nullable: false),
                PlannedAmount = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                CurrencyCode = table.Column<string>(
                    type: "character varying(3)",
                    maxLength: 3,
                    nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_PlanningPaycheckAllocations",
                    item => item.Id);

                table.UniqueConstraint(
                    "AK_PlanningPaycheckAllocations_Id_UserId",
                    item => new
                    {
                        item.Id,
                        item.UserId
                    });

                table.CheckConstraint(
                    "CK_PlanningPaycheckAllocations_PlannedAmount",
                    "\"PlannedAmount\" > 0");

                table.CheckConstraint(
                    "CK_PlanningPaycheckAllocations_CurrencyCode",
                    "char_length(\"CurrencyCode\") = 3");

                table.ForeignKey(
                    name: "FK_PlanningPaycheckAllocations_AspNetUsers_UserId",
                    column: item => item.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlanningPaycheckAllocations_UserId_BillStreamId_BillDueDate",
            table: "PlanningPaycheckAllocations",
            columns: new[]
            {
                "UserId",
                "BillStreamId",
                "BillDueDate"
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlanningPaycheckAllocations_UserId_PaycheckPostedDate",
            table: "PlanningPaycheckAllocations",
            columns: new[]
            {
                "UserId",
                "PaycheckPostedDate"
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlanningPaycheckAllocations_UserId_PayrollTransactionId_BillStreamId_BillDueDate",
            table: "PlanningPaycheckAllocations",
            columns: new[]
            {
                "UserId",
                "PayrollTransactionId",
                "BillStreamId",
                "BillDueDate"
            },
            unique: true);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PlanningPaycheckAllocations");
    }
}
