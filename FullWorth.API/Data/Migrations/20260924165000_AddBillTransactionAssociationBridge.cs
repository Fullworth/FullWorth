using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FullWorth.API.Data.Migrations;

[DbContext(typeof(FullWorthDbContext))]
[Migration("20260924165000_AddBillTransactionAssociationBridge")]
public sealed class AddBillTransactionAssociationBridge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "BillTransactionAssociations",
            columns: table => new
            {
                UserId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                BankTransactionId = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),
                BillStreamId = table.Column<Guid>(
                    type: "uuid",
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
                    "PK_BillTransactionAssociations",
                    item => new
                    {
                        item.UserId,
                        item.BankTransactionId
                    });

                table.ForeignKey(
                    name: "FK_BillTransactionAssociations_AspNetUsers_UserId",
                    column: item => item.UserId,
                    principalTable: "AspNetUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);

                table.ForeignKey(
                    name: "FK_BillTransactionAssociations_BillStreams_BillStreamId_UserId",
                    columns: item => new
                    {
                        item.BillStreamId,
                        item.UserId
                    },
                    principalTable: "BillStreams",
                    principalColumns: new[]
                    {
                        "Id",
                        "UserId"
                    },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_BillTransactionAssociations_BillStreamId_UserId",
            table: "BillTransactionAssociations",
            columns: new[]
            {
                "BillStreamId",
                "UserId"
            });

        migrationBuilder.Sql(
            """
            INSERT INTO "BillTransactionAssociations"
                ("UserId", "BankTransactionId", "BillStreamId", "CreatedAtUtc", "UpdatedAtUtc")
            SELECT
                "UserId",
                "Id",
                "BillStreamId",
                "UpdatedAtUtc",
                "UpdatedAtUtc"
            FROM "BankTransactions"
            WHERE "BillStreamId" IS NOT NULL
            ON CONFLICT ("UserId", "BankTransactionId") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "BillTransactionAssociations");
    }
}
