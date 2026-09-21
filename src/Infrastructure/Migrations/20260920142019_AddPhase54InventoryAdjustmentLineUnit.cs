using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase54InventoryAdjustmentLineUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "inventory",
                table: "InventoryAdjustmentLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "inventory",
                table: "InventoryAdjustmentLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustmentLines_UnitId",
                schema: "inventory",
                table: "InventoryAdjustmentLines",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustmentLines_UnitsOfMeasurement_UnitId",
                schema: "inventory",
                table: "InventoryAdjustmentLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAdjustmentLines_UnitsOfMeasurement_UnitId",
                schema: "inventory",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustmentLines_UnitId",
                schema: "inventory",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "inventory",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "inventory",
                table: "InventoryAdjustmentLines");
        }
    }
}
