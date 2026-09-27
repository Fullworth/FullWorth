using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260927040500_AddPlanningPersistenceFoundation")]
public sealed class AddPlanningPersistenceFoundation : Migration
{
    protected override void Up(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlanningBillFundingPreferences",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                UserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                BillStreamId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                PaychecksAheadOverride = table.Column<int>(
                    type: "integer",
                    nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_PlanningBillFundingPreferences",
                    item => item.Id);

                table.UniqueConstraint(
                    "AK_PlanningBillFundingPreferences_Id_UserId",
                    item => new
                    {
                        item.Id,
                        item.UserId
                    });

                table.CheckConstraint(
                    "CK_PlanningBillFundingPreferences_PaychecksAheadOverride",
                    "\"PaychecksAheadOverride\" IS NULL OR (\"PaychecksAheadOverride\" >= 1 AND \"PaychecksAheadOverride\" <= 26)");

                table.ForeignKey(
                    name: "FK_PlanningBillFundingPreferences_AspNetUsers_UserId",
                    column: item => item.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "PlanningPaySchedules",
            columns: table => new
            {
                Id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                UserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                Frequency = table.Column<string>(
                    type: "character varying(50)",
                    maxLength: 50,
                    nullable: false),
                AnchorPayDate = table.Column<DateOnly>(
                    type: "date",
                    nullable: false),
                SecondaryDayOfMonth = table.Column<int>(
                    type: "integer",
                    nullable: true),
                DefaultPaychecksAhead = table.Column<int>(
                    type: "integer",
                    nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_PlanningPaySchedules",
                    item => item.Id);

                table.UniqueConstraint(
                    "AK_PlanningPaySchedules_Id_UserId",
                    item => new
                    {
                        item.Id,
                        item.UserId
                    });

                table.CheckConstraint(
                    "CK_PlanningPaySchedules_Frequency",
                    "\"Frequency\" IN ('Weekly', 'Biweekly', 'SemiMonthly', 'Monthly')");

                table.CheckConstraint(
                    "CK_PlanningPaySchedules_DefaultPaychecksAhead",
                    "\"DefaultPaychecksAhead\" >= 1 AND \"DefaultPaychecksAhead\" <= 26");

                table.CheckConstraint(
                    "CK_PlanningPaySchedules_SecondaryDay",
                    "(\"Frequency\" = 'SemiMonthly' AND \"SecondaryDayOfMonth\" IS NOT NULL AND \"SecondaryDayOfMonth\" >= 1 AND \"SecondaryDayOfMonth\" <= 31 AND \"SecondaryDayOfMonth\" <> EXTRACT(DAY FROM \"AnchorPayDate\")::integer) OR (\"Frequency\" <> 'SemiMonthly' AND \"SecondaryDayOfMonth\" IS NULL)");

                table.ForeignKey(
                    name: "FK_PlanningPaySchedules_AspNetUsers_UserId",
                    column: item => item.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlanningBillFundingPreferences_UserId_BillStreamId",
            table: "PlanningBillFundingPreferences",
            columns: new[]
            {
                "UserId",
                "BillStreamId"
            },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_PlanningPaySchedules_UserId",
            table: "PlanningPaySchedules",
            column: "UserId",
            unique: true);
    }

    protected override void Down(
        MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PlanningBillFundingPreferences");

        migrationBuilder.DropTable(
            name: "PlanningPaySchedules");
    }
}
