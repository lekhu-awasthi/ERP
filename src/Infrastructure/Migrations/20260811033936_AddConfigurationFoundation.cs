using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "configuration");

            migrationBuilder.AddColumn<string>(
                name: "InventoryTrackingMode",
                schema: "tenancy",
                table: "TenantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "AccountingMovement");

            migrationBuilder.AddColumn<string>(
                name: "NegativeCashBalanceAction",
                schema: "tenancy",
                table: "TenantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Reject");

            migrationBuilder.AddColumn<string>(
                name: "NegativeStockBalanceAction",
                schema: "tenancy",
                table: "TenantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Warn");

            migrationBuilder.AddColumn<string>(
                name: "ProductPriceBasis",
                schema: "tenancy",
                table: "TenantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "ExclusiveOfVat");

            migrationBuilder.AddColumn<string>(
                name: "SuggestSellingPriceMode",
                schema: "tenancy",
                table: "TenantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "RecentSellingPrice");

            migrationBuilder.CreateTable(
                name: "CreditTerms",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DueDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldDefinitions",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApplicableDocumentTypes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomStatuses",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomStatuses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentNumberingRules",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Prefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NextNumber = table.Column<int>(type: "int", nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResetEveryFiscalYear = table.Column<bool>(type: "bit", nullable: false),
                    IncludeFiscalYearInCode = table.Column<bool>(type: "bit", nullable: false),
                    LocationWiseNumbering = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentNumberingRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentModes",
                schema: "configuration",
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
                    table.PrimaryKey("PK_PaymentModes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportingTagCategories",
                schema: "configuration",
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
                    table.PrimaryKey("PK_ReportingTagCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldValues",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFieldValues_CustomFieldDefinitions_FieldDefinitionId",
                        column: x => x.FieldDefinitionId,
                        principalSchema: "configuration",
                        principalTable: "CustomFieldDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportingTagOptions",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportingTagOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportingTagOptions_ReportingTagCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "configuration",
                        principalTable: "ReportingTagCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000005"), true, "Configuration.CreditTerm.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000006"), true, "Configuration.CreditTerm.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000007"), true, "Configuration.CreditTerm.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000008"), false, "Configuration.CreditTerm.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000009"), true, "Configuration.PaymentMode.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000000a"), true, "Configuration.PaymentMode.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000000b"), true, "Configuration.PaymentMode.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000000c"), false, "Configuration.PaymentMode.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000000d"), true, "Configuration.CustomStatus.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000000e"), true, "Configuration.CustomStatus.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000000f"), true, "Configuration.CustomStatus.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000010"), false, "Configuration.CustomStatus.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000011"), true, "Configuration.ReportingTagCategory.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000012"), true, "Configuration.ReportingTagCategory.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000013"), true, "Configuration.ReportingTagCategory.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000014"), false, "Configuration.ReportingTagCategory.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000015"), true, "Configuration.ReportingTagOption.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000016"), true, "Configuration.ReportingTagOption.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000017"), true, "Configuration.ReportingTagOption.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000018"), false, "Configuration.ReportingTagOption.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000019"), true, "Configuration.CustomFieldDefinition.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000001a"), true, "Configuration.CustomFieldDefinition.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000001b"), true, "Configuration.CustomFieldDefinition.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000001c"), false, "Configuration.CustomFieldDefinition.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditTerms_OrganizationId_Name",
                schema: "configuration",
                table: "CreditTerms",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldDefinitions_OrganizationId_Name",
                schema: "configuration",
                table: "CustomFieldDefinitions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldValues_DocumentType_DocumentId",
                schema: "configuration",
                table: "CustomFieldValues",
                columns: new[] { "DocumentType", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldValues_FieldDefinitionId",
                schema: "configuration",
                table: "CustomFieldValues",
                column: "FieldDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomStatuses_OrganizationId_DocumentType_Name",
                schema: "configuration",
                table: "CustomStatuses",
                columns: new[] { "OrganizationId", "DocumentType", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentNumberingRules_OrganizationId_DocumentType",
                schema: "configuration",
                table: "DocumentNumberingRules",
                columns: new[] { "OrganizationId", "DocumentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentModes_OrganizationId_Name",
                schema: "configuration",
                table: "PaymentModes",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportingTagCategories_OrganizationId_Name",
                schema: "configuration",
                table: "ReportingTagCategories",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportingTagOptions_CategoryId",
                schema: "configuration",
                table: "ReportingTagOptions",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportingTagOptions_OrganizationId_CategoryId_Name",
                schema: "configuration",
                table: "ReportingTagOptions",
                columns: new[] { "OrganizationId", "CategoryId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditTerms",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "CustomFieldValues",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "CustomStatuses",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "DocumentNumberingRules",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "PaymentModes",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "ReportingTagOptions",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "CustomFieldDefinitions",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "ReportingTagCategories",
                schema: "configuration");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000005"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000006"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000007"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000008"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000009"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000000a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000000b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000000c"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000000d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000000e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000000f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000010"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000011"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000012"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000013"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000014"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000015"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000016"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000017"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000018"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000019"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000001a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000001b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000001c"));

            migrationBuilder.DropColumn(
                name: "InventoryTrackingMode",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "NegativeCashBalanceAction",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "NegativeStockBalanceAction",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "ProductPriceBasis",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "SuggestSellingPriceMode",
                schema: "tenancy",
                table: "TenantSettings");
        }
    }
}
