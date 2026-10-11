using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase81SalesPriceKeys : Migration
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
                    { new Guid("00000000-0000-0000-0002-0000000001f9"), true, null, "Sales.Invoice.Discount", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001fa"), true, null, "Sales.Invoice.Discount", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001fb"), true, null, "Sales.Invoice.ChangeRate", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001fc"), true, null, "Sales.Invoice.ChangeRate", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            // Hand-written (phase 81, the user's 2026-10-11 decision): until now anyone who could create or edit
            // a Quotation, Sales Order or Invoice could discount it and type any rate. Grant both keys wherever a
            // custom role holds any of those six keys, at the same scope (organization-wide, or at that one
            // location), so no clerk loses a power on upgrade. DISTINCT because one role usually holds several of
            // the six at one scope, and the unique index on (RoleId, PermissionKey, LocationId) is unfiltered.
            // After the InsertData above, so NOT EXISTS skips the system roles' seeded rows.
            migrationBuilder.Sql("""
                INSERT INTO [tenancy].[RolePermissions] ([Id], [RoleId], [PermissionKey], [IsGranted], [LocationId])
                SELECT NEWID(), a.[RoleId], k.[PermissionKey], CAST(1 AS bit), a.[LocationId]
                FROM (SELECT DISTINCT [RoleId], [LocationId]
                      FROM [tenancy].[RolePermissions]
                      WHERE [IsGranted] = 1
                        AND [PermissionKey] IN (N'Sales.Quotation.Create', N'Sales.Quotation.Edit',
                                                N'Sales.SalesOrder.Create', N'Sales.SalesOrder.Edit',
                                                N'Sales.Invoice.Create', N'Sales.Invoice.Edit')) AS a
                CROSS JOIN (VALUES (N'Sales.Invoice.Discount'), (N'Sales.Invoice.ChangeRate')) AS k([PermissionKey])
                WHERE NOT EXISTS (
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
                WHERE [PermissionKey] IN (N'Sales.Invoice.Discount', N'Sales.Invoice.ChangeRate')
                  AND [Id] NOT IN ('00000000-0000-0000-0002-0000000001f9', '00000000-0000-0000-0002-0000000001fa',
                                   '00000000-0000-0000-0002-0000000001fb', '00000000-0000-0000-0002-0000000001fc');
                """);

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f9"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001fa"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001fb"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001fc"));
        }
    }
}
