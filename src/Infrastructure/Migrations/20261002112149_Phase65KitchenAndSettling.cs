using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase65KitchenAndSettling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SettledAt",
                schema: "pos",
                table: "PosOrders",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosOrderId",
                schema: "sales",
                table: "Invoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PosOrderLineId",
                schema: "sales",
                table: "InvoiceLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "LocationId", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001ef"), true, null, "Pos.Kitchen.Operate", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001f0"), true, null, "Pos.Kitchen.Operate", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PosOrderId",
                schema: "sales",
                table: "Invoices",
                column: "PosOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_PosOrderLineId",
                schema: "sales",
                table: "InvoiceLines",
                column: "PosOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_InvoiceLines_PosOrderLines_PosOrderLineId",
                schema: "sales",
                table: "InvoiceLines",
                column: "PosOrderLineId",
                principalSchema: "pos",
                principalTable: "PosOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_PosOrders_PosOrderId",
                schema: "sales",
                table: "Invoices",
                column: "PosOrderId",
                principalSchema: "pos",
                principalTable: "PosOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InvoiceLines_PosOrderLines_PosOrderLineId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_PosOrders_PosOrderId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_PosOrderId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_PosOrderLineId",
                schema: "sales",
                table: "InvoiceLines");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ef"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001f0"));

            migrationBuilder.DropColumn(
                name: "SettledAt",
                schema: "pos",
                table: "PosOrders");

            migrationBuilder.DropColumn(
                name: "PosOrderId",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PosOrderLineId",
                schema: "sales",
                table: "InvoiceLines");
        }
    }
}
