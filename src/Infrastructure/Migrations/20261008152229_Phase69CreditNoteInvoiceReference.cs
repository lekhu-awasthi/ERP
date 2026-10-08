using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase69CreditNoteInvoiceReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AgainstInvoiceDate",
                schema: "sales",
                table: "CreditNotes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AgainstInvoiceId",
                schema: "sales",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgainstInvoiceNumber",
                schema: "sales",
                table: "CreditNotes",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_AgainstInvoiceId",
                schema: "sales",
                table: "CreditNotes",
                column: "AgainstInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_Invoices_AgainstInvoiceId",
                schema: "sales",
                table: "CreditNotes",
                column: "AgainstInvoiceId",
                principalSchema: "sales",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_Invoices_AgainstInvoiceId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_AgainstInvoiceId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "AgainstInvoiceDate",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "AgainstInvoiceId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "AgainstInvoiceNumber",
                schema: "sales",
                table: "CreditNotes");
        }
    }
}
