using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Industry = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AccountingStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsVatRegistered = table.Column<bool>(type: "bit", nullable: false),
                    WorkspaceName = table.Column<string>(type: "nvarchar(63)", maxLength: 63, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PanNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    LockDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberships",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvitedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InvitedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "tenancy",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberships_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "tenancy",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantSubscriptions",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TrialStartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TrialEndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TrackInventoryEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MultipleLocationsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MultipleWarehousesEnabled = table.Column<bool>(type: "bit", nullable: false),
                    MultiCurrencyEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ManufacturingEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PosRetailEnabled = table.Column<bool>(type: "bit", nullable: false),
                    PosRestaurantEnabled = table.Column<bool>(type: "bit", nullable: false),
                    IrdSyncEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantSubscriptions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "tenancy",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_OrganizationId_InvitedEmail",
                schema: "tenancy",
                table: "OrganizationMemberships",
                columns: new[] { "OrganizationId", "InvitedEmail" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_OrganizationId_UserId",
                schema: "tenancy",
                table: "OrganizationMemberships",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_UserId",
                schema: "tenancy",
                table: "OrganizationMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_WorkspaceName",
                schema: "tenancy",
                table: "Organizations",
                column: "WorkspaceName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSettings_OrganizationId",
                schema: "tenancy",
                table: "TenantSettings",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantSubscriptions_OrganizationId",
                schema: "tenancy",
                table: "TenantSubscriptions",
                column: "OrganizationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationMemberships",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantSettings",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantSubscriptions",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "tenancy");
        }
    }
}
