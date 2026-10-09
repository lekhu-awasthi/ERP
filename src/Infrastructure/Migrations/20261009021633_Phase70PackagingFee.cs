using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase70PackagingFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultPackagingFeeAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PackagingFeeApplicable",
                schema: "catalog",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingFeeBasis",
                schema: "pos",
                table: "PosOrderLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingFeeValue",
                schema: "pos",
                table: "PosOrderLines",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PackagingFeeAccountId",
                schema: "pos",
                table: "PosLocationSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackagingFeeBasis",
                schema: "pos",
                table: "PosLocationSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingFeeValue",
                schema: "pos",
                table: "PosLocationSettings",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingFeeAmount",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PackagingFeeBasis",
                schema: "sales",
                table: "InvoiceLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingFeeValue",
                schema: "sales",
                table: "InvoiceLines",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingFeeAmount",
                schema: "sales",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PackagingFeeBasis",
                schema: "sales",
                table: "CreditNoteLines",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<decimal>(
                name: "PackagingFeeValue",
                schema: "sales",
                table: "CreditNoteLines",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationSettings_PackagingFeeAccountId",
                schema: "pos",
                table: "PosLocationSettings",
                column: "PackagingFeeAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_PosLocationSettings_Accounts_PackagingFeeAccountId",
                schema: "pos",
                table: "PosLocationSettings",
                column: "PackagingFeeAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PosLocationSettings_Accounts_PackagingFeeAccountId",
                schema: "pos",
                table: "PosLocationSettings");

            migrationBuilder.DropIndex(
                name: "IX_PosLocationSettings_PackagingFeeAccountId",
                schema: "pos",
                table: "PosLocationSettings");

            migrationBuilder.DropColumn(
                name: "DefaultPackagingFeeAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "PackagingFeeApplicable",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackagingFeeBasis",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropColumn(
                name: "PackagingFeeValue",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropColumn(
                name: "PackagingFeeAccountId",
                schema: "pos",
                table: "PosLocationSettings");

            migrationBuilder.DropColumn(
                name: "PackagingFeeBasis",
                schema: "pos",
                table: "PosLocationSettings");

            migrationBuilder.DropColumn(
                name: "PackagingFeeValue",
                schema: "pos",
                table: "PosLocationSettings");

            migrationBuilder.DropColumn(
                name: "PackagingFeeAmount",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "PackagingFeeBasis",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "PackagingFeeValue",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "PackagingFeeAmount",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "PackagingFeeBasis",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "PackagingFeeValue",
                schema: "sales",
                table: "CreditNoteLines");
        }
    }
}
