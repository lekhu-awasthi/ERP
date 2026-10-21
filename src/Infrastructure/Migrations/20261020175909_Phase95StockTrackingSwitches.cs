using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase95StockTrackingSwitches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BatchTrackingEnabled",
                schema: "tenancy",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SerialTrackingEnabled",
                schema: "tenancy",
                table: "TenantSettings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Phase 95, hand-written: the default (off) is false of a company already tracking a product, whose
            // tracking the new switch would silently stop (phase 31's rule for a NOT NULL column on populated rows). Such
            // a company starts with its switch on, so nothing it does changes; every other company starts off, as a new
            // one does.
            migrationBuilder.Sql(@"
UPDATE s SET s.BatchTrackingEnabled = 1
FROM tenancy.TenantSettings s
WHERE EXISTS (SELECT 1 FROM catalog.Products p WHERE p.OrganizationId = s.OrganizationId AND p.BatchTracking = 1);

UPDATE s SET s.SerialTrackingEnabled = 1
FROM tenancy.TenantSettings s
WHERE EXISTS (SELECT 1 FROM catalog.Products p WHERE p.OrganizationId = s.OrganizationId AND p.SerialTracking = 1);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BatchTrackingEnabled",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "SerialTrackingEnabled",
                schema: "tenancy",
                table: "TenantSettings");
        }
    }
}
