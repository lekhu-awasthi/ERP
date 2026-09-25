using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase58CoveringStockIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_PhysicalStockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "PhysicalStockMovements");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "OrganizationId", "ProductId", "WarehouseId", "TransactionDate" })
                .Annotation("SqlServer:Include", new[] { "Direction", "Quantity", "SourceDocumentType" });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalStockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "PhysicalStockMovements",
                columns: new[] { "OrganizationId", "ProductId", "WarehouseId", "TransactionDate" })
                .Annotation("SqlServer:Include", new[] { "Direction", "Quantity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_PhysicalStockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "PhysicalStockMovements");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "OrganizationId", "ProductId", "WarehouseId", "TransactionDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalStockMovements_OrganizationId_ProductId_WarehouseId_TransactionDate",
                schema: "inventory",
                table: "PhysicalStockMovements",
                columns: new[] { "OrganizationId", "ProductId", "WarehouseId", "TransactionDate" });
        }
    }
}
