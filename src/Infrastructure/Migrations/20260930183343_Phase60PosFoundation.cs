using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase60PosFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pos");

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultCashOverShortAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultRoundingAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultServiceChargeAccountId",
                schema: "tenancy",
                table: "TenantSettings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ServiceChargeApplicable",
                schema: "catalog",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "AccountId",
                schema: "configuration",
                table: "PaymentModes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "configuration",
                table: "PaymentModes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Other");

            migrationBuilder.AddColumn<bool>(
                name: "IsWalkInCustomer",
                schema: "contacts",
                table: "Contacts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PosMode",
                schema: "tenancy",
                table: "BillingLocations",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            // Phase 60, hand-written -- retiring BillingLocationType.PosRestaurant (3) and PosRetail
            // (4). Every existing row takes PosMode 'None' from the column default above
            // (phase-60-status.md Decision A). No row held 3 or 4 on the dev database when this was
            // written (192 HeadOffice, 20 Standard) and nothing could create one, but a database
            // that did would otherwise hold an enum value the model no longer has; so any such row
            // becomes an ordinary Standard location carrying the mode its old type meant.
            migrationBuilder.Sql(@"
UPDATE [tenancy].[BillingLocations]
SET [PosMode] = CASE [LocationType] WHEN 3 THEN N'Restaurant' ELSE N'Retail' END,
    [LocationType] = 2
WHERE [LocationType] IN (3, 4);");

            migrationBuilder.CreateTable(
                name: "PosLocationPaymentModes",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosLocationPaymentModes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosLocationPaymentModes_BillingLocations_BillingLocationId",
                        column: x => x.BillingLocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosLocationPaymentModes_PaymentModes_PaymentModeId",
                        column: x => x.PaymentModeId,
                        principalSchema: "configuration",
                        principalTable: "PaymentModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PosLocationSettings",
                schema: "pos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BillingLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceChargeEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ServiceChargeRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ServiceChargeAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RoundOffEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RoundOffAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CashVerificationRequired = table.Column<bool>(type: "bit", nullable: false),
                    Denominations = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefaultTab = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PrintEstimateBill = table.Column<bool>(type: "bit", nullable: false),
                    PrintInvoice = table.Column<bool>(type: "bit", nullable: false),
                    PrintCreditNote = table.Column<bool>(type: "bit", nullable: false),
                    PrintKot = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosLocationSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosLocationSettings_Accounts_RoundOffAccountId",
                        column: x => x.RoundOffAccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosLocationSettings_Accounts_ServiceChargeAccountId",
                        column: x => x.ServiceChargeAccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosLocationSettings_BillingLocations_BillingLocationId",
                        column: x => x.BillingLocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "LocationId", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001e1"), true, null, "Pos.Settings.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001e2"), false, null, "Pos.Settings.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentModes_AccountId",
                schema: "configuration",
                table: "PaymentModes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Contacts_OrganizationId_WalkInCustomer",
                schema: "contacts",
                table: "Contacts",
                column: "OrganizationId",
                unique: true,
                filter: "[IsWalkInCustomer] = 1");

            // Phase 60, hand-written -- every existing organization gets the walk-in customer that
            // CreateOrganizationCommandHandler now seeds for new ones (Contact.CreateWalkInCustomer:
            // a Customer named 'Cash Customer', code 'WALKIN', no limit, no term). After the filtered
            // unique index, so a second run or a stray duplicate fails loudly instead of doubling.
            // The code sits outside the numbered pool (digits only), so it cannot collide with a
            // generated one; the NOT EXISTS on it covers a tenant that typed it by hand.
            migrationBuilder.Sql(@"
INSERT INTO [contacts].[Contacts]
    ([Id], [OrganizationId], [Type], [Name], [Code], [IsActive], [OpeningBalance], [CreditLimit],
     [AcceptsReverseTransactions], [IsWalkInCustomer], [CreatedAt])
SELECT NEWID(), o.[Id], N'Customer', N'Cash Customer', N'WALKIN', 1, 0, 0, 0, 1, SYSDATETIMEOFFSET()
FROM [tenancy].[Organizations] o
WHERE NOT EXISTS (
    SELECT 1 FROM [contacts].[Contacts] c
    WHERE c.[OrganizationId] = o.[Id] AND (c.[IsWalkInCustomer] = 1 OR c.[Code] = N'WALKIN'));");

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationPaymentModes_BillingLocationId",
                schema: "pos",
                table: "PosLocationPaymentModes",
                column: "BillingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationPaymentModes_OrganizationId_BillingLocationId_PaymentModeId",
                schema: "pos",
                table: "PosLocationPaymentModes",
                columns: new[] { "OrganizationId", "BillingLocationId", "PaymentModeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationPaymentModes_PaymentModeId",
                schema: "pos",
                table: "PosLocationPaymentModes",
                column: "PaymentModeId");

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationSettings_BillingLocationId",
                schema: "pos",
                table: "PosLocationSettings",
                column: "BillingLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationSettings_OrganizationId_BillingLocationId",
                schema: "pos",
                table: "PosLocationSettings",
                columns: new[] { "OrganizationId", "BillingLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationSettings_RoundOffAccountId",
                schema: "pos",
                table: "PosLocationSettings",
                column: "RoundOffAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PosLocationSettings_ServiceChargeAccountId",
                schema: "pos",
                table: "PosLocationSettings",
                column: "ServiceChargeAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentModes_Accounts_AccountId",
                schema: "configuration",
                table: "PaymentModes",
                column: "AccountId",
                principalSchema: "accounting",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentModes_Accounts_AccountId",
                schema: "configuration",
                table: "PaymentModes");

            migrationBuilder.DropTable(
                name: "PosLocationPaymentModes",
                schema: "pos");

            migrationBuilder.DropTable(
                name: "PosLocationSettings",
                schema: "pos");

            migrationBuilder.DropIndex(
                name: "IX_PaymentModes_AccountId",
                schema: "configuration",
                table: "PaymentModes");

            // The walk-in customers this migration and CreateOrganization seeded. A walk-in that has
            // since been invoiced cannot be removed (the FK refuses), which is right: Down should
            // fail rather than orphan a document's customer.
            migrationBuilder.Sql("DELETE FROM [contacts].[Contacts] WHERE [IsWalkInCustomer] = 1;");

            migrationBuilder.DropIndex(
                name: "IX_Contacts_OrganizationId_WalkInCustomer",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e1"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001e2"));

            migrationBuilder.DropColumn(
                name: "DefaultCashOverShortAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DefaultRoundingAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "DefaultServiceChargeAccountId",
                schema: "tenancy",
                table: "TenantSettings");

            migrationBuilder.DropColumn(
                name: "ServiceChargeApplicable",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "AccountId",
                schema: "configuration",
                table: "PaymentModes");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "configuration",
                table: "PaymentModes");

            migrationBuilder.DropColumn(
                name: "IsWalkInCustomer",
                schema: "contacts",
                table: "Contacts");

            migrationBuilder.DropColumn(
                name: "PosMode",
                schema: "tenancy",
                table: "BillingLocations");
        }
    }
}
