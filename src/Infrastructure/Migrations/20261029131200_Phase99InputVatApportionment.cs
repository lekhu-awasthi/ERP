using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase99InputVatApportionment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 99 -- hand-edited: the scaffold defaulted these enum-by-name columns to '', which Enum.Parse refuses on
            // read (phase-73). Every existing line is backfilled to what it posted (Taxable use, Full credit) and every
            // product to Full, so no approved bill, expense or return changes.
            migrationBuilder.AddColumn<string>(
                name: "CreditCategory",
                schema: "purchasing",
                table: "PurchaseBillLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Full");

            migrationBuilder.AddColumn<string>(
                name: "CreditUse",
                schema: "purchasing",
                table: "PurchaseBillLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Taxable");

            migrationBuilder.AddColumn<string>(
                name: "VatCreditCategory",
                schema: "catalog",
                table: "Products",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Full");

            migrationBuilder.AddColumn<string>(
                name: "CreditCategory",
                schema: "purchasing",
                table: "ExpenseLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Full");

            migrationBuilder.AddColumn<string>(
                name: "CreditUse",
                schema: "purchasing",
                table: "ExpenseLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Taxable");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreditCategory",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropColumn(
                name: "CreditUse",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropColumn(
                name: "VatCreditCategory",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CreditCategory",
                schema: "purchasing",
                table: "ExpenseLines");

            migrationBuilder.DropColumn(
                name: "CreditUse",
                schema: "purchasing",
                table: "ExpenseLines");
        }
    }
}
