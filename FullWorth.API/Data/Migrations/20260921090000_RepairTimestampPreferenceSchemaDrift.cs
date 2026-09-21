using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260921090000_RepairTimestampPreferenceSchemaDrift")]
public sealed class RepairTimestampPreferenceSchemaDrift : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE "AspNetUsers"
                ADD COLUMN IF NOT EXISTS "TimestampDisplayMode" integer NOT NULL DEFAULT 0;

            ALTER TABLE "SubscriptionAccessKeys"
                ADD COLUMN IF NOT EXISTS "Label" character varying(120);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally non-destructive.
        //
        // This migration repairs schema drift for columns owned by the earlier
        // AddKeyLabelsAndTimestampPreference migration. Rolling this repair back
        // must not remove columns that may have been created correctly by that
        // original migration.
    }
}
