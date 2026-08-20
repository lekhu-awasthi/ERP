using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase17JournalVoucherAllocationSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAllocations_Payments_PaymentId",
                schema: "payments",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_PaymentId",
                schema: "payments",
                table: "PaymentAllocations");

            migrationBuilder.RenameColumn(
                name: "PaymentId",
                schema: "payments",
                table: "PaymentAllocations",
                newName: "SourceId");

            // Every pre-existing PaymentAllocation row predates decision #2 (SourceId was a hard
            // PaymentId FK) so was necessarily Payment-sourced -- backfill "Payment" (the enum's
            // string-converted name), not the scaffolded "" (would leave existing rows unparseable,
            // same class of bug as the Kind column's defaultValue fix in Phase17AccountingBreadth).
            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                schema: "payments",
                table: "PaymentAllocations",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Payment");

            migrationBuilder.AddColumn<Guid>(
                name: "ContactId",
                schema: "accounting",
                table: "JournalVoucherLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_SourceType_SourceId",
                schema: "payments",
                table: "PaymentAllocations",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalVoucherLines_ContactId",
                schema: "accounting",
                table: "JournalVoucherLines",
                column: "ContactId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalVoucherLines_Contacts_ContactId",
                schema: "accounting",
                table: "JournalVoucherLines",
                column: "ContactId",
                principalSchema: "contacts",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalVoucherLines_Contacts_ContactId",
                schema: "accounting",
                table: "JournalVoucherLines");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAllocations_SourceType_SourceId",
                schema: "payments",
                table: "PaymentAllocations");

            migrationBuilder.DropIndex(
                name: "IX_JournalVoucherLines_ContactId",
                schema: "accounting",
                table: "JournalVoucherLines");

            migrationBuilder.DropColumn(
                name: "SourceType",
                schema: "payments",
                table: "PaymentAllocations");

            migrationBuilder.DropColumn(
                name: "ContactId",
                schema: "accounting",
                table: "JournalVoucherLines");

            migrationBuilder.RenameColumn(
                name: "SourceId",
                schema: "payments",
                table: "PaymentAllocations",
                newName: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAllocations_PaymentId",
                schema: "payments",
                table: "PaymentAllocations",
                column: "PaymentId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAllocations_Payments_PaymentId",
                schema: "payments",
                table: "PaymentAllocations",
                column: "PaymentId",
                principalSchema: "payments",
                principalTable: "Payments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
