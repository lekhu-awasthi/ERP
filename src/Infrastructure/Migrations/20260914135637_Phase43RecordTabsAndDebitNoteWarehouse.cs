using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase43RecordTabsAndDebitNoteWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HAND-CORRECTED. `dotnet ef migrations add` scaffolded these two renames paired by
            // ordinal position rather than by meaning: it emitted TrialStartsAt -> TermEndsAt and
            // TrialEndsAt -> OriginatedAt, which SWAPS the two values on every existing row. A
            // tenant's origin would become its term end and vice versa, which SubscriptionExpiry
            // Behavior reads on every request -- every live tenant would have read as expired, and
            // nothing in the model would ever have said so, because the model only sees names.
            //
            // The correct pairing, which is what the Domain rename actually meant:
            //   TrialStartsAt -> OriginatedAt  (the tenant's origin, moved by nothing)
            //   TrialEndsAt   -> TermEndsAt    (the end of the current term, trial or paid)
            migrationBuilder.RenameColumn(
                name: "TrialStartsAt",
                schema: "tenancy",
                table: "TenantSubscriptions",
                newName: "OriginatedAt");

            migrationBuilder.RenameColumn(
                name: "TrialEndsAt",
                schema: "tenancy",
                table: "TenantSubscriptions",
                newName: "TermEndsAt");

            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                schema: "purchasing",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            // Phase 43 -- the backfill, and it is about behaviour rather than about NULL.
            //
            // The column is nullable, so phase-37's "a default is safe exactly when it is already
            // true of the existing rows" does not bite here. What would bite without this is the
            // approve path: ApproveDebitNoteCommandHandler used to consume at the SOURCE BILL's
            // warehouse and now consumes at the note's own, so every converted Draft note already
            // in the database would refuse to approve until someone re-picked a warehouse it could
            // have worked out. Copying the bill's warehouse across makes the new one-rule path
            // produce exactly what the old two-branch path produced.
            //
            // Approved and voided notes are updated too. They consumed their stock long ago and
            // nothing re-reads this for them, but a column that is populated for some rows of a
            // kind and empty for others is a trap for the next reader, and the detail page now
            // shows it.
            //
            // Standalone notes (no PurchaseBill referrer) are deliberately left NULL: there is no
            // warehouse to infer, and inventing one would be guessing where goods came from.
            migrationBuilder.Sql(
                """
                UPDATE dn
                SET dn.WarehouseId = pb.WarehouseId
                FROM purchasing.DebitNotes AS dn
                INNER JOIN purchasing.PurchaseBills AS pb
                    ON pb.Id = dn.ReferrerId
                   AND pb.OrganizationId = dn.OrganizationId
                WHERE dn.ReferrerType = 'PurchaseBill' AND dn.WarehouseId IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_WarehouseId",
                schema: "purchasing",
                table: "DebitNotes",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNotes_Warehouses_WarehouseId",
                schema: "purchasing",
                table: "DebitNotes",
                column: "WarehouseId",
                principalSchema: "tenancy",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DebitNotes_Warehouses_WarehouseId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_WarehouseId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                schema: "purchasing",
                table: "DebitNotes");

            // The exact inverse of the corrected Up above, not of what was scaffolded.
            migrationBuilder.RenameColumn(
                name: "OriginatedAt",
                schema: "tenancy",
                table: "TenantSubscriptions",
                newName: "TrialStartsAt");

            migrationBuilder.RenameColumn(
                name: "TermEndsAt",
                schema: "tenancy",
                table: "TenantSubscriptions",
                newName: "TrialEndsAt");
        }
    }
}
