using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertScheduler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertDefinitions",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Medium = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AlertType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Recipients = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ScheduleTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertSendLogs",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertDefinitionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OccurrenceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertSendLogs", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000133"), true, "Configuration.AlertDefinition.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000134"), true, "Configuration.AlertDefinition.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000135"), false, "Configuration.AlertDefinition.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000136"), false, "Configuration.AlertDefinition.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000137"), true, "Configuration.AlertSendLog.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000138"), false, "Configuration.AlertSendLog.View", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertDefinitions_IsActive_ScheduleTime",
                schema: "configuration",
                table: "AlertDefinitions",
                columns: new[] { "IsActive", "ScheduleTime" });

            migrationBuilder.CreateIndex(
                name: "IX_AlertDefinitions_OrganizationId_Name",
                schema: "configuration",
                table: "AlertDefinitions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlertSendLogs_AlertDefinitionId_OccurrenceDate_Recipient",
                schema: "configuration",
                table: "AlertSendLogs",
                columns: new[] { "AlertDefinitionId", "OccurrenceDate", "Recipient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlertSendLogs_OrganizationId_CreatedAt",
                schema: "configuration",
                table: "AlertSendLogs",
                columns: new[] { "OrganizationId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertDefinitions",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "AlertSendLogs",
                schema: "configuration");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000133"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000134"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000135"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000136"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000137"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000138"));
        }
    }
}
