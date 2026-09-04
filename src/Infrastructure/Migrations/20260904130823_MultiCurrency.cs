using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MultiCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultForexGainAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultForexLossAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "sales",
                table: "SalesOrders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "sales",
                table: "SalesOrders",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "sales",
                table: "Quotations",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "sales",
                table: "Quotations",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "payments",
                table: "Payments",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "payments",
                table: "Payments",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "accounting",
                table: "OpeningBalanceLines",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "accounting",
                table: "OpeningBalanceLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "accounting",
                table: "JournalVouchers",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "accounting",
                table: "JournalVouchers",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "sales",
                table: "Invoices",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "Expenses",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "Expenses",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "DebitNotes",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "DebitNotes",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "sales",
                table: "CreditNotes",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "sales",
                table: "CreditNotes",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                schema: "accounting",
                table: "CashTransfers",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "NPR");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                schema: "accounting",
                table: "CashTransfers",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Symbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001a7"), true, "Tenancy.Currency.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001a8"), true, "Tenancy.Currency.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001a9"), true, "Tenancy.Currency.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001aa"), false, "Tenancy.Currency.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Currencies_OrganizationId_Code",
                schema: "tenancy",
                table: "Currencies",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            // Hand-written, and deliberately placed after the unique index so a re-run cannot
            // duplicate a row. Every Organization created from here on is seeded with its base
            // currency by CreateOrganizationCommandHandler; every Organization that already exists
            // needs the same row, because the document columns added above default every existing
            // document to NPR and a tenant whose currency list did not contain NPR would have
            // documents referring to a currency it does not list.
            //
            // The document columns' own backfill needs no statement at all -- their SQL defaults
            // (see the AddColumn calls above) populate every existing row as the column is added,
            // which is exactly why they were given defaults rather than being made nullable.
            migrationBuilder.Sql("""
                INSERT INTO tenancy.Currencies (Id, OrganizationId, Code, Name, Symbol, IsActive, CreatedAt)
                SELECT NEWID(), o.Id, 'NPR', 'Nepalese Rupee', 'Rs.', 1, SYSDATETIMEOFFSET()
                FROM tenancy.Organizations o
                WHERE NOT EXISTS (
                    SELECT 1 FROM tenancy.Currencies c
                    WHERE c.OrganizationId = o.Id AND c.Code = 'NPR');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "tenancy");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001a7"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001a8"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001a9"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001aa"));

            migrationBuilder.DropColumn(
                name: "DefaultForexGainAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DefaultForexLossAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "payments",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "accounting",
                table: "OpeningBalanceLines");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "accounting",
                table: "OpeningBalanceLines");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "accounting",
                table: "JournalVouchers");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                schema: "accounting",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                schema: "accounting",
                table: "CashTransfers");
        }
    }
}
