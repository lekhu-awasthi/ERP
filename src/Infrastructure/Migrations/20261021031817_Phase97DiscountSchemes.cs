using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase97DiscountSchemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DiscountSetByHand",
                schema: "pos",
                table: "PosOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DiscountSchemes",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    AllLocations = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountSchemes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DiscountSchemeLines",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountSchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    MinimumAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    DiscountPct = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountSchemeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountSchemeLines_DiscountSchemes_DiscountSchemeId",
                        column: x => x.DiscountSchemeId,
                        principalSchema: "sales",
                        principalTable: "DiscountSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DiscountSchemeLocations",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiscountSchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountSchemeLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountSchemeLocations_BillingLocations_BillingLocationId",
                        column: x => x.BillingLocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscountSchemeLocations_DiscountSchemes_DiscountSchemeId",
                        column: x => x.DiscountSchemeId,
                        principalSchema: "sales",
                        principalTable: "DiscountSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "LocationId", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001fd"), true, null, "Sales.DiscountScheme.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001fe"), true, null, "Sales.DiscountScheme.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001ff"), true, null, "Sales.DiscountScheme.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000200"), false, null, "Sales.DiscountScheme.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountSchemeLines_DiscountSchemeId",
                schema: "sales",
                table: "DiscountSchemeLines",
                column: "DiscountSchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountSchemeLines_OrganizationId_DiscountSchemeId",
                schema: "sales",
                table: "DiscountSchemeLines",
                columns: new[] { "OrganizationId", "DiscountSchemeId" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountSchemeLocations_BillingLocationId",
                schema: "sales",
                table: "DiscountSchemeLocations",
                column: "BillingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountSchemeLocations_DiscountSchemeId",
                schema: "sales",
                table: "DiscountSchemeLocations",
                column: "DiscountSchemeId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountSchemeLocations_OrganizationId_DiscountSchemeId_BillingLocationId",
                schema: "sales",
                table: "DiscountSchemeLocations",
                columns: new[] { "OrganizationId", "DiscountSchemeId", "BillingLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscountSchemes_OrganizationId_Code",
                schema: "sales",
                table: "DiscountSchemes",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            // An open order whose discount a cashier already set keeps it: a slab scheme moves only a discount nobody set
            // (PosOrder.DiscountSetByHand). Every existing discount was set by hand, since no scheme existed before.
            migrationBuilder.Sql("""
                UPDATE [pos].[PosOrders] SET [DiscountSetByHand] = CAST(1 AS bit)
                WHERE [DiscountPct] <> 0 OR [DiscountAmount] <> 0;
                """);

            // As phase 81: a custom role that drafts sales reads the schemes its forms suggest, organization-wide (the key
            // is not per location).
            migrationBuilder.Sql("""
                INSERT INTO [tenancy].[RolePermissions] ([Id], [RoleId], [PermissionKey], [IsGranted], [LocationId])
                SELECT NEWID(), a.[RoleId], N'Sales.DiscountScheme.View', CAST(1 AS bit), NULL
                FROM (SELECT DISTINCT [RoleId]
                      FROM [tenancy].[RolePermissions]
                      WHERE [IsGranted] = 1
                        AND [PermissionKey] IN (N'Sales.Quotation.Create', N'Sales.Quotation.Edit',
                                                N'Sales.SalesOrder.Create', N'Sales.SalesOrder.Edit',
                                                N'Sales.Invoice.Create', N'Sales.Invoice.Edit')) AS a
                WHERE NOT EXISTS (
                      SELECT 1 FROM [tenancy].[RolePermissions] AS x
                      WHERE x.[RoleId] = a.[RoleId]
                        AND x.[PermissionKey] = N'Sales.DiscountScheme.View'
                        AND x.[LocationId] IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The backfill's rows; the seeded ones go with DeleteData below.
            migrationBuilder.Sql("""
                DELETE FROM [tenancy].[RolePermissions]
                WHERE [PermissionKey] = N'Sales.DiscountScheme.View'
                  AND [Id] NOT IN ('00000000-0000-0000-0002-0000000001fd', '00000000-0000-0000-0002-0000000001fe');
                """);

            migrationBuilder.DropTable(
                name: "DiscountSchemeLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "DiscountSchemeLocations",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "DiscountSchemes",
                schema: "sales");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001fd"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001fe"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ff"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000200"));

            migrationBuilder.DropColumn(
                name: "DiscountSetByHand",
                schema: "pos",
                table: "PosOrders");
        }
    }
}
