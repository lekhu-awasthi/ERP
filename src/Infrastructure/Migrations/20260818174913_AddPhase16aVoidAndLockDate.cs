using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase16aVoidAndLockDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "inventory",
                table: "WarehouseTransfers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "inventory",
                table: "WarehouseTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "sales",
                table: "SalesOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "sales",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "sales",
                table: "Quotations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "sales",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "payments",
                table: "Payments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "payments",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "accounting",
                table: "JournalVouchers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "accounting",
                table: "JournalVouchers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "sales",
                table: "Invoices",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "inventory",
                table: "InventoryAdjustments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "inventory",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConsumedUnitCost",
                schema: "inventory",
                table: "InventoryAdjustmentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "purchasing",
                table: "Expenses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "purchasing",
                table: "DebitNotes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ConsumedUnitCost",
                schema: "purchasing",
                table: "DebitNoteLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "sales",
                table: "CreditNotes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "sales",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                schema: "accounting",
                table: "CashTransfers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidedByUserId",
                schema: "accounting",
                table: "CashTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000000e1"), true, "Sales.Quotation.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000e2"), false, "Sales.Quotation.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000e3"), true, "Sales.SalesOrder.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000e4"), false, "Sales.SalesOrder.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000e5"), true, "Sales.Invoice.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000e6"), false, "Sales.Invoice.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000e7"), true, "Sales.CreditNote.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000e8"), false, "Sales.CreditNote.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000e9"), true, "Payments.Payment.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000ea"), false, "Payments.Payment.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000eb"), true, "Purchasing.PurchaseOrder.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000ec"), false, "Purchasing.PurchaseOrder.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000ed"), true, "Purchasing.PurchaseBill.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000ee"), false, "Purchasing.PurchaseBill.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000ef"), true, "Purchasing.Expense.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000f0"), false, "Purchasing.Expense.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000f1"), true, "Purchasing.DebitNote.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000f2"), false, "Purchasing.DebitNote.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000f3"), true, "Accounting.JournalVoucher.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000f4"), false, "Accounting.JournalVoucher.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000f5"), true, "Accounting.CashTransfer.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000f6"), false, "Accounting.CashTransfer.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000f7"), true, "Inventory.WarehouseTransfer.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000f8"), false, "Inventory.WarehouseTransfer.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000f9"), true, "Inventory.InventoryAdjustment.Void", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000fa"), false, "Inventory.InventoryAdjustment.Void", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000000fb"), true, "Tenancy.Organization.LockDateManage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000000fc"), false, "Tenancy.Organization.LockDateManage", new Guid("00000000-0000-0000-0001-000000000002") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e1"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e2"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e3"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e4"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e5"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e6"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e7"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e8"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000e9"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000ea"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000eb"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000ec"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000ed"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000ee"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000ef"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f0"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f1"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f2"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f3"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f4"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f5"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f6"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f7"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f8"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000f9"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000fa"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000fb"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000000fc"));

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "ConsumedUnitCost",
                schema: "inventory",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "ConsumedUnitCost",
                schema: "purchasing",
                table: "DebitNoteLines");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                schema: "accounting",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                schema: "accounting",
                table: "CashTransfers");
        }
    }
}
