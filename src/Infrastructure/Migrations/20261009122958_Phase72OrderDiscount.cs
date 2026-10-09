using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase72OrderDiscount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "pos",
                table: "PosOrders",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceChargeWaived",
                schema: "pos",
                table: "PosOrders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountPct",
                schema: "pos",
                table: "PosOrderLines",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "pos",
                table: "PosOrders");

            migrationBuilder.DropColumn(
                name: "ServiceChargeWaived",
                schema: "pos",
                table: "PosOrders");

            migrationBuilder.DropColumn(
                name: "DiscountPct",
                schema: "pos",
                table: "PosOrderLines");
        }
    }
}
