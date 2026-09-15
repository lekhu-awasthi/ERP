using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase46MeteredAxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // HAND-WRITTEN, and the scaffold had it wrong in the way phase 31's DueDate and phase
            // 41's TermStartsAt were wrong -- the same trap a third time, with a new symptom.
            //
            // `dotnet ef` emitted `nullable: false, defaultValue: 0`. Zero on this column does not
            // mean "no scans yet"; it is the NOT-METERED sentinel, so every tenant that already
            // exists would have come out of this migration with an UNLIMITED daily AI-scan
            // allowance, and the ceiling this phase exists to add would have applied to nobody but
            // tenants created afterwards. Nothing would have failed, no test would have caught it,
            // and the only evidence would have been the API bill.
            //
            // Phase 37's refinement of the rule decides it: a scaffolded default is safe exactly
            // when it is already the truth about the rows that are there. For LocationQuota below it
            // is -- no existing tenant has recorded a purchased location count, so 0 is a fact. For
            // this column the truth is 20, the allowance every published tier grants and the same
            // figure TenantSubscription.DefaultDailyAiScanQuota seeds onto a new trial. So: add
            // nullable, backfill every row, then tighten to NOT NULL -- which also leaves no stray
            // default constraint behind for a later insert to lean on.
            migrationBuilder.AddColumn<int>(
                name: "DailyAiScanQuota",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE [tenancy].[TenantSubscriptions] SET [DailyAiScanQuota] = 20 WHERE [DailyAiScanQuota] IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "DailyAiScanQuota",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            // Scaffolded default kept, deliberately: 0 purchased locations is true of every row that
            // already exists, because nothing could have recorded one before this migration.
            migrationBuilder.AddColumn<int>(
                name: "LocationQuota",
                schema: "tenancy",
                table: "TenantSubscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DailyAiScanQuota",
                schema: "tenancy",
                table: "SubscriptionPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                schema: "tenancy",
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000001"),
                column: "DailyAiScanQuota",
                value: 20);

            migrationBuilder.UpdateData(
                schema: "tenancy",
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000002"),
                column: "DailyAiScanQuota",
                value: 20);

            migrationBuilder.UpdateData(
                schema: "tenancy",
                table: "SubscriptionPlans",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0003-000000000003"),
                column: "DailyAiScanQuota",
                value: 20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyAiScanQuota",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "LocationQuota",
                schema: "tenancy",
                table: "TenantSubscriptions");

            migrationBuilder.DropColumn(
                name: "DailyAiScanQuota",
                schema: "tenancy",
                table: "SubscriptionPlans");
        }
    }
}
