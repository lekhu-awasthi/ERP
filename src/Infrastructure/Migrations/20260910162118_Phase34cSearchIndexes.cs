using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase34cSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WarehouseTransfers_OrganizationId_Code",
                schema: "inventory",
                table: "WarehouseTransfers",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_OrganizationId_Code",
                schema: "sales",
                table: "SalesOrders",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_OrganizationId_Code",
                schema: "sales",
                table: "Quotations",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId_Code",
                schema: "purchasing",
                table: "PurchaseOrders",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBills_OrganizationId_Code",
                schema: "purchasing",
                table: "PurchaseBills",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference", "SupplierInvoiceReference" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId_Name_Code",
                schema: "catalog",
                table: "Products",
                columns: new[] { "OrganizationId", "Name", "Code" })
                .Annotation("SqlServer:Include", new[] { "Sku" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_OrganizationId_Code",
                schema: "manufacturing",
                table: "ProductionOrders",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournals_OrganizationId_Code",
                schema: "manufacturing",
                table: "ProductionJournals",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrganizationId_Code",
                schema: "payments",
                table: "Payments",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalVouchers_OrganizationId_Code",
                schema: "accounting",
                table: "JournalVouchers",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrganizationId_Code",
                schema: "sales",
                table: "Invoices",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_OrganizationId_Code",
                schema: "inventory",
                table: "InventoryAdjustments",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_OrganizationId_Code",
                schema: "purchasing",
                table: "Expenses",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "SupplierInvoiceReference" });

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_OrganizationId_Code",
                schema: "purchasing",
                table: "DebitNotes",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrganizationId_Code",
                schema: "sales",
                table: "CreditNotes",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_OrganizationId_Name_Code",
                schema: "contacts",
                table: "Contacts",
                columns: new[] { "OrganizationId", "Name", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_OrganizationId_Code",
                schema: "accounting",
                table: "CashTransfers",
                columns: new[] { "OrganizationId", "Code" })
                .Annotation("SqlServer:Include", new[] { "Reference" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehouseTransfers_OrganizationId_Code",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_OrganizationId_Code",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_OrganizationId_Code",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_OrganizationId_Code",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseBills_OrganizationId_Code",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropIndex(
                name: "IX_Products_OrganizationId_Name_Code",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_OrganizationId_Code",
                schema: "manufacturing",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionJournals_OrganizationId_Code",
                schema: "manufacturing",
                table: "ProductionJournals");

            migrationBuilder.DropIndex(
                name: "IX_Payments_OrganizationId_Code",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_JournalVouchers_OrganizationId_Code",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_OrganizationId_Code",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_OrganizationId_Code",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_OrganizationId_Code",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_OrganizationId_Code",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_OrganizationId_Code",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_OrganizationId_Name_Code",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_CashTransfers_OrganizationId_Code",
                schema: "accounting",
                table: "CashTransfers");
        }
    }
}
