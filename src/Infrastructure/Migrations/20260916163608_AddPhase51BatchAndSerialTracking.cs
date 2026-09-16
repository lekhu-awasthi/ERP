using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase51BatchAndSerialTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "inventory",
                table: "StockMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNo",
                schema: "inventory",
                table: "StockMovements",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "inventory",
                table: "StockLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SerialNo",
                schema: "inventory",
                table: "StockLedgerEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BatchTracking",
                schema: "catalog",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SerialTracking",
                schema: "catalog",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "sales",
                table: "InvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "purchasing",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "sales",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentLineSerials",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ParentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLineSerials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductBatches",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ManufactureDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BatchId",
                schema: "inventory",
                table: "StockMovements",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_BatchId",
                schema: "inventory",
                table: "StockLedgerEntries",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedgerEntries_OrganizationId_ProductId_SerialNo",
                schema: "inventory",
                table: "StockLedgerEntries",
                columns: new[] { "OrganizationId", "ProductId", "SerialNo" },
                unique: true,
                filter: "[SerialNo] IS NOT NULL AND [QuantityRemaining] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillLines_BatchId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_BatchId",
                schema: "sales",
                table: "InvoiceLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNoteLines_BatchId",
                schema: "purchasing",
                table: "DebitNoteLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_BatchId",
                schema: "sales",
                table: "CreditNoteLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLineSerials_OrganizationId_ParentType_ParentLineId",
                schema: "inventory",
                table: "DocumentLineSerials",
                columns: new[] { "OrganizationId", "ParentType", "ParentLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLineSerials_OrganizationId_ParentType_ParentLineId_SerialNo",
                schema: "inventory",
                table: "DocumentLineSerials",
                columns: new[] { "OrganizationId", "ParentType", "ParentLineId", "SerialNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_OrganizationId_ProductId_BatchNo",
                schema: "catalog",
                table: "ProductBatches",
                columns: new[] { "OrganizationId", "ProductId", "BatchNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_ProductId",
                schema: "catalog",
                table: "ProductBatches",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNoteLines_ProductBatches_BatchId",
                schema: "sales",
                table: "CreditNoteLines",
                column: "BatchId",
                principalSchema: "catalog",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNoteLines_ProductBatches_BatchId",
                schema: "purchasing",
                table: "DebitNoteLines",
                column: "BatchId",
                principalSchema: "catalog",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLines_ProductBatches_BatchId",
                schema: "sales",
                table: "InvoiceLines",
                column: "BatchId",
                principalSchema: "catalog",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseBillLines_ProductBatches_BatchId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                column: "BatchId",
                principalSchema: "catalog",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockLedgerEntries_ProductBatches_BatchId",
                schema: "inventory",
                table: "StockLedgerEntries",
                column: "BatchId",
                principalSchema: "catalog",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_ProductBatches_BatchId",
                schema: "inventory",
                table: "StockMovements",
                column: "BatchId",
                principalSchema: "catalog",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNoteLines_ProductBatches_BatchId",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNoteLines_ProductBatches_BatchId",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLines_ProductBatches_BatchId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseBillLines_ProductBatches_BatchId",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockLedgerEntries_ProductBatches_BatchId",
                schema: "inventory",
                table: "StockLedgerEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_ProductBatches_BatchId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "DocumentLineSerials",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "ProductBatches",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_BatchId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockLedgerEntries_BatchId",
                schema: "inventory",
                table: "StockLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_StockLedgerEntries_OrganizationId_ProductId_SerialNo",
                schema: "inventory",
                table: "StockLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseBillLines_BatchId",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_BatchId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_DebitNoteLines_BatchId",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_CreditNoteLines_BatchId",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "SerialNo",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "inventory",
                table: "StockLedgerEntries");

            migrationBuilder.DropColumn(
                name: "SerialNo",
                schema: "inventory",
                table: "StockLedgerEntries");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropColumn(
                name: "BatchTracking",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SerialTracking",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "sales",
                table: "CreditNoteLines");
        }
    }
}
