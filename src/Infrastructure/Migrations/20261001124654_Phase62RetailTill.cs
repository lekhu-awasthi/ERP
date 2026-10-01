using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase62RetailTill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AbbreviatedTaxInvoiceEnabled",
                schema: "pos",
                table: "PosLocationSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsAbbreviatedTaxInvoice",
                schema: "sales",
                table: "Invoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "InvoicePrints",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintNumber = table.Column<int>(type: "int", nullable: false),
                    PrintedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoicePrints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoicePrints_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "sales",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrints_InvoiceId",
                schema: "sales",
                table: "InvoicePrints",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoicePrints_OrganizationId_InvoiceId_PrintNumber",
                schema: "sales",
                table: "InvoicePrints",
                columns: new[] { "OrganizationId", "InvoiceId", "PrintNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoicePrints",
                schema: "sales");

            migrationBuilder.DropColumn(
                name: "AbbreviatedTaxInvoiceEnabled",
                schema: "pos",
                table: "PosLocationSettings");

            migrationBuilder.DropColumn(
                name: "IsAbbreviatedTaxInvoice",
                schema: "sales",
                table: "Invoices");
        }
    }
}
