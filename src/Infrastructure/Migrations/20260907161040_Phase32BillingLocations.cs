using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 32 (Billing Locations, FR-2.3/FR-3.3). Creates <c>tenancy.BillingLocations</c>, adds a
    /// nullable <c>LocationId</c> to all 17 location-bearing types with an FK apiece, adds the two
    /// Advanced-panel settings to <c>tenancy.TenantSettings</c>, and seeds the four permission rows.
    ///
    /// <para><b>Two of its steps are hand-written and their position is load-bearing.</b> The
    /// scaffold produces schema only, and CLAUDE.md's standing rule -- <i>read any migration that
    /// adds, replaces or retypes a column on a populated table and write its backfill by hand</i>
    /// (phase 31) -- applies twice over here:</para>
    /// <list type="number">
    /// <item>every <i>pre-existing</i> Organization needs the HeadOffice row that
    /// <c>CreateOrganizationCommandHandler</c> seeds only for organizations created from now on.
    /// Without it those tenants get the feature with an empty list and nothing for step 2 to point
    /// at;</item>
    /// <item>every pre-existing document is pointed at its own tenant's HeadOffice, because before
    /// this phase a tenant had exactly one place to transact from.</item>
    /// </list>
    ///
    /// <para>Both sit after <c>CreateTable</c> and <b>before the <c>AddForeignKey</c> calls</b>: SQL
    /// Server validates a new foreign key against the rows already in the table, so a backfill placed
    /// after them would either fail the constraint or leave the columns null while the migration still
    /// reported success. This is the model-diff-vs-data-safety ordering CLAUDE.md warns about, in its
    /// add-a-column form.</para>
    ///
    /// <para>The <c>LocationId</c> columns are <b>nullable</b>, so unlike phase 31's <c>DueDate</c>
    /// there is no NOT NULL step and no stray default constraint to clean up. The backfill is still
    /// hand-written, because "nullable" only removes the crash, not the wrong answer.</para>
    /// </summary>
    public partial class Phase32BillingLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "inventory",
                table: "WarehouseTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationScopeMode",
                schema: "tenancy",
                table: "TenantSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "LocationWiseReportPermission",
                schema: "tenancy",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "sales",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "sales",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "manufacturing",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "manufacturing",
                table: "ProductionJournals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "payments",
                table: "Payments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "inventory",
                table: "OpeningStockLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "accounting",
                table: "OpeningBalanceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "accounting",
                table: "JournalVouchers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "inventory",
                table: "InventoryAdjustments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "purchasing",
                table: "Expenses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "purchasing",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "sales",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "accounting",
                table: "CashTransfers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingLocations",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LocationType = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingLocations", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001b5"), true, "Tenancy.BillingLocation.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001b6"), true, "Tenancy.BillingLocation.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001b7"), true, "Tenancy.BillingLocation.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001b8"), false, "Tenancy.BillingLocation.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            // Phase 32, hand-written step 1 of 2 -- see this file's header comment for why these two
            // steps sit here, between CreateTable and AddForeignKey, rather than anywhere else.
            //
            // Every Organization that already exists gets the HeadOffice row that
            // CreateOrganizationCommandHandler will seed for every future one. NEWID() rather than a
            // fixed Guid because there is one row per tenant, not one row in total. Idempotent via
            // NOT EXISTS so a re-run cannot double-seed.
            migrationBuilder.Sql(@"
INSERT INTO [tenancy].[BillingLocations]
    ([Id], [OrganizationId], [Code], [Name], [Address], [WarehouseId], [LocationType], [IsActive], [CreatedAt])
SELECT NEWID(), o.[Id], N'HO', N'HeadOffice', NULL, NULL, 1, 1, SYSDATETIMEOFFSET()
FROM [tenancy].[Organizations] o
WHERE NOT EXISTS (
    SELECT 1 FROM [tenancy].[BillingLocations] b WHERE b.[OrganizationId] = o.[Id]);
");

            // Phase 32, hand-written step 2 of 2 -- point every pre-existing document at its own
            // tenant's HeadOffice. Truthful rather than merely convenient: before this phase a tenant
            // had exactly one place to transact from, so HeadOffice is where these documents were
            // actually raised. Leaving them null would instead make every location-filtered report
            // silently omit all history.
            migrationBuilder.Sql(@"
UPDATE d SET d.[LocationId] = b.[Id]
FROM [inventory].[WarehouseTransfers] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [sales].[SalesOrders] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [sales].[Quotations] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [sales].[Invoices] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [sales].[CreditNotes] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [purchasing].[PurchaseOrders] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [purchasing].[PurchaseBills] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [purchasing].[Expenses] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [purchasing].[DebitNotes] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [manufacturing].[ProductionOrders] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [manufacturing].[ProductionJournals] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [payments].[Payments] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [inventory].[OpeningStockLines] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [inventory].[InventoryAdjustments] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [accounting].[OpeningBalanceLines] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [accounting].[JournalVouchers] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;

UPDATE d SET d.[LocationId] = b.[Id]
FROM [accounting].[CashTransfers] d
INNER JOIN [tenancy].[BillingLocations] b
    ON b.[OrganizationId] = d.[OrganizationId] AND b.[LocationType] = 1
WHERE d.[LocationId] IS NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseTransfers_LocationId",
                schema: "inventory",
                table: "WarehouseTransfers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_LocationId",
                schema: "sales",
                table: "SalesOrders",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_LocationId",
                schema: "sales",
                table: "Quotations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_LocationId",
                schema: "purchasing",
                table: "PurchaseOrders",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBills_LocationId",
                schema: "purchasing",
                table: "PurchaseBills",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_LocationId",
                schema: "manufacturing",
                table: "ProductionOrders",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionJournals_LocationId",
                schema: "manufacturing",
                table: "ProductionJournals",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_LocationId",
                schema: "payments",
                table: "Payments",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningStockLines_LocationId",
                schema: "inventory",
                table: "OpeningStockLines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_OpeningBalanceLines_LocationId",
                schema: "accounting",
                table: "OpeningBalanceLines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalVouchers_LocationId",
                schema: "accounting",
                table: "JournalVouchers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_LocationId",
                schema: "sales",
                table: "Invoices",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_LocationId",
                schema: "inventory",
                table: "InventoryAdjustments",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_LocationId",
                schema: "purchasing",
                table: "Expenses",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_LocationId",
                schema: "purchasing",
                table: "DebitNotes",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_LocationId",
                schema: "sales",
                table: "CreditNotes",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_LocationId",
                schema: "accounting",
                table: "CashTransfers",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLocations_OrganizationId",
                schema: "tenancy",
                table: "BillingLocations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingLocations_OrganizationId_Code",
                schema: "tenancy",
                table: "BillingLocations",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransfers_BillingLocations_LocationId",
                schema: "accounting",
                table: "CashTransfers",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_BillingLocations_LocationId",
                schema: "sales",
                table: "CreditNotes",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNotes_BillingLocations_LocationId",
                schema: "purchasing",
                table: "DebitNotes",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_BillingLocations_LocationId",
                schema: "purchasing",
                table: "Expenses",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryAdjustments_BillingLocations_LocationId",
                schema: "inventory",
                table: "InventoryAdjustments",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_BillingLocations_LocationId",
                schema: "sales",
                table: "Invoices",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalVouchers_BillingLocations_LocationId",
                schema: "accounting",
                table: "JournalVouchers",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OpeningBalanceLines_BillingLocations_LocationId",
                schema: "accounting",
                table: "OpeningBalanceLines",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OpeningStockLines_BillingLocations_LocationId",
                schema: "inventory",
                table: "OpeningStockLines",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_BillingLocations_LocationId",
                schema: "payments",
                table: "Payments",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionJournals_BillingLocations_LocationId",
                schema: "manufacturing",
                table: "ProductionJournals",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductionOrders_BillingLocations_LocationId",
                schema: "manufacturing",
                table: "ProductionOrders",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseBills_BillingLocations_LocationId",
                schema: "purchasing",
                table: "PurchaseBills",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_BillingLocations_LocationId",
                schema: "purchasing",
                table: "PurchaseOrders",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_BillingLocations_LocationId",
                schema: "sales",
                table: "Quotations",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesOrders_BillingLocations_LocationId",
                schema: "sales",
                table: "SalesOrders",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WarehouseTransfers_BillingLocations_LocationId",
                schema: "inventory",
                table: "WarehouseTransfers",
                column: "LocationId",
                principalSchema: "tenancy",
                principalTable: "BillingLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashTransfers_BillingLocations_LocationId",
                schema: "accounting",
                table: "CashTransfers");

            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_BillingLocations_LocationId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_DebitNotes_BillingLocations_LocationId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_BillingLocations_LocationId",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryAdjustments_BillingLocations_LocationId",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_BillingLocations_LocationId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalVouchers_BillingLocations_LocationId",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropForeignKey(
                name: "FK_OpeningBalanceLines_BillingLocations_LocationId",
                schema: "accounting",
                table: "OpeningBalanceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_OpeningStockLines_BillingLocations_LocationId",
                schema: "inventory",
                table: "OpeningStockLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_BillingLocations_LocationId",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionJournals_BillingLocations_LocationId",
                schema: "manufacturing",
                table: "ProductionJournals");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductionOrders_BillingLocations_LocationId",
                schema: "manufacturing",
                table: "ProductionOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseBills_BillingLocations_LocationId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_BillingLocations_LocationId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_BillingLocations_LocationId",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_BillingLocations_LocationId",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_WarehouseTransfers_BillingLocations_LocationId",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropTable(
                name: "BillingLocations",
                schema: "tenancy");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseTransfers_LocationId",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropIndex(
                name: "IX_SalesOrders_LocationId",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_LocationId",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_LocationId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseBills_LocationId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropIndex(
                name: "IX_ProductionOrders_LocationId",
                schema: "manufacturing",
                table: "ProductionOrders");

            migrationBuilder.DropIndex(
                name: "IX_ProductionJournals_LocationId",
                schema: "manufacturing",
                table: "ProductionJournals");

            migrationBuilder.DropIndex(
                name: "IX_Payments_LocationId",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_OpeningStockLines_LocationId",
                schema: "inventory",
                table: "OpeningStockLines");

            migrationBuilder.DropIndex(
                name: "IX_OpeningBalanceLines_LocationId",
                schema: "accounting",
                table: "OpeningBalanceLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalVouchers_LocationId",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_LocationId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InventoryAdjustments_LocationId",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_LocationId",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_LocationId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_LocationId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CashTransfers_LocationId",
                schema: "accounting",
                table: "CashTransfers");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b5"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b6"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b7"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b8"));

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "inventory",
                table: "WarehouseTransfers");

            migrationBuilder.DropColumn(
                name: "LocationScopeMode",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "LocationWiseReportPermission",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "manufacturing",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "manufacturing",
                table: "ProductionJournals");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "inventory",
                table: "OpeningStockLines");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "accounting",
                table: "OpeningBalanceLines");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "inventory",
                table: "InventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "accounting",
                table: "CashTransfers");
        }
    }
}
