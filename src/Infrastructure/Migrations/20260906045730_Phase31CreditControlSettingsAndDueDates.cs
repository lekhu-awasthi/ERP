using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase31CreditControlSettingsAndDueDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreditLimitExceedsAction",
                schema: "tenancy",
                table: "TenantSettings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Warn");

            // Phase 31 -- DueDate is non-nullable in the model, and the scaffolded ADD COLUMN
            // therefore wanted a literal 0001-01-01 default on every existing row. That would have
            // silently back-dated every historical invoice and bill to the year 1 and left a stray
            // default constraint behind, so the three steps are written out by hand instead: add it
            // nullable, backfill each row from its OWN Date -- which is exactly the value the ageing
            // reports were already improvising for these two types -- then tighten to NOT NULL.
            // CLAUDE.md's "read any migration that replaces or retypes a column and reorder by hand".
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("UPDATE [purchasing].[PurchaseBills] SET [DueDate] = [Date];");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DueDate",
                schema: "purchasing",
                table: "PurchaseBills",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                schema: "sales",
                table: "Invoices",
                type: "date",
                nullable: true);

            migrationBuilder.Sql("UPDATE [sales].[Invoices] SET [DueDate] = [Date];");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "DueDate",
                schema: "sales",
                table: "Invoices",
                type: "date",
                nullable: false,
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AcceptsReverseTransactions",
                schema: "contacts",
                table: "Contacts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                schema: "contacts",
                table: "Contacts",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "CreditTermId",
                schema: "contacts",
                table: "Contacts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001b1"), true, "Configuration.GeneralSettings.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001b2"), false, "Configuration.GeneralSettings.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001b3"), true, "Tenancy.Subscription.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001b4"), false, "Tenancy.Subscription.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_CreditTermId",
                schema: "contacts",
                table: "Contacts",
                column: "CreditTermId");

            migrationBuilder.AddForeignKey(
                name: "FK_Contacts_CreditTerms_CreditTermId",
                schema: "contacts",
                table: "Contacts",
                column: "CreditTermId",
                principalSchema: "configuration",
                principalTable: "CreditTerms",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Contacts_CreditTerms_CreditTermId",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_CreditTermId",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b1"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b2"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b3"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b4"));

            migrationBuilder.DropColumn(
                name: "CreditLimitExceedsAction",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DueDate",
                schema: "purchasing",
                table: "PurchaseBills");

            migrationBuilder.DropColumn(
                name: "DueDate",
                schema: "sales",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "AcceptsReverseTransactions",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "CreditTermId",
                schema: "contacts",
                table: "Contacts");
        }
    }
}
