using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 35b -- LocationId on both stock fact tables, plus the backfill across the eight types
    /// that write them.
    ///
    /// <para>The same shape as GlJournalEntryLocation, in the same phase and for the same reason:
    /// StockMovement and StockLedgerEntry point back at their source with (SourceDocumentType,
    /// SourceDocumentId) and nothing else, and six inventory reports carry a Billing Location filter
    /// live. Eight types rather than eleven because three of the GL's producers move no stock
    /// (JournalVoucher, CashTransfer, Expense, Payment) while two stock producers post no GL of
    /// their own (WarehouseTransfer, OpeningStock) -- the two sets overlap, neither contains the
    /// other, which is why this is a second explicit list and not a copy of the first.</para>
    ///
    /// <para><b>A consumed layer's Out movement inherits its layer's location</b>, which the
    /// backfill reproduces for history: an Out movement carries the *consuming* document's id, so
    /// it is backfilled from that document, exactly as StockLedgerService now stamps it.</para>
    /// </summary>
    public partial class StockFactLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "inventory",
                table: "StockMovements",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "inventory",
                table: "StockLedgerEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [sales].[Invoices] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'Invoice' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [sales].[CreditNotes] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'CreditNote' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [purchasing].[PurchaseBills] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'PurchaseBill' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [purchasing].[DebitNotes] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'DebitNote' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [inventory].[InventoryAdjustments] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'InventoryAdjustment' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [inventory].[WarehouseTransfers] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'WarehouseTransfer' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [inventory].[OpeningStockLines] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'OpeningStock' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockMovements] f
                INNER JOIN [manufacturing].[ProductionJournals] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'ProductionJournal' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [sales].[Invoices] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'Invoice' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [sales].[CreditNotes] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'CreditNote' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [purchasing].[PurchaseBills] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'PurchaseBill' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [purchasing].[DebitNotes] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'DebitNote' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [inventory].[InventoryAdjustments] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'InventoryAdjustment' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [inventory].[WarehouseTransfers] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'WarehouseTransfer' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [inventory].[OpeningStockLines] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'OpeningStock' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE f
                SET f.LocationId = d.LocationId
                FROM [inventory].[StockLedgerEntries] f
                INNER JOIN [manufacturing].[ProductionJournals] d
                    ON d.Id = f.SourceDocumentId AND d.OrganizationId = f.OrganizationId
                WHERE f.SourceDocumentType = 'ProductionJournal' AND d.LocationId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "inventory",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "inventory",
                table: "StockLedgerEntries");
        }
    }
}
