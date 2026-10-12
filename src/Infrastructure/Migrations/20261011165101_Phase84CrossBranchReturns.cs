using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase84CrossBranchReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WarehouseId",
                schema: "sales",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_WarehouseId",
                schema: "sales",
                table: "CreditNotes",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_Warehouses_WarehouseId",
                schema: "sales",
                table: "CreditNotes",
                column: "WarehouseId",
                principalSchema: "tenancy",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_Warehouses_WarehouseId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_WarehouseId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                schema: "sales",
                table: "CreditNotes");
        }
    }
}
