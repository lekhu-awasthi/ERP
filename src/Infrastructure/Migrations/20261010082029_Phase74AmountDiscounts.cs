using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase74AmountDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "pos",
                table: "PosOrders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPerUnit",
                schema: "pos",
                table: "PosOrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OrderDiscountShare",
                schema: "pos",
                table: "PosOrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "sales",
                table: "Invoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BillDiscountAmount",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPerUnit",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                schema: "sales",
                table: "CreditNotes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "BillDiscountAmount",
                schema: "sales",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPerUnit",
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
                name: "DiscountAmount",
                schema: "pos",
                table: "PosOrders");

            migrationBuilder.DropColumn(
                name: "DiscountPerUnit",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropColumn(
                name: "OrderDiscountShare",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "BillDiscountAmount",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "DiscountPerUnit",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "BillDiscountAmount",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "DiscountPerUnit",
                schema: "sales",
                table: "CreditNoteLines");
        }
    }
}
