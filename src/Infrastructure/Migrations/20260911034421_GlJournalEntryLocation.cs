using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 35b -- GlJournalEntry.LocationId, plus the backfill that makes it true of history and
    /// not only of what is posted from now on.
    ///
    /// <para><b>Eleven UPDATE ... FROM statements, one per posting type.</b> Every type that calls
    /// GlJournalEntry.Post is in DocumentMechanisms.LocationBearing and therefore already carries a
    /// LocationId column of its own, so the backfill is a join on the source document id the entry
    /// already stores -- there is nothing to derive. SourceDocumentType is stored as a string
    /// (HasConversion&lt;string&gt;), which is why each statement matches a literal name; a member
    /// renamed later breaks this migration loudly rather than silently skipping a type.</para>
    ///
    /// <para><b>Reversals come along for free.</b> A void posts a second entry against the same
    /// SourceDocumentType/SourceDocumentId (GlJournalEntry.PostReversalOf, phase 16a), so the same
    /// join reaches both halves and a voided document's pair stays at the same location -- which is
    /// what keeps a branch's Trial Balance balanced across a void.</para>
    ///
    /// <para><b>Only non-null source locations are written.</b> A document raised while its type was
    /// outside the tenant's LocationScopeMode has no location, and writing NULL over NULL is a no-op
    /// worth skipping on a large table. The IS NOT NULL predicate is the difference between touching
    /// every historical entry and touching only those a location can be established for.</para>
    ///
    /// <para>No index is added -- see GlJournalEntryConfiguration for why, and phase 34c for the
    /// measurement behind it.</para>
    /// </summary>
    public partial class GlJournalEntryLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "accounting",
                table: "GlJournalEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [accounting].[CashTransfers] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'CashTransfer' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [accounting].[JournalVouchers] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'JournalVoucher' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [accounting].[OpeningBalanceLines] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'OpeningBalance' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [inventory].[InventoryAdjustments] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'InventoryAdjustment' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [manufacturing].[ProductionJournals] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'ProductionJournal' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [payments].[Payments] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'Payment' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [purchasing].[DebitNotes] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'DebitNote' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [purchasing].[Expenses] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'Expense' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [purchasing].[PurchaseBills] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'PurchaseBill' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [sales].[CreditNotes] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'CreditNote' AND d.LocationId IS NOT NULL;");

            migrationBuilder.Sql(@"
                UPDATE e
                SET e.LocationId = d.LocationId
                FROM [accounting].[GlJournalEntries] e
                INNER JOIN [sales].[Invoices] d
                    ON d.Id = e.SourceDocumentId AND d.OrganizationId = e.OrganizationId
                WHERE e.SourceDocumentType = 'Invoice' AND d.LocationId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "accounting",
                table: "GlJournalEntries");
        }
    }
}
