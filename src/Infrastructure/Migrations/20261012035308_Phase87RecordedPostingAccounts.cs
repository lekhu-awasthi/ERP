using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase87RecordedPostingAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryAccountId",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LandedCostClearingAccountId",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PayableAccountId",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TdsPayableAccountId",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VatReceivableAccountId",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DebitAccountId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UnallocatedControlAccountId",
                schema: "payments",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CogsAccountId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryAccountId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivableAccountId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VatPayableAccountId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesAccountId",
                schema: "sales",
                table: "InvoiceLines",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InventoryAccountId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "LandedCostClearingAccountId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "PayableAccountId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "TdsPayableAccountId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "VatReceivableAccountId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "DebitAccountId",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropColumn(
                name: "UnallocatedControlAccountId",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CogsAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InventoryAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReceivableAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "VatPayableAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SalesAccountId",
                schema: "sales",
                table: "InvoiceLines");
        }
    }
}
