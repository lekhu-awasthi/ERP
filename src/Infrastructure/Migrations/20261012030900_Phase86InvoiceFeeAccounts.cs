using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase86InvoiceFeeAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PackagingFeeAccountId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceChargeAccountId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PackagingFeeAccountId",
                schema: "sales",
                table: "Invoices",
                column: "PackagingFeeAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ServiceChargeAccountId",
                schema: "sales",
                table: "Invoices",
                column: "ServiceChargeAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Accounts_PackagingFeeAccountId",
                schema: "sales",
                table: "Invoices",
                column: "PackagingFeeAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Accounts_ServiceChargeAccountId",
                schema: "sales",
                table: "Invoices",
                column: "ServiceChargeAccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Accounts_PackagingFeeAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Accounts_ServiceChargeAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PackagingFeeAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ServiceChargeAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PackagingFeeAccountId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ServiceChargeAccountId",
                schema: "sales",
                table: "Invoices");
        }
    }
}
