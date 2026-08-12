using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDebitNoteTds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TdsAmount",
                schema: "purchasing",
                table: "DebitNotes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "TdsTypeId",
                schema: "purchasing",
                table: "DebitNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_TdsTypeId",
                schema: "purchasing",
                table: "DebitNotes",
                column: "TdsTypeId");

            migrationBuilder.AddForeignKey(
                name: "FK_DebitNotes_TdsTypes_TdsTypeId",
                schema: "purchasing",
                table: "DebitNotes",
                column: "TdsTypeId",
                principalSchema: "configuration",
                principalTable: "TdsTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DebitNotes_TdsTypes_TdsTypeId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropIndex(
                name: "IX_DebitNotes_TdsTypeId",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "TdsAmount",
                schema: "purchasing",
                table: "DebitNotes");

            migrationBuilder.DropColumn(
                name: "TdsTypeId",
                schema: "purchasing",
                table: "DebitNotes");
        }
    }
}
