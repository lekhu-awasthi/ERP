using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase76TillKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "LocationId", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001f3"), true, null, "Sales.Invoice.TillDiscount", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001f4"), false, null, "Sales.Invoice.TillDiscount", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001f5"), true, null, "Sales.Invoice.TillRemoveServiceCharge", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001f6"), false, null, "Sales.Invoice.TillRemoveServiceCharge", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001f7"), true, null, "Sales.Invoice.TillChangeRate", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001f8"), false, null, "Sales.Invoice.TillChangeRate", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            // Hand-written (phase 76 Decision B): every custom role that holds Sales.Invoice.Approve today can
            // discount, waive and reprice at the till, because phases 72 and 74 gated all three on it. Grant the
            // three till keys wherever such a role holds Approve, at the same scope (organization-wide, or at that
            // one location), so no cashier loses a power on upgrade. An Admin revokes what they do not want.
            // After the InsertData above, so NOT EXISTS skips the system roles' seeded rows (Admin holds Approve
            // and the three keys already; Member holds neither). Runs once: the unique index on
            // (RoleId, PermissionKey, LocationId) is unfiltered, and NOT EXISTS keeps it from tripping.
            migrationBuilder.Sql("""
                INSERT INTO [tenancy].[RolePermissions] ([Id], [RoleId], [PermissionKey], [IsGranted], [LocationId])
                SELECT NEWID(), a.[RoleId], k.[PermissionKey], CAST(1 AS bit), a.[LocationId]
                FROM [tenancy].[RolePermissions] AS a
                CROSS JOIN (VALUES (N'Sales.Invoice.TillDiscount'),
                                   (N'Sales.Invoice.TillRemoveServiceCharge'),
                                   (N'Sales.Invoice.TillChangeRate')) AS k([PermissionKey])
                WHERE a.[PermissionKey] = N'Sales.Invoice.Approve'
                  AND a.[IsGranted] = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM [tenancy].[RolePermissions] AS x
                      WHERE x.[RoleId] = a.[RoleId]
                        AND x.[PermissionKey] = k.[PermissionKey]
                        AND (x.[LocationId] = a.[LocationId] OR (x.[LocationId] IS NULL AND a.[LocationId] IS NULL)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The backfill's rows; the seeded ones go with DeleteData below.
            migrationBuilder.Sql("""
                DELETE FROM [tenancy].[RolePermissions]
                WHERE [PermissionKey] IN (N'Sales.Invoice.TillDiscount', N'Sales.Invoice.TillRemoveServiceCharge',
                                          N'Sales.Invoice.TillChangeRate')
                  AND [Id] NOT IN ('00000000-0000-0000-0002-0000000001f3', '00000000-0000-0000-0002-0000000001f4',
                                   '00000000-0000-0000-0002-0000000001f5', '00000000-0000-0000-0002-0000000001f6',
                                   '00000000-0000-0000-0002-0000000001f7', '00000000-0000-0000-0002-0000000001f8');
                """);

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f3"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f4"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f5"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f6"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f7"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f8"));
        }
    }
}
