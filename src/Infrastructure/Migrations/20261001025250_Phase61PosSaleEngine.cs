using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase61PosSaleEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ChangeAmount",
                schema: "sales",
                table: "Invoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Erp");

            migrationBuilder.AddColumn<string>(
                name: "OrderType",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosSessionId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundOff",
                schema: "sales",
                table: "Invoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeAmount",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeRate",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "InvoiceTenders",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceTenders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceTenders_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InvoiceTenders_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InvoiceTenders_PaymentModes_PaymentModeId",
                        column: x => x.PaymentModeId,
                        principalSchema: "configuration",
                        principalTable: "PaymentModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosSessions",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BillingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OpeningFloat = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    OpeningCount = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CountedCash = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ClosingCount = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CashDifference = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    ClosingNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosSessions_Accounts_CashAccountId",
                        column: x => x.CashAccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosSessions_BillingLocations_BillingLocationId",
                        column: x => x.BillingLocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosCashMovements",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PosSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosCashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosCashMovements_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosCashMovements_PosSessions_PosSessionId",
                        column: x => x.PosSessionId,
                        principalSchema: "pos",
                        principalTable: "PosSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "LocationId", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001e3"), true, null, "Pos.Session.Operate", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001e4"), true, null, "Pos.Session.Operate", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001e5"), true, null, "Pos.Session.ViewAll", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001e6"), false, null, "Pos.Session.ViewAll", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PosSessionId",
                schema: "sales",
                table: "Invoices",
                column: "PosSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTenders_AccountId",
                schema: "sales",
                table: "InvoiceTenders",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTenders_InvoiceId",
                schema: "sales",
                table: "InvoiceTenders",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceTenders_PaymentModeId",
                schema: "sales",
                table: "InvoiceTenders",
                column: "PaymentModeId");

            migrationBuilder.CreateIndex(
                name: "IX_PosCashMovements_AccountId",
                schema: "pos",
                table: "PosCashMovements",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PosCashMovements_PosSessionId",
                schema: "pos",
                table: "PosCashMovements",
                column: "PosSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSessions_BillingLocationId",
                schema: "pos",
                table: "PosSessions",
                column: "BillingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSessions_CashAccountId",
                schema: "pos",
                table: "PosSessions",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PosSessions_OrganizationId_BillingLocationId_OpenedAt",
                schema: "pos",
                table: "PosSessions",
                columns: new[] { "OrganizationId", "BillingLocationId", "OpenedAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PosSessions_OrganizationId_BillingLocationId_UserId",
                schema: "pos",
                table: "PosSessions",
                columns: new[] { "OrganizationId", "BillingLocationId", "UserId" },
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_PosSessions_PosSessionId",
                schema: "sales",
                table: "Invoices",
                column: "PosSessionId",
                principalSchema: "pos",
                principalTable: "PosSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_PosSessions_PosSessionId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "InvoiceTenders",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "PosCashMovements",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosSessions",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PosSessionId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e3"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e4"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e5"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e6"));

            migrationBuilder.DropColumn(
                name: "ChangeAmount",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "OrderType",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PosSessionId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "RoundOff",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ServiceChargeAmount",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "ServiceChargeRate",
                schema: "sales",
                table: "InvoiceLines");
        }
    }
}
