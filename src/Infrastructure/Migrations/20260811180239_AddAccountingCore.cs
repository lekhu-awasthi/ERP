using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "accounting");

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseAccountId",
                schema: "catalog",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseReturnAccountId",
                schema: "catalog",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesAccountId",
                schema: "catalog",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesReturnAccountId",
                schema: "catalog",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountGroups",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RootType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ParentGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountGroups_AccountGroups_ParentGroupId",
                        column: x => x.ParentGroupId,
                        principalSchema: "accounting",
                        principalTable: "AccountGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GlJournalEntries",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceDocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlJournalEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JournalVouchers",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalVouchers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Accounts",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RootType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Accounts_AccountGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "accounting",
                        principalTable: "AccountGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashTransfers",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FromAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashTransfers_Accounts_FromAccountId",
                        column: x => x.FromAccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GlLines",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GlJournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GlLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GlLines_GlJournalEntries_GlJournalEntryId",
                        column: x => x.GlJournalEntryId,
                        principalSchema: "accounting",
                        principalTable: "GlJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalVoucherLines",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalVoucherId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalVoucherLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalVoucherLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalVoucherLines_JournalVouchers_JournalVoucherId",
                        column: x => x.JournalVoucherId,
                        principalSchema: "accounting",
                        principalTable: "JournalVouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CashTransferLines",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransferLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashTransferLines_Accounts_ToAccountId",
                        column: x => x.ToAccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransferLines_CashTransfers_CashTransferId",
                        column: x => x.CashTransferId,
                        principalSchema: "accounting",
                        principalTable: "CashTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000031"), true, "Accounting.AccountGroup.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000032"), true, "Accounting.AccountGroup.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000033"), true, "Accounting.AccountGroup.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000034"), false, "Accounting.AccountGroup.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000035"), true, "Accounting.Account.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000036"), true, "Accounting.Account.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000037"), true, "Accounting.Account.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000038"), false, "Accounting.Account.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000039"), true, "Accounting.JournalVoucher.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000003a"), true, "Accounting.JournalVoucher.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000003b"), true, "Accounting.JournalVoucher.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000003c"), true, "Accounting.JournalVoucher.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000003d"), true, "Accounting.JournalVoucher.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000003e"), true, "Accounting.JournalVoucher.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000003f"), true, "Accounting.JournalVoucher.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000040"), false, "Accounting.JournalVoucher.Approve", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000041"), true, "Accounting.CashTransfer.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000042"), true, "Accounting.CashTransfer.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000043"), true, "Accounting.CashTransfer.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000044"), true, "Accounting.CashTransfer.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000045"), true, "Accounting.CashTransfer.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000046"), true, "Accounting.CashTransfer.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000047"), true, "Accounting.CashTransfer.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000048"), false, "Accounting.CashTransfer.Approve", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_PurchaseAccountId",
                schema: "catalog",
                table: "Products",
                column: "PurchaseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_PurchaseReturnAccountId",
                schema: "catalog",
                table: "Products",
                column: "PurchaseReturnAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SalesAccountId",
                schema: "catalog",
                table: "Products",
                column: "SalesAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SalesReturnAccountId",
                schema: "catalog",
                table: "Products",
                column: "SalesReturnAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_OrganizationId_Name",
                schema: "accounting",
                table: "AccountGroups",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountGroups_ParentGroupId",
                schema: "accounting",
                table: "AccountGroups",
                column: "ParentGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_GroupId",
                schema: "accounting",
                table: "Accounts",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_OrganizationId_Code",
                schema: "accounting",
                table: "Accounts",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_OrganizationId_Name",
                schema: "accounting",
                table: "Accounts",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashTransferLines_CashTransferId",
                schema: "accounting",
                table: "CashTransferLines",
                column: "CashTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransferLines_ToAccountId",
                schema: "accounting",
                table: "CashTransferLines",
                column: "ToAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransfers_FromAccountId",
                schema: "accounting",
                table: "CashTransfers",
                column: "FromAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GlJournalEntries_SourceDocumentType_SourceDocumentId",
                schema: "accounting",
                table: "GlJournalEntries",
                columns: new[] { "SourceDocumentType", "SourceDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_GlLines_AccountId",
                schema: "accounting",
                table: "GlLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_GlLines_GlJournalEntryId",
                schema: "accounting",
                table: "GlLines",
                column: "GlJournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalVoucherLines_AccountId",
                schema: "accounting",
                table: "JournalVoucherLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalVoucherLines_JournalVoucherId",
                schema: "accounting",
                table: "JournalVoucherLines",
                column: "JournalVoucherId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Accounts_PurchaseAccountId",
                schema: "catalog",
                table: "Products",
                column: "PurchaseAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Accounts_PurchaseReturnAccountId",
                schema: "catalog",
                table: "Products",
                column: "PurchaseReturnAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Accounts_SalesAccountId",
                schema: "catalog",
                table: "Products",
                column: "SalesAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Accounts_SalesReturnAccountId",
                schema: "catalog",
                table: "Products",
                column: "SalesReturnAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Accounts_PurchaseAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Accounts_PurchaseReturnAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Accounts_SalesAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Accounts_SalesReturnAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropTable(
                name: "CashTransferLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "GlLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "JournalVoucherLines",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "CashTransfers",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "GlJournalEntries",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "JournalVouchers",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "Accounts",
                schema: "accounting");

            migrationBuilder.DropTable(
                name: "AccountGroups",
                schema: "accounting");

            migrationBuilder.DropIndex(
                name: "IX_Products_PurchaseAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_PurchaseReturnAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SalesAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SalesReturnAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000031"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000032"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000033"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000034"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000035"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000036"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000037"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000038"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000039"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000003a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000003b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000003c"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000003d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000003e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000003f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000040"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000041"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000042"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000043"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000044"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000045"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000046"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000047"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000048"));

            migrationBuilder.DropColumn(
                name: "PurchaseAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PurchaseReturnAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalesAccountId",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalesReturnAccountId",
                schema: "catalog",
                table: "Products");
        }
    }
}
