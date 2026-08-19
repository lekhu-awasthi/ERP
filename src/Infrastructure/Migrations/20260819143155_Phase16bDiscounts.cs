using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase16bDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "SalesOrders",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "SalesOrderLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "Quotations",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "QuotationLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseBillLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "Invoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "purchasing",
                table: "DebitNotes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "purchasing",
                table: "DebitNoteLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "CreditNotes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "sales",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "QuotationLines");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "sales",
                table: "CreditNoteLines");
        }
    }
}
