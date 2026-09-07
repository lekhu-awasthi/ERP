using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 32 -- makes the document-numbering counter location-aware, so the live
    /// <b>"Enable Location-wise Next Number"</b> toggle (confirmed 2026-09-07, and visible only on a
    /// tenant whose Billing Location entitlement is on) finally has a consumer. It has been written
    /// by the settings screen and read by nothing since phase 2.
    ///
    /// <para>A nullable <c>LocationId</c> joins the counter key: null is the settings row (one per
    /// org + type, carrying Prefix/Mode/the flags, and the shared counter while the toggle is off),
    /// non-null is one branch's own counter, created lazily on first approval from that branch.
    /// <b>No backfill is needed and none is written</b> -- every existing row already has
    /// <c>LocationId NULL</c>, which is exactly the settings row it was, so every tenant's numbering
    /// is unchanged until an Admin turns the toggle on.</para>
    ///
    /// <para><b>The rebuilt unique index deliberately carries no filter, and that is the point of
    /// reading this migration by hand.</b> EF Core scaffolds
    /// <c>filter: "[LocationId] IS NOT NULL"</c> automatically for a unique index over a nullable
    /// column -- usually correct, and what CLAUDE.md's own gotcha asks for. Here it is precisely
    /// inverted: SQL Server treating NULLs as <i>equal</i> is the invariant being bought, because it
    /// is what admits one and only one settings row per (org, type). Under EF's default filter those
    /// rows sit outside the index, a tenant can acquire two settings rows and two competing counters,
    /// and <c>DocumentNumberGenerator</c>'s lazy-create race -- which relies on a concurrent loser's
    /// INSERT violating this index -- stops being guarded. Suppressed with <c>HasFilter(null)</c> in
    /// <c>DocumentNumberingRuleConfiguration</c>; if a later scaffold reintroduces the filter, that is
    /// a bug, not a tidy-up.</para>
    /// </summary>
    public partial class Phase32LocationWiseNumbering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentNumberingRules_OrganizationId_DocumentType",
                schema: "configuration",
                table: "DocumentNumberingRules");

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberingRules_LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberingRules_OrganizationId_DocumentType_LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules",
                columns: new[] { "OrganizationId", "DocumentType", "LocationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentNumberingRules_BillingLocations_LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules",
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
                name: "FK_DocumentNumberingRules_BillingLocations_LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules");

            migrationBuilder.DropIndex(
                name: "IX_DocumentNumberingRules_LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules");

            migrationBuilder.DropIndex(
                name: "IX_DocumentNumberingRules_OrganizationId_DocumentType_LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules");

            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "configuration",
                table: "DocumentNumberingRules");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberingRules_OrganizationId_DocumentType",
                schema: "configuration",
                table: "DocumentNumberingRules",
                columns: new[] { "OrganizationId", "DocumentType" },
                unique: true);
        }
    }
}
