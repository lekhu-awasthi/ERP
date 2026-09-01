using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase21bExportJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "exports");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArtifactPurgedAt",
                schema: "imports",
                table: "ImportJobs",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExportJobs",
                schema: "exports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    InitiatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    TotalCategoryCount = table.Column<int>(type: "int", nullable: false),
                    ProcessedCategoryCount = table.Column<int>(type: "int", nullable: false),
                    TotalRowCount = table.Column<int>(type: "int", nullable: false),
                    TruncationNotice = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CancellationRequested = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ArtifactPurgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-00000000013d"), true, "Configuration.ExportJob.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000013e"), true, "Configuration.ExportJob.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000013f"), false, "Configuration.ExportJob.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000140"), false, "Configuration.ExportJob.Manage", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_ArtifactPurgedAt_ExpiresAt",
                schema: "exports",
                table: "ExportJobs",
                columns: new[] { "ArtifactPurgedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_OrganizationId_CreatedAt",
                schema: "exports",
                table: "ExportJobs",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExportJobs_Status_HeartbeatAt_CreatedAt",
                schema: "exports",
                table: "ExportJobs",
                columns: new[] { "Status", "HeartbeatAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportJobs",
                schema: "exports");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000013d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000013e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000013f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000140"));

            migrationBuilder.DropColumn(
                name: "ArtifactPurgedAt",
                schema: "imports",
                table: "ImportJobs");
        }
    }
}
