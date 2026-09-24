using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260924163500_MoveBankTransactionLinksToBills")]
public sealed class MoveBankTransactionLinksToBills : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BillTransactionLinks",
            columns: table => new
            {
                BankTransactionId =
                    table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                UserId =
                    table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                BillStreamId =
                    table.Column<Guid>(
                        type: "uuid",
                        nullable: false),

                CreatedAtUtc =
                    table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false),

                UpdatedAtUtc =
                    table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_BillTransactionLinks",
                    item =>
                        item.BankTransactionId);

                table.ForeignKey(
                    name: "FK_BillTransactionLinks_AspNetUsers_UserId",
                    column:
                        item =>
                            item.UserId,
                    principalTable:
                        "AspNetUsers",
                    principalColumn:
                        "Id",
                    onDelete:
                        ReferentialAction.Cascade);

                table.ForeignKey(
                    name: "FK_BillTransactionLinks_BillStreams_BillStreamId_UserId",
                    columns:
                        item =>
                            new
                            {
                                item.BillStreamId,
                                item.UserId
                            },
                    principalTable:
                        "BillStreams",
                    principalColumns:
                        new[]
                        {
                            "Id",
                            "UserId"
                        },
                    onDelete:
                        ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BillTransactionLinks_BillStreamId",
            table: "BillTransactionLinks",
            column: "BillStreamId");

        migrationBuilder.CreateIndex(
            name: "IX_BillTransactionLinks_UserId",
            table: "BillTransactionLinks",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_BillTransactionLinks_UserId_BillStreamId",
            table: "BillTransactionLinks",
            columns:
                new[]
                {
                    "UserId",
                    "BillStreamId"
                });

        migrationBuilder.Sql(
            """
            INSERT INTO "BillTransactionLinks"
                (
                    "BankTransactionId",
                    "UserId",
                    "BillStreamId",
                    "CreatedAtUtc",
                    "UpdatedAtUtc"
                )
            SELECT
                "Id",
                "UserId",
                "BillStreamId",
                "CreatedAtUtc",
                "UpdatedAtUtc"
            FROM "BankTransactions"
            WHERE "BillStreamId" IS NOT NULL;
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_BankTransactions_BillStreams_BillStreamId_UserId",
            table: "BankTransactions");

        migrationBuilder.DropIndex(
            name: "IX_BankTransactions_BillStreamId",
            table: "BankTransactions");

        migrationBuilder.DropIndex(
            name: "IX_BankTransactions_BillStreamId_UserId",
            table: "BankTransactions");

        migrationBuilder.DropColumn(
            name: "BillStreamId",
            table: "BankTransactions");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BillStreamId",
            table: "BankTransactions",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "BankTransactions" AS transaction
            SET "BillStreamId" = link."BillStreamId"
            FROM "BillTransactionLinks" AS link
            WHERE transaction."Id" = link."BankTransactionId"
              AND transaction."UserId" = link."UserId";
            """);

        migrationBuilder.CreateIndex(
            name: "IX_BankTransactions_BillStreamId",
            table: "BankTransactions",
            column: "BillStreamId");

        migrationBuilder.CreateIndex(
            name: "IX_BankTransactions_BillStreamId_UserId",
            table: "BankTransactions",
            columns:
                new[]
                {
                    "BillStreamId",
                    "UserId"
                });

        migrationBuilder.AddForeignKey(
            name: "FK_BankTransactions_BillStreams_BillStreamId_UserId",
            table: "BankTransactions",
            columns:
                new[]
                {
                    "BillStreamId",
                    "UserId"
                },
            principalTable: "BillStreams",
            principalColumns:
                new[]
                {
                    "Id",
                    "UserId"
                },
            onDelete:
                ReferentialAction.Restrict);

        migrationBuilder.DropTable(
            name: "BillTransactionLinks");
    }
}
