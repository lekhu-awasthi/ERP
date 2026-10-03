using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase67CountedPrints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Medium",
                schema: "sales",
                table: "InvoicePrints",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "TillReceipt");

            migrationBuilder.AddColumn<string>(
                name: "Medium",
                schema: "sales",
                table: "CreditNotePrints",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "TillReceipt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Medium",
                schema: "sales",
                table: "InvoicePrints");

            migrationBuilder.DropColumn(
                name: "Medium",
                schema: "sales",
                table: "CreditNotePrints");
        }
    }
}
