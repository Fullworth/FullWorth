using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FullWorth.API.Data.Migrations;

public partial class RemoveLegacyBankTransactionBillLink : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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

        migrationBuilder.CreateIndex(
            name: "IX_BankTransactions_BillStreamId",
            table: "BankTransactions",
            column: "BillStreamId");

        migrationBuilder.CreateIndex(
            name: "IX_BankTransactions_BillStreamId_UserId",
            table: "BankTransactions",
            columns: new[] { "BillStreamId", "UserId" });

        migrationBuilder.AddForeignKey(
            name: "FK_BankTransactions_BillStreams_BillStreamId_UserId",
            table: "BankTransactions",
            columns: new[] { "BillStreamId", "UserId" },
            principalTable: "BillStreams",
            principalColumns: new[] { "Id", "UserId" },
            onDelete: ReferentialAction.Restrict);
    }
}
