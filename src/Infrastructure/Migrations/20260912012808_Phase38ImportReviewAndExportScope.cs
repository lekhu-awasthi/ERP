using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase38ImportReviewAndExportScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "imports",
                table: "ImportJobs",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<bool>(
                name: "ReviewBeforeApply",
                schema: "imports",
                table: "ImportJobs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewConfirmedAt",
                schema: "imports",
                table: "ImportJobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Categories",
                schema: "exports",
                table: "ExportJobs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "FromDate",
                schema: "exports",
                table: "ExportJobs",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ToDate",
                schema: "exports",
                table: "ExportJobs",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewBeforeApply",
                schema: "imports",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "ReviewConfirmedAt",
                schema: "imports",
                table: "ImportJobs");

            migrationBuilder.DropColumn(
                name: "Categories",
                schema: "exports",
                table: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "FromDate",
                schema: "exports",
                table: "ExportJobs");

            migrationBuilder.DropColumn(
                name: "ToDate",
                schema: "exports",
                table: "ExportJobs");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "imports",
                table: "ImportJobs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);
        }
    }
}
