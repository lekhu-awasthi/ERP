using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase27bDocumentTerms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Terms",
                schema: "sales",
                table: "SalesOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms",
                schema: "sales",
                table: "Quotations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Terms",
                schema: "sales",
                table: "CreditNotes",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Terms",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "Terms",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "Terms",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "Terms",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Terms",
                schema: "sales",
                table: "CreditNotes");
        }
    }
}
