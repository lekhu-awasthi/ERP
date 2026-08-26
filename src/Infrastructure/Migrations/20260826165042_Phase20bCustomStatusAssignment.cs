using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase20bCustomStatusAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomStatusId",
                schema: "sales",
                table: "Quotations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomStatusId",
                schema: "purchasing",
                table: "PurchaseOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_CustomStatusId",
                schema: "sales",
                table: "Quotations",
                column: "CustomStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_CustomStatusId",
                schema: "purchasing",
                table: "PurchaseOrders",
                column: "CustomStatusId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_CustomStatuses_CustomStatusId",
                schema: "purchasing",
                table: "PurchaseOrders",
                column: "CustomStatusId",
                principalSchema: "configuration",
                principalTable: "CustomStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Quotations_CustomStatuses_CustomStatusId",
                schema: "sales",
                table: "Quotations",
                column: "CustomStatusId",
                principalSchema: "configuration",
                principalTable: "CustomStatuses",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_CustomStatuses_CustomStatusId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Quotations_CustomStatuses_CustomStatusId",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_Quotations_CustomStatusId",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_CustomStatusId",
                schema: "purchasing",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "CustomStatusId",
                schema: "sales",
                table: "Quotations");

            migrationBuilder.DropColumn(
                name: "CustomStatusId",
                schema: "purchasing",
                table: "PurchaseOrders");
        }
    }
}
