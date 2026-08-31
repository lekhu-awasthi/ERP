using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase20dPrintingCustomTemplatePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000129"), true, "Configuration.PrintingTemplate.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000012a"), true, "Configuration.PrintingTemplate.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000012b"), false, "Configuration.PrintingTemplate.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000012c"), false, "Configuration.PrintingTemplate.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000012d"), true, "Configuration.CustomTemplate.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000012e"), true, "Configuration.CustomTemplate.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000012f"), false, "Configuration.CustomTemplate.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000130"), false, "Configuration.CustomTemplate.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000129"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000012a"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000012b"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000012c"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000012d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000012e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000012f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000130"));
        }
    }
}
