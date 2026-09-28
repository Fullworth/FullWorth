using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260928032500_AddPlanningPaycheckPlanRuns")]
public sealed class AddPlanningPaycheckPlanRuns : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlanningPaycheckPlanRuns",
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
                PaycheckPostedDate = table.Column<DateOnly>(
                    type: "date",
                    nullable: false),
                PaycheckAmount = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                CurrencyCode = table.Column<string>(
                    type: "character varying(3)",
                    maxLength: 3,
                    nullable: false),
                RecommendedSetAside = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                PaycheckRemainingAfterPlan = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                Shortfall = table.Column<decimal>(
                    type: "numeric(18,2)",
                    precision: 18,
                    scale: 2,
                    nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_PlanningPaycheckPlanRuns",
                    item => item.Id);

                table.UniqueConstraint(
                    "AK_PlanningPaycheckPlanRuns_Id_UserId",
                    item => new
                    {
                        item.Id,
                        item.UserId
                    });

                table.CheckConstraint(
                    "CK_PlanningPaycheckPlanRuns_Amounts",
                    "\"PaycheckAmount\" >= 0 AND \"RecommendedSetAside\" >= 0 AND \"PaycheckRemainingAfterPlan\" >= 0 AND \"Shortfall\" >= 0");

                table.CheckConstraint(
                    "CK_PlanningPaycheckPlanRuns_CurrencyCode",
                    "char_length(\"CurrencyCode\") = 3");

                table.ForeignKey(
                    name: "FK_PlanningPaycheckPlanRuns_AspNetUsers_UserId",
                    column: item => item.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlanningPaycheckPlanRuns_UserId_PayrollTransactionId",
            table: "PlanningPaycheckPlanRuns",
            columns: new[]
            {
                "UserId",
                "PayrollTransactionId"
            },
            unique: true);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PlanningPaycheckPlanRuns");
    }
}
