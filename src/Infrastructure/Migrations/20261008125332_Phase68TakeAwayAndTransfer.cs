using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase68TakeAwayAndTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTakeAway",
                schema: "pos",
                table: "PosOrderLines",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParcelledFromLineId",
                schema: "pos",
                table: "PosOrderLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceChargeOnTakeAway",
                schema: "pos",
                table: "PosLocationSettings",
                type: "bit",
                nullable: false,
                // Hand-edited: every existing location keeps today's behaviour (a parcelled quantity
                // keeps its service charge), which is On. The scaffold's false would switch it off.
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CounterpartOrderId",
                schema: "pos",
                table: "KitchenTickets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "pos",
                table: "KitchenTickets",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                // Hand-edited: every existing ticket is a send or a cancellation, and a cancellation is
                // exactly the ticket with a reason (phase 64's invariant), so the backfill is exact.
                defaultValue: "Send");

            migrationBuilder.Sql(
                "UPDATE [pos].[KitchenTickets] SET [Kind] = 'Cancellation' WHERE [Reason] IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_PosOrderLines_ParcelledFromLineId",
                schema: "pos",
                table: "PosOrderLines",
                column: "ParcelledFromLineId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenTickets_CounterpartOrderId",
                schema: "pos",
                table: "KitchenTickets",
                column: "CounterpartOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_KitchenTickets_PosOrders_CounterpartOrderId",
                schema: "pos",
                table: "KitchenTickets",
                column: "CounterpartOrderId",
                principalSchema: "pos",
                principalTable: "PosOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PosOrderLines_PosOrderLines_ParcelledFromLineId",
                schema: "pos",
                table: "PosOrderLines",
                column: "ParcelledFromLineId",
                principalSchema: "pos",
                principalTable: "PosOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KitchenTickets_PosOrders_CounterpartOrderId",
                schema: "pos",
                table: "KitchenTickets");

            migrationBuilder.DropForeignKey(
                name: "FK_PosOrderLines_PosOrderLines_ParcelledFromLineId",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_PosOrderLines_ParcelledFromLineId",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropIndex(
                name: "IX_KitchenTickets_CounterpartOrderId",
                schema: "pos",
                table: "KitchenTickets");

            migrationBuilder.DropColumn(
                name: "IsTakeAway",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropColumn(
                name: "ParcelledFromLineId",
                schema: "pos",
                table: "PosOrderLines");

            migrationBuilder.DropColumn(
                name: "ServiceChargeOnTakeAway",
                schema: "pos",
                table: "PosLocationSettings");

            migrationBuilder.DropColumn(
                name: "CounterpartOrderId",
                schema: "pos",
                table: "KitchenTickets");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "pos",
                table: "KitchenTickets");
        }
    }
}
