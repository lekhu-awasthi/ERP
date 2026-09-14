using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase41SubscriptionPlansAndQuotas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IrdVerified",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductQuota",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "SubscriptionAmount",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Phase 41, hand-written: the scaffold produced
            //   TermStartsAt datetimeoffset NOT NULL DEFAULT '0001-01-01'
            // which would back-date every existing tenant's current term to the year 1 and leave a
            // stray default constraint behind -- phase 31's DueDate trap exactly. Phase 37 refined
            // that rule: a default is safe precisely when it is the truth about the rows already
            // there, and this one is not. The truth for an existing row is TrialStartsAt: no tenant
            // has yet renewed under this phase, so its current term is the one it began with. The
            // other five columns added here keep their scaffolded defaults, because for those the
            // default IS the truth -- every pre-existing tenant is on no plan (null), charged
            // nothing (0), unmetered on both axes (0, the "no limit" sentinel) and not IRD-verified.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TermStartsAt",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [tenancy].[TenantSubscriptions] SET [TermStartsAt] = [TrialStartsAt] WHERE [TermStartsAt] IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "TermStartsAt",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TransactionQuota",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    AnnualAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProductQuota = table.Column<int>(type: "int", nullable: false),
                    TransactionQuota = table.Column<int>(type: "int", nullable: false),
                    TrackInventoryIncluded = table.Column<bool>(type: "bit", nullable: false),
                    MultipleWarehousesIncluded = table.Column<bool>(type: "bit", nullable: false),
                    LandedCostIncluded = table.Column<bool>(type: "bit", nullable: false),
                    ManufacturingIncluded = table.Column<bool>(type: "bit", nullable: false),
                    PosIncluded = table.Column<bool>(type: "bit", nullable: false),
                    MultiCurrencyIncluded = table.Column<bool>(type: "bit", nullable: false),
                    DeveloperApiIncluded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "SubscriptionPlans",
                columns: new[] { "Id", "AnnualAmount", "Code", "Description", "DeveloperApiIncluded", "DisplayOrder", "LandedCostIncluded", "ManufacturingIncluded", "MultiCurrencyIncluded", "MultipleWarehousesIncluded", "Name", "PosIncluded", "ProductQuota", "TrackInventoryIncluded", "TransactionQuota" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0003-000000000001"), 15000m, "Basic", "Best for service based businesses that require basic accounting", false, 1, false, false, true, false, "Basic", false, 1000, false, 30000 },
                    { new Guid("00000000-0000-0000-0003-000000000002"), 20000m, "Standard", "Best for SME organizations that require accounting & inventory tracking", false, 2, true, false, true, true, "Standard", false, 5000, true, 50000 },
                    { new Guid("00000000-0000-0000-0003-000000000003"), 32000m, "Professional", "Best for Retail/Restaurants that require POS along with accounting & inventory tracking", true, 3, true, true, true, true, "Professional", true, 10000, true, 200000 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_PlanId",
                schema: "tenancy",
                table: "TenantSubscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_Code",
                schema: "tenancy",
                table: "SubscriptionPlans",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TenantSubscriptions_SubscriptionPlans_PlanId",
                schema: "tenancy",
                table: "TenantSubscriptions",
                column: "PlanId",
                principalSchema: "tenancy",
                principalTable: "SubscriptionPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TenantSubscriptions_SubscriptionPlans_PlanId",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans",
                schema: "tenancy");

            migrationBuilder.DropIndex(
                name: "IX_TenantSubscriptions_PlanId",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "IrdVerified",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "PlanId",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "ProductQuota",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "SubscriptionAmount",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "TermStartsAt",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "TransactionQuota",
                schema: "tenancy",
                table: "TenantSubscriptions");
        }
    }
}
