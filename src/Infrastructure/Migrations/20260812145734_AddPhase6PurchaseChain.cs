using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase6PurchaseChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "purchasing");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultAccountsPayableId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultPurchaseAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultTdsPayableAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultVatReceivableAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DebitNotes",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ReferrerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReferrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebitNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebitNotes_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TdsTypes",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RatePct = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TdsTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DebitNoteLines",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DebitNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebitNoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebitNoteLines_DebitNotes_DebitNoteId",
                        column: x => x.DebitNoteId,
                        principalSchema: "purchasing",
                        principalTable: "DebitNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DebitNoteLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SupplierInvoiceReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TdsApplicable = table.Column<bool>(type: "bit", nullable: false),
                    TdsTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TdsAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Expenses_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_TdsTypes_TdsTypeId",
                        column: x => x.TdsTypeId,
                        principalSchema: "configuration",
                        principalTable: "TdsTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseBills",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupplierInvoiceReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsImport = table.Column<bool>(type: "bit", nullable: false),
                    ImportCountry = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImportDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ImportDocumentNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TdsTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TdsAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    ReferrerType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ReferrerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseBills_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseBills_TdsTypes_TdsTypeId",
                        column: x => x.TdsTypeId,
                        principalSchema: "configuration",
                        principalTable: "TdsTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseBills_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "tenancy",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseLines",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseLines_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseLines_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalSchema: "purchasing",
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseBillLines",
                schema: "purchasing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseBillId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatRate = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpenditureClassification = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseBillLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseBillLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalSchema: "catalog",
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseBillLines_PurchaseBills_PurchaseBillId",
                        column: x => x.PurchaseBillId,
                        principalSchema: "purchasing",
                        principalTable: "PurchaseBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000077"), true, "Configuration.TdsType.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000078"), true, "Configuration.TdsType.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000079"), true, "Configuration.TdsType.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000007a"), false, "Configuration.TdsType.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000007b"), true, "Purchasing.PurchaseOrder.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000007c"), true, "Purchasing.PurchaseOrder.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000007d"), true, "Purchasing.PurchaseOrder.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000007e"), true, "Purchasing.PurchaseOrder.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000007f"), true, "Purchasing.PurchaseOrder.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000080"), true, "Purchasing.PurchaseOrder.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000081"), true, "Purchasing.PurchaseOrder.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000082"), false, "Purchasing.PurchaseOrder.Approve", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000083"), true, "Purchasing.PurchaseBill.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000084"), true, "Purchasing.PurchaseBill.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000085"), true, "Purchasing.PurchaseBill.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000086"), true, "Purchasing.PurchaseBill.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000087"), true, "Purchasing.PurchaseBill.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000088"), true, "Purchasing.PurchaseBill.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000089"), true, "Purchasing.PurchaseBill.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000008a"), false, "Purchasing.PurchaseBill.Approve", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000008b"), true, "Purchasing.Expense.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000008c"), true, "Purchasing.Expense.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000008d"), true, "Purchasing.Expense.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000008e"), true, "Purchasing.Expense.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000008f"), true, "Purchasing.Expense.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000090"), true, "Purchasing.Expense.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000091"), true, "Purchasing.Expense.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000092"), false, "Purchasing.Expense.Approve", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000093"), true, "Purchasing.DebitNote.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000094"), true, "Purchasing.DebitNote.Create", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000095"), true, "Purchasing.DebitNote.Edit", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000096"), true, "Purchasing.DebitNote.Approve", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000097"), true, "Purchasing.DebitNote.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000098"), true, "Purchasing.DebitNote.Create", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000099"), true, "Purchasing.DebitNote.Edit", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000009a"), false, "Purchasing.DebitNote.Approve", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_DefaultAccountsPayableId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultAccountsPayableId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_DefaultPurchaseAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultPurchaseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_DefaultTdsPayableAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultTdsPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_DefaultVatReceivableAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultVatReceivableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNoteLines_DebitNoteId",
                schema: "purchasing",
                table: "DebitNoteLines",
                column: "DebitNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNoteLines_ProductId",
                schema: "purchasing",
                table: "DebitNoteLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_ContactId",
                schema: "purchasing",
                table: "DebitNotes",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_AccountId",
                schema: "purchasing",
                table: "ExpenseLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseLines_ExpenseId",
                schema: "purchasing",
                table: "ExpenseLines",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ContactId",
                schema: "purchasing",
                table: "Expenses",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TdsTypeId",
                schema: "purchasing",
                table: "Expenses",
                column: "TdsTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillLines_ProductId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBillLines_PurchaseBillId",
                schema: "purchasing",
                table: "PurchaseBillLines",
                column: "PurchaseBillId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBills_ContactId",
                schema: "purchasing",
                table: "PurchaseBills",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBills_TdsTypeId",
                schema: "purchasing",
                table: "PurchaseBills",
                column: "TdsTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseBills_WarehouseId",
                schema: "purchasing",
                table: "PurchaseBills",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProductId",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                schema: "purchasing",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_ContactId",
                schema: "purchasing",
                table: "PurchaseOrders",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_TdsTypes_OrganizationId_Code",
                schema: "configuration",
                table: "TdsTypes",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultAccountsPayableId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultAccountsPayableId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultPurchaseAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultPurchaseAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultTdsPayableAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultTdsPayableAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultVatReceivableAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "DefaultVatReceivableAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultAccountsPayableId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultPurchaseAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultTdsPayableAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropForeignKey(
                name: "FK_TenantSettings_Accounts_DefaultVatReceivableAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropTable(
                name: "DebitNoteLines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "ExpenseLines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseBillLines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "DebitNotes",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "Expenses",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseBills",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "PurchaseOrders",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "TdsTypes",
                schema: "configuration");

            migrationBuilder.DropIndex(
                name: "IX_TenantSettings_DefaultAccountsPayableId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropIndex(
                name: "IX_TenantSettings_DefaultPurchaseAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropIndex(
                name: "IX_TenantSettings_DefaultTdsPayableAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropIndex(
                name: "IX_TenantSettings_DefaultVatReceivableAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000077"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000078"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000079"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000007a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000007b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000007c"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000007d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000007e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000007f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000080"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000081"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000082"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000083"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000084"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000085"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000086"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000087"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000088"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000089"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000008a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000008b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000008c"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000008d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000008e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000008f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000090"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000091"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000092"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000093"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000094"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000095"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000096"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000097"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000098"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000099"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000009a"));

            migrationBuilder.DropColumn(
                name: "DefaultAccountsPayableId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPurchaseAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DefaultTdsPayableAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DefaultVatReceivableAccountId",
                schema: "tenancy",
                table: "TenantSettings");
        }
    }
}
