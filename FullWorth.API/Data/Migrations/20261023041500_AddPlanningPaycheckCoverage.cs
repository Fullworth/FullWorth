using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20261023041500_AddPlanningPaycheckCoverage")]
public sealed class AddPlanningPaycheckCoverage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No default/backfill: old plans have unknown generation-time coverage.
        migrationBuilder.AddColumn<int>(
            name: "CoverageEligibleBillCount",
            table: "PlanningPaycheckPlanRuns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CoverageMissingStatementFactCount",
            table: "PlanningPaycheckPlanRuns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CoverageMissingDueDateCount",
            table: "PlanningPaycheckPlanRuns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CoverageCurrencyMismatchCount",
            table: "PlanningPaycheckPlanRuns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CoverageInvalidStatementAmountCount",
            table: "PlanningPaycheckPlanRuns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "CoveragePriorCurrencyConflictCount",
            table: "PlanningPaycheckPlanRuns",
            type: "integer",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_PlanningPaycheckPlanRuns_Coverage",
            table: "PlanningPaycheckPlanRuns",
            sql: "(\"CoverageEligibleBillCount\" IS NULL AND \"CoverageMissingStatementFactCount\" IS NULL AND \"CoverageMissingDueDateCount\" IS NULL AND \"CoverageCurrencyMismatchCount\" IS NULL AND \"CoverageInvalidStatementAmountCount\" IS NULL AND \"CoveragePriorCurrencyConflictCount\" IS NULL) OR (\"CoverageEligibleBillCount\" IS NOT NULL AND \"CoverageEligibleBillCount\" >= 0 AND \"CoverageEligibleBillCount\" <= 500 AND \"CoverageMissingStatementFactCount\" IS NOT NULL AND \"CoverageMissingStatementFactCount\" >= 0 AND \"CoverageMissingStatementFactCount\" <= 500 AND \"CoverageMissingDueDateCount\" IS NOT NULL AND \"CoverageMissingDueDateCount\" >= 0 AND \"CoverageMissingDueDateCount\" <= 500 AND \"CoverageCurrencyMismatchCount\" IS NOT NULL AND \"CoverageCurrencyMismatchCount\" >= 0 AND \"CoverageCurrencyMismatchCount\" <= 500 AND \"CoverageInvalidStatementAmountCount\" IS NOT NULL AND \"CoverageInvalidStatementAmountCount\" >= 0 AND \"CoverageInvalidStatementAmountCount\" <= 500 AND \"CoveragePriorCurrencyConflictCount\" IS NOT NULL AND \"CoveragePriorCurrencyConflictCount\" >= 0 AND \"CoveragePriorCurrencyConflictCount\" <= 500 AND CAST(\"CoverageEligibleBillCount\" AS bigint) + CAST(\"CoverageMissingStatementFactCount\" AS bigint) + CAST(\"CoverageMissingDueDateCount\" AS bigint) + CAST(\"CoverageCurrencyMismatchCount\" AS bigint) + CAST(\"CoverageInvalidStatementAmountCount\" AS bigint) + CAST(\"CoveragePriorCurrencyConflictCount\" AS bigint) <= 500)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_PlanningPaycheckPlanRuns_Coverage",
            table: "PlanningPaycheckPlanRuns");
        migrationBuilder.DropColumn(
            name: "CoverageEligibleBillCount",
            table: "PlanningPaycheckPlanRuns");

        migrationBuilder.DropColumn(
            name: "CoverageMissingStatementFactCount",
            table: "PlanningPaycheckPlanRuns");

        migrationBuilder.DropColumn(
            name: "CoverageMissingDueDateCount",
            table: "PlanningPaycheckPlanRuns");

        migrationBuilder.DropColumn(
            name: "CoverageCurrencyMismatchCount",
            table: "PlanningPaycheckPlanRuns");

        migrationBuilder.DropColumn(
            name: "CoverageInvalidStatementAmountCount",
            table: "PlanningPaycheckPlanRuns");

        migrationBuilder.DropColumn(
            name: "CoveragePriorCurrencyConflictCount",
            table: "PlanningPaycheckPlanRuns");
    }
}
