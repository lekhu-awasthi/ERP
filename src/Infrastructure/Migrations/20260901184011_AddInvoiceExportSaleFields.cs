using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceExportSaleFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExportCountry",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExportDeclarationDate",
                schema: "sales",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExportDeclarationNo",
                schema: "sales",
                table: "Invoices",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsExport",
                schema: "sales",
                table: "Invoices",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExportCountry",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExportDeclarationDate",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExportDeclarationNo",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "IsExport",
                schema: "sales",
                table: "Invoices");
        }
    }
}
