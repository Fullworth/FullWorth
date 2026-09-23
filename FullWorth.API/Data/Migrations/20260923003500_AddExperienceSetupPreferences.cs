using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260923003500_AddExperienceSetupPreferences")]
public sealed class AddExperienceSetupPreferences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "ExperienceFocus",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 9);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ExperienceSetupCompletedAtUtc",
            table: "AspNetUsers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "HighContrastEnabled",
            table: "AspNetUsers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "PreferredUiLanguage",
            table: "AspNetUsers",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "en-US");

        migrationBuilder.AddColumn<bool>(
            name: "ReduceMotionEnabled",
            table: "AspNetUsers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "TextSizePreference",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "ThemePreference",
            table: "AspNetUsers",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ExperienceFocus",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "ExperienceSetupCompletedAtUtc",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "HighContrastEnabled",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "PreferredUiLanguage",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "ReduceMotionEnabled",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "TextSizePreference",
            table: "AspNetUsers");

        migrationBuilder.DropColumn(
            name: "ThemePreference",
            table: "AspNetUsers");
    }
}
