using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase24VariantProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                schema: "catalog",
                table: "Products",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CombinationKey",
                schema: "catalog",
                table: "Products",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasVariants",
                schema: "catalog",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentProductId",
                schema: "catalog",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sku",
                schema: "catalog",
                table: "Products",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VariantAttributes",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantAttributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VariantAttributeOptions",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantAttributeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VariantAttributeOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VariantAttributeOptions_VariantAttributes_VariantAttributeId",
                        column: x => x.VariantAttributeId,
                        principalSchema: "catalog",
                        principalTable: "VariantAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantAttributeUsages",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantAttributeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantAttributeOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantAttributeUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributeUsages_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributeUsages_VariantAttributeOptions_VariantAttributeOptionId",
                        column: x => x.VariantAttributeOptionId,
                        principalSchema: "catalog",
                        principalTable: "VariantAttributeOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantAttributeUsages_VariantAttributes_VariantAttributeId",
                        column: x => x.VariantAttributeId,
                        principalSchema: "catalog",
                        principalTable: "VariantAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantValues",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantAttributeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VariantAttributeOptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantValues_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductVariantValues_VariantAttributeOptions_VariantAttributeOptionId",
                        column: x => x.VariantAttributeOptionId,
                        principalSchema: "catalog",
                        principalTable: "VariantAttributeOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantValues_VariantAttributes_VariantAttributeId",
                        column: x => x.VariantAttributeId,
                        principalSchema: "catalog",
                        principalTable: "VariantAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000151"), true, "Catalog.VariantAttribute.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000152"), true, "Catalog.VariantAttribute.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000153"), true, "Catalog.VariantAttribute.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000154"), false, "Catalog.VariantAttribute.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId_ParentProductId_CombinationKey",
                schema: "catalog",
                table: "Products",
                columns: new[] { "OrganizationId", "ParentProductId", "CombinationKey" },
                unique: true,
                filter: "[ParentProductId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ParentProductId",
                schema: "catalog",
                table: "Products",
                column: "ParentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeUsages_ProductId_VariantAttributeOptionId",
                schema: "catalog",
                table: "ProductVariantAttributeUsages",
                columns: new[] { "ProductId", "VariantAttributeOptionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeUsages_VariantAttributeId",
                schema: "catalog",
                table: "ProductVariantAttributeUsages",
                column: "VariantAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantAttributeUsages_VariantAttributeOptionId",
                schema: "catalog",
                table: "ProductVariantAttributeUsages",
                column: "VariantAttributeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantValues_ProductId_VariantAttributeId",
                schema: "catalog",
                table: "ProductVariantValues",
                columns: new[] { "ProductId", "VariantAttributeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantValues_VariantAttributeId",
                schema: "catalog",
                table: "ProductVariantValues",
                column: "VariantAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantValues_VariantAttributeOptionId",
                schema: "catalog",
                table: "ProductVariantValues",
                column: "VariantAttributeOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_VariantAttributeOptions_VariantAttributeId",
                schema: "catalog",
                table: "VariantAttributeOptions",
                column: "VariantAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_VariantAttributes_OrganizationId_Name",
                schema: "catalog",
                table: "VariantAttributes",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Products_ParentProductId",
                schema: "catalog",
                table: "Products",
                column: "ParentProductId",
                principalSchema: "catalog",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Products_ParentProductId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductVariantAttributeUsages",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "ProductVariantValues",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "VariantAttributeOptions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "VariantAttributes",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "IX_Products_OrganizationId_ParentProductId_CombinationKey",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ParentProductId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000151"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000152"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000153"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000154"));

            migrationBuilder.DropColumn(
                name: "Barcode",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "CombinationKey",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HasVariants",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ParentProductId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Sku",
                schema: "catalog",
                table: "Products");
        }
    }
}
