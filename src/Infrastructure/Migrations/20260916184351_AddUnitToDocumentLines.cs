using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitToDocumentLines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "inventory",
                table: "WarehouseTransferLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "inventory",
                table: "WarehouseTransferLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "sales",
                table: "SalesOrderLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "sales",
                table: "SalesOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "sales",
                table: "QuotationLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "sales",
                table: "QuotationLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "purchasing",
                table: "PurchaseBillLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "sales",
                table: "InvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "purchasing",
                table: "DebitNoteLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "purchasing",
                table: "DebitNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                schema: "sales",
                table: "CreditNoteLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<Guid>(
                name: "UnitId",
                schema: "sales",
                table: "CreditNoteLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseTransferLines_UnitId",
                schema: "inventory",
                table: "WarehouseTransferLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_UnitId",
                schema: "sales",
                table: "SalesOrderLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_QuotationLines_UnitId",
                schema: "sales",
                table: "QuotationLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_UnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillLines_UnitId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_UnitId",
                schema: "sales",
                table: "InvoiceLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNoteLines_UnitId",
                schema: "purchasing",
                table: "DebitNoteLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNoteLines_UnitId",
                schema: "sales",
                table: "CreditNoteLines",
                column: "UnitId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNoteLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "CreditNoteLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNoteLines_UnitsOfMeasurement_UnitId",
                schema: "purchasing",
                table: "DebitNoteLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "InvoiceLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseBillLines_UnitsOfMeasurement_UnitId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrderLines_UnitsOfMeasurement_UnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QuotationLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "QuotationLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrderLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "SalesOrderLines",
                column: "UnitId",
                principalSchema: "catalog",
                principalTable: "UnitsOfMeasurement",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WarehouseTransferLines_UnitsOfMeasurement_UnitId",
                schema: "inventory",
                table: "WarehouseTransferLines",
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
                name: "FK_CreditNoteLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNoteLines_UnitsOfMeasurement_UnitId",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseBillLines_UnitsOfMeasurement_UnitId",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrderLines_UnitsOfMeasurement_UnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_QuotationLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "QuotationLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrderLines_UnitsOfMeasurement_UnitId",
                schema: "sales",
                table: "SalesOrderLines");

            migrationBuilder.DropForeignKey(
                name: "FK_WarehouseTransferLines_UnitsOfMeasurement_UnitId",
                schema: "inventory",
                table: "WarehouseTransferLines");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseTransferLines_UnitId",
                schema: "inventory",
                table: "WarehouseTransferLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrderLines_UnitId",
                schema: "sales",
                table: "SalesOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_QuotationLines_UnitId",
                schema: "sales",
                table: "QuotationLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrderLines_UnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseBillLines_UnitId",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_UnitId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropIndex(
                name: "IX_DebitNoteLines_UnitId",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropIndex(
                name: "IX_CreditNoteLines_UnitId",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "inventory",
                table: "WarehouseTransferLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "inventory",
                table: "WarehouseTransferLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "sales",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "sales",
                table: "SalesOrderLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "sales",
                table: "QuotationLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "sales",
                table: "QuotationLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "purchasing",
                table: "PurchaseOrderLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "purchasing",
                table: "PurchaseBillLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                schema: "sales",
                table: "CreditNoteLines");
        }
    }
}
