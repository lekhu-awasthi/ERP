using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited from the scaffolded output: the generated Up() dropped the old string
            // "Role" column before adding "RoleId", which would silently discard every existing
            // membership's role. Reordered so Roles/RolePermissions are seeded, then RoleId is
            // backfilled from the still-present "Role" column via raw SQL, and only then is
            // "Role" dropped -- see docs/phase-1c-status.md for why this needed manual editing.
            migrationBuilder.CreateTable(
                name: "Roles",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsGranted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "tenancy",
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "Roles",
                columns: new[] { "Id", "Description", "Name" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0001-000000000001"), "Full access to every permission in the Organization.", "Admin" },
                    { new Guid("00000000-0000-0000-0001-000000000002"), "Read-only access; cannot invite users or approve join requests.", "Member" }
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000001"), true, "Tenancy.Organization.InviteUser", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000002"), true, "Tenancy.Organization.AcceptRequest", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000003"), false, "Tenancy.Organization.InviteUser", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000004"), false, "Tenancy.Organization.AcceptRequest", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.AddColumn<Guid>(
                name: "RoleId",
                schema: "tenancy",
                table: "OrganizationMemberships",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Backfill from the old "Role" text column (still present at this point) before it's
            // dropped below. Existing rows only ever contain "Admin" or "Member" (MembershipRole's
            // two values), written via OrganizationMembershipConfiguration's HasConversion<string>().
            migrationBuilder.Sql(
                """
                UPDATE [tenancy].[OrganizationMemberships]
                SET [RoleId] = CASE WHEN [Role] = N'Admin'
                    THEN '00000000-0000-0000-0001-000000000001'
                    ELSE '00000000-0000-0000-0001-000000000002'
                END;
                """);

            migrationBuilder.DropColumn(
                name: "Role",
                schema: "tenancy",
                table: "OrganizationMemberships");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberships_RoleId",
                schema: "tenancy",
                table: "OrganizationMemberships",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionKey",
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrganizationMemberships_Roles_RoleId",
                schema: "tenancy",
                table: "OrganizationMemberships",
                column: "RoleId",
                principalSchema: "tenancy",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrganizationMemberships_Roles_RoleId",
                schema: "tenancy",
                table: "OrganizationMemberships");

            migrationBuilder.DropIndex(
                name: "IX_OrganizationMemberships_RoleId",
                schema: "tenancy",
                table: "OrganizationMemberships");

            migrationBuilder.AddColumn<string>(
                name: "Role",
                schema: "tenancy",
                table: "OrganizationMemberships",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE [tenancy].[OrganizationMemberships]
                SET [Role] = CASE WHEN [RoleId] = '00000000-0000-0000-0001-000000000001'
                    THEN N'Admin'
                    ELSE N'Member'
                END;
                """);

            migrationBuilder.DropColumn(
                name: "RoleId",
                schema: "tenancy",
                table: "OrganizationMemberships");

            migrationBuilder.DropTable(
                name: "RolePermissions",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Roles",
                schema: "tenancy");
        }
    }
}
