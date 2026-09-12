using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 37 -- the value-only cost catch-up column on the stock movement fact table.
    ///
    /// <para>NOT NULL with a default of zero, and deliberately <b>not</b> backfilled by hand: zero
    /// is what every existing row means, because a movement written before this phase carried its
    /// whole value in quantity times unit cost. That is what separates this from phase 31's stored
    /// DueDate, where the scaffold's own default would have back-dated real data -- the rule is
    /// that a default is safe exactly when it is the truth about the rows already there.</para>
    /// </summary>
    public partial class AddStockMovementValueAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ValueAdjustment",
                schema: "inventory",
                table: "StockMovements",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValueAdjustment",
                schema: "inventory",
                table: "StockMovements");
        }
    }
}
