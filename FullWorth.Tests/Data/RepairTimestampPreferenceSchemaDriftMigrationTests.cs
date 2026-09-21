using FullWorth.API.Data.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FullWorth.Tests.Data;

public sealed class RepairTimestampPreferenceSchemaDriftMigrationTests
{
    [Fact]
    public void Up_RepairsBothColumnsIdempotently()
    {
        var migration =
            new RepairTimestampPreferenceSchemaDrift();

        var sqlOperation =
            Assert.Single(
                migration.UpOperations.OfType<SqlOperation>());

        Assert.Contains(
            "ALTER TABLE \"AspNetUsers\"",
            sqlOperation.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "ADD COLUMN IF NOT EXISTS \"TimestampDisplayMode\" integer NOT NULL DEFAULT 0",
            sqlOperation.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "ALTER TABLE \"SubscriptionAccessKeys\"",
            sqlOperation.Sql,
            StringComparison.Ordinal);

        Assert.Contains(
            "ADD COLUMN IF NOT EXISTS \"Label\" character varying(120)",
            sqlOperation.Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Down_DoesNotDropColumnsOwnedByEarlierMigration()
    {
        var migration =
            new RepairTimestampPreferenceSchemaDrift();

        Assert.Empty(
            migration.DownOperations);
    }
}
