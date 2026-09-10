using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase34cTenantIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WarehouseTransfers_OrganizationId_CreatedAt",
                schema: "inventory",
                table: "WarehouseTransfers",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseTransfers_OrganizationId_Date",
                schema: "inventory",
                table: "WarehouseTransfers",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrganizationId_CreatedAt",
                schema: "sales",
                table: "SalesOrders",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrganizationId_Date",
                schema: "sales",
                table: "SalesOrders",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_OrganizationId_CreatedAt",
                schema: "sales",
                table: "Quotations",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_OrganizationId_Date",
                schema: "sales",
                table: "Quotations",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "PurchaseOrders",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId_Date",
                schema: "purchasing",
                table: "PurchaseOrders",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBills_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "PurchaseBills",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBills_OrganizationId_Date",
                schema: "purchasing",
                table: "PurchaseBills",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_OrganizationId_CreatedAt",
                schema: "manufacturing",
                table: "ProductionOrders",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_OrganizationId_Date",
                schema: "manufacturing",
                table: "ProductionOrders",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournals_OrganizationId_CreatedAt",
                schema: "manufacturing",
                table: "ProductionJournals",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournals_OrganizationId_Date",
                schema: "manufacturing",
                table: "ProductionJournals",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrganizationId_CreatedAt",
                schema: "payments",
                table: "Payments",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrganizationId_Date",
                schema: "payments",
                table: "Payments",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MigratedSalesRegisterEntries_OrganizationId_CreatedAt",
                schema: "sales",
                table: "MigratedSalesRegisterEntries",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MigratedPurchaseRegisterEntries_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "MigratedPurchaseRegisterEntries",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_JournalVouchers_OrganizationId_CreatedAt",
                schema: "accounting",
                table: "JournalVouchers",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_JournalVouchers_OrganizationId_Date",
                schema: "accounting",
                table: "JournalVouchers",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrganizationId_CreatedAt",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrganizationId_Date",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_OrganizationId_CreatedAt",
                schema: "inventory",
                table: "InventoryAdjustments",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_OrganizationId_Date",
                schema: "inventory",
                table: "InventoryAdjustments",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_GlJournalEntries_OrganizationId_PostedAt",
                schema: "accounting",
                table: "GlJournalEntries",
                columns: new[] { "OrganizationId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "Expenses",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_Date",
                schema: "purchasing",
                table: "Expenses",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "DebitNotes",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_OrganizationId_Date",
                schema: "purchasing",
                table: "DebitNotes",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrganizationId_CreatedAt",
                schema: "sales",
                table: "CreditNotes",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrganizationId_Date",
                schema: "sales",
                table: "CreditNotes",
                columns: new[] { "OrganizationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_OrganizationId_CreatedAt",
                schema: "accounting",
                table: "CashTransfers",
                columns: new[] { "OrganizationId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_OrganizationId_Date",
                schema: "accounting",
                table: "CashTransfers",
                columns: new[] { "OrganizationId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehouseTransfers_OrganizationId_CreatedAt",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseTransfers_OrganizationId_Date",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrganizationId_CreatedAt",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrganizationId_Date",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_OrganizationId_CreatedAt",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_OrganizationId_Date",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_OrganizationId_Date",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseBills_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseBills_OrganizationId_Date",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_OrganizationId_CreatedAt",
                schema: "manufacturing",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_OrganizationId_Date",
                schema: "manufacturing",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionJournals_OrganizationId_CreatedAt",
                schema: "manufacturing",
                table: "ProductionJournals");

            migrationBuilder.DropIndex(
                name: "IX_ProductionJournals_OrganizationId_Date",
                schema: "manufacturing",
                table: "ProductionJournals");

            migrationBuilder.DropIndex(
                name: "IX_Payments_OrganizationId_CreatedAt",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_OrganizationId_Date",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_MigratedSalesRegisterEntries_OrganizationId_CreatedAt",
                schema: "sales",
                table: "MigratedSalesRegisterEntries");

            migrationBuilder.DropIndex(
                name: "IX_MigratedPurchaseRegisterEntries_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "MigratedPurchaseRegisterEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalVouchers_OrganizationId_CreatedAt",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropIndex(
                name: "IX_JournalVouchers_OrganizationId_Date",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OrganizationId_CreatedAt",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OrganizationId_Date",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_OrganizationId_CreatedAt",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_OrganizationId_Date",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_GlJournalEntries_OrganizationId_PostedAt",
                schema: "accounting",
                table: "GlJournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_OrganizationId_Date",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_OrganizationId_CreatedAt",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_OrganizationId_Date",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_OrganizationId_CreatedAt",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_OrganizationId_Date",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CashTransfers_OrganizationId_CreatedAt",
                schema: "accounting",
                table: "CashTransfers");

            migrationBuilder.DropIndex(
                name: "IX_CashTransfers_OrganizationId_Date",
                schema: "accounting",
                table: "CashTransfers");
        }
    }
}
