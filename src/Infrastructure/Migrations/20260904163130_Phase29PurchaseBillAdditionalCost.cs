using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase29PurchaseBillAdditionalCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultLandedCostClearingAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AdditionalCostRoundingAdjustment",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CapitalisedAdditionalCost",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProductWiseAdditionalCost",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PurchaseBillAdditionalCosts",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseBillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CostTermId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseBillAdditionalCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseBillAdditionalCosts_CostTerms_CostTermId",
                        column: x => x.CostTermId,
                        principalSchema: "configuration",
                        principalTable: "CostTerms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseBillAdditionalCosts_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseBillAdditionalCosts_PurchaseBills_PurchaseBillId",
                        column: x => x.PurchaseBillId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseBillAdditionalCostAllocations",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseBillAdditionalCostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseBillLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseBillAdditionalCostAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseBillAdditionalCostAllocations_PurchaseBillAdditionalCosts_PurchaseBillAdditionalCostId",
                        column: x => x.PurchaseBillAdditionalCostId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseBillAdditionalCosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PurchaseBillAdditionalCostAllocations_PurchaseBillLines_PurchaseBillLineId",
                        column: x => x.PurchaseBillLineId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseBillLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillAdditionalCostAllocations_PurchaseBillAdditionalCostId",
                schema: "purchasing",
                table: "PurchaseBillAdditionalCostAllocations",
                column: "PurchaseBillAdditionalCostId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillAdditionalCostAllocations_PurchaseBillLineId",
                schema: "purchasing",
                table: "PurchaseBillAdditionalCostAllocations",
                column: "PurchaseBillLineId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillAdditionalCosts_CostTermId",
                schema: "purchasing",
                table: "PurchaseBillAdditionalCosts",
                column: "CostTermId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillAdditionalCosts_ProductId",
                schema: "purchasing",
                table: "PurchaseBillAdditionalCosts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillAdditionalCosts_PurchaseBillId",
                schema: "purchasing",
                table: "PurchaseBillAdditionalCosts",
                column: "PurchaseBillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchaseBillAdditionalCostAllocations",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseBillAdditionalCosts",
                schema: "purchasing");

            migrationBuilder.DropColumn(
                name: "DefaultLandedCostClearingAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "AdditionalCostRoundingAdjustment",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "CapitalisedAdditionalCost",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "IsProductWiseAdditionalCost",
                schema: "purchasing",
                table: "PurchaseBills");
        }
    }
}
