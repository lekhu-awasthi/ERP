using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase95ProductScanIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId_Barcode",
                schema: "catalog",
                table: "Products",
                columns: new[] { "OrganizationId", "Barcode" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId_Sku",
                schema: "catalog",
                table: "Products",
                columns: new[] { "OrganizationId", "Sku" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_OrganizationId_Barcode",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_OrganizationId_Sku",
                schema: "catalog",
                table: "Products");
        }
    }
}
