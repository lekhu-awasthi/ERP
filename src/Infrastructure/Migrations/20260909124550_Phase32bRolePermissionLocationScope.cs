using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 32b -- per-location permission scope (FR-3.3).
    ///
    /// <para><b>No backfill, and that is the point.</b> Every RolePermission row written before this
    /// phase is an organization-wide grant, which is exactly what <c>LocationId = NULL</c> means, so
    /// adding the column nullable leaves every existing row already correct. The roadmap flagged
    /// "N locations x 94 keys" as the first permission set whose row count is a function of tenant
    /// data; it is not, because an absent row is a denial and the live editor's own default is
    /// 0 of 94 at every location. Nothing is seeded per location, here or at Organization creation.</para>
    ///
    /// <para><b>The scaffold was hand-trimmed.</b> <c>dotnet ef migrations add</c> emitted an
    /// <c>UpdateData(..., column: "LocationId", value: null)</c> for all ~440 seeded RolePermission
    /// rows -- 3,500 lines setting a just-created nullable column to the null it already holds. They
    /// are removed. CLAUDE.md's "read any migration that replaces or retypes a column and reorder by
    /// hand" rule, applied to one that merely adds one (phase-31 lesson (e)).</para>
    ///
    /// <para><b>The new unique index is deliberately unfiltered.</b>
    /// <c>(RoleId, PermissionKey, LocationId)</c> carries no <c>WHERE [LocationId] IS NOT NULL</c>,
    /// overriding both EF's convention and CLAUDE.md's own standing gotcha, because NULL here is not
    /// "no value" but the sentinel for <i>the</i> organization-wide grant, of which there must be at
    /// most one per (role, key). SQL Server treats NULLs as equal in a unique index, so the
    /// unfiltered form is the enforcement -- phase 32's numbering-counter inversion, second instance.
    /// It also supersedes the old <c>(RoleId, PermissionKey)</c> unique index exactly: with every
    /// existing row's LocationId null, the two constrain the same rows identically, so the swap
    /// cannot fail on live data.</para>
    /// </summary>
    public partial class Phase32bRolePermissionLocationScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_RoleId_PermissionKey",
                schema: "tenancy",
                table: "RolePermissions");

            migrationBuilder.AddColumn<Guid>(
                name: "LocationId",
                schema: "tenancy",
                table: "RolePermissions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_LocationId",
                schema: "tenancy",
                table: "RolePermissions",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionKey_LocationId",
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionKey", "LocationId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RolePermissions_BillingLocations_LocationId",
                schema: "tenancy",
                table: "RolePermissions",
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
                name: "FK_RolePermissions_BillingLocations_LocationId",
                schema: "tenancy",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_LocationId",
                schema: "tenancy",
                table: "RolePermissions");

            migrationBuilder.DropIndex(
                name: "IX_RolePermissions_RoleId_PermissionKey_LocationId",
                schema: "tenancy",
                table: "RolePermissions");

            // Any location-scoped grant made while this phase was deployed is dropped with the
            // column, so the old (RoleId, PermissionKey) unique index cannot be violated by the rows
            // that remain -- all of which are organization-wide by construction.
            migrationBuilder.DropColumn(
                name: "LocationId",
                schema: "tenancy",
                table: "RolePermissions");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionKey",
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionKey" },
                unique: true);
        }
    }
}
