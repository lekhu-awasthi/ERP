using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase30Communications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "communications");

            // Phase 30, Decision B: CustomTemplateType.Email is gone -- email templates are their
            // own aggregate with six fields and a disjoint type vocabulary (see EmailTemplate).
            // Phase 27b added that member on the strength of the reference product grouping both
            // under one Configurations panel; the grouping is a UI convenience and the data model
            // underneath is a different shape. No consumer was ever built for it, so any row
            // carrying it is a template that could never have been used for anything -- deleted
            // here rather than left to fail enum mapping on the next read.
            migrationBuilder.Sql(
                "DELETE FROM [configuration].[CustomTemplates] WHERE [Type] = 'Email';");

            // Hand-edited: the scaffolder's defaultValue was "", which is not a valid AlertMedium
            // and would throw on the first read of any pre-existing row. Every AlertSendLog written
            // before this phase was an email -- AlertMedium had exactly one member -- so "Email" is
            // both the correct backfill and the correct default. (CLAUDE.md: read any migration that
            // adds or retypes a column and fix it by hand.)
            migrationBuilder.AddColumn<string>(
                name: "Medium",
                schema: "configuration",
                table: "AlertSendLogs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Email");

            migrationBuilder.CreateTable(
                name: "EmailSendLogs",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Context = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToAddresses = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CcAddresses = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BccAddresses = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ReplyTo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttachDocumentPdf = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SentByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSendLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSendLogs_Users_SentByUserId",
                        column: x => x.SentByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplates",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Context = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReplyTo = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Cc = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Bcc = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSendAttachments",
                schema: "communications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailSendLogId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PurgedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSendAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailSendAttachments_EmailSendLogs_EmailSendLogId",
                        column: x => x.EmailSendLogId,
                        principalSchema: "communications",
                        principalTable: "EmailSendLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001ab"), true, "Configuration.EmailTemplate.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001ac"), true, "Configuration.EmailTemplate.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001ad"), true, "Communication.Email.Send", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001ae"), true, "Communication.EmailLog.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001af"), true, "Communication.Email.Send", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-0000000001b0"), true, "Communication.EmailLog.View", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSendAttachments_EmailSendLogId",
                schema: "communications",
                table: "EmailSendAttachments",
                column: "EmailSendLogId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSendLogs_OrganizationId_ParentType_ParentId_CreatedAt",
                schema: "communications",
                table: "EmailSendLogs",
                columns: new[] { "OrganizationId", "ParentType", "ParentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailSendLogs_OrganizationId_RequestId",
                schema: "communications",
                table: "EmailSendLogs",
                columns: new[] { "OrganizationId", "RequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailSendLogs_SentByUserId",
                schema: "communications",
                table: "EmailSendLogs",
                column: "SentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailSendLogs_Status_CreatedAt",
                schema: "communications",
                table: "EmailSendLogs",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_OrganizationId_Context",
                schema: "configuration",
                table: "EmailTemplates",
                columns: new[] { "OrganizationId", "Context" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailTemplates_OrganizationId_Context_Name",
                schema: "configuration",
                table: "EmailTemplates",
                columns: new[] { "OrganizationId", "Context", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailSendAttachments",
                schema: "communications");

            migrationBuilder.DropTable(
                name: "EmailTemplates",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "EmailSendLogs",
                schema: "communications");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ab"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ac"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ad"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001ae"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001af"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001b0"));

            migrationBuilder.DropColumn(
                name: "Medium",
                schema: "configuration",
                table: "AlertSendLogs");
        }
    }
}
