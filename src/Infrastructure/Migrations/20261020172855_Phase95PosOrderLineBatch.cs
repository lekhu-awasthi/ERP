using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase95PosOrderLineBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BatchId",
                schema: "pos",
                table: "PosOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosOrderLines_BatchId",
                schema: "pos",
                table: "PosOrderLines",
                column: "BatchId");

            migrationBuilder.AddForeignKey(
                name: "FK_PosOrderLines_ProductBatches_BatchId",
                schema: "pos",
                table: "PosOrderLines",
                column: "BatchId",
                principalSchema: "catalog",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PosOrderLines_ProductBatches_BatchId",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PosOrderLines_BatchId",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropColumn(
                name: "BatchId",
                schema: "pos",
                table: "PosOrderLines");
        }
    }
}
