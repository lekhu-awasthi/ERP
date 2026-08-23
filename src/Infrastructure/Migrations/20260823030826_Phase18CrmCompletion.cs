using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase18CrmCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attachments",
                schema: "workflow",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attachments_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                schema: "contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContactPersonnel",
                schema: "contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    OrganizationTitle = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactPersonnel", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContactPersonnel_ContactGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "contacts",
                        principalTable: "ContactGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContactPersonnel_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SmsCreditLedgerEntries",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ChangeAmount = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelatedSmsBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsCreditLedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsCreditLedgerEntries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SmsTemplates",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmsLogs",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContactId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreditsUsed = table.Column<int>(type: "int", nullable: false),
                    SentByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SmsLogs_Contacts_ContactId",
                        column: x => x.ContactId,
                        principalSchema: "contacts",
                        principalTable: "Contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SmsLogs_SmsTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalSchema: "crm",
                        principalTable: "SmsTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SmsLogs_Users_SentByUserId",
                        column: x => x.SentByUserId,
                        principalSchema: "identity",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-00000000010d"), true, "Crm.Sms.Send", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-00000000010e"), false, "Crm.Sms.Send", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-00000000010f"), true, "Crm.SmsTemplate.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000110"), true, "Crm.SmsTemplate.Manage", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000111"), true, "Crm.SmsTemplate.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000112"), false, "Crm.SmsTemplate.Manage", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000113"), true, "Crm.SmsCreditLedger.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000114"), true, "Crm.SmsCreditLedger.Adjust", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000115"), true, "Crm.SmsCreditLedger.View", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000116"), false, "Crm.SmsCreditLedger.Adjust", new Guid("00000000-0000-0000-0001-000000000002") },
                    { new Guid("00000000-0000-0000-0002-000000000117"), true, "Crm.SmsLog.View", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-000000000118"), true, "Crm.SmsLog.View", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_OrganizationId_ParentType_ParentId",
                schema: "workflow",
                table: "Attachments",
                columns: new[] { "OrganizationId", "ParentType", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_UploadedByUserId",
                schema: "workflow",
                table: "Attachments",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_AuthorUserId",
                schema: "contacts",
                table: "Comments",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ContactId",
                schema: "contacts",
                table: "Comments",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_OrganizationId_ContactId",
                schema: "contacts",
                table: "Comments",
                columns: new[] { "OrganizationId", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContactPersonnel_ContactId",
                schema: "contacts",
                table: "ContactPersonnel",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactPersonnel_GroupId",
                schema: "contacts",
                table: "ContactPersonnel",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ContactPersonnel_OrganizationId_ContactId",
                schema: "contacts",
                table: "ContactPersonnel",
                columns: new[] { "OrganizationId", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsCreditLedgerEntries_CreatedByUserId",
                schema: "crm",
                table: "SmsCreditLedgerEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsCreditLedgerEntries_OrganizationId",
                schema: "crm",
                table: "SmsCreditLedgerEntries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_ContactId",
                schema: "crm",
                table: "SmsLogs",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_OrganizationId_BatchId",
                schema: "crm",
                table: "SmsLogs",
                columns: new[] { "OrganizationId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_OrganizationId_ContactId",
                schema: "crm",
                table: "SmsLogs",
                columns: new[] { "OrganizationId", "ContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_SentByUserId",
                schema: "crm",
                table: "SmsLogs",
                column: "SentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsLogs_TemplateId",
                schema: "crm",
                table: "SmsLogs",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsTemplates_OrganizationId",
                schema: "crm",
                table: "SmsTemplates",
                column: "OrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachments",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "Comments",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "ContactPersonnel",
                schema: "contacts");

            migrationBuilder.DropTable(
                name: "SmsCreditLedgerEntries",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "SmsLogs",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "SmsTemplates",
                schema: "crm");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000010d"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000010e"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-00000000010f"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000110"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000111"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000112"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000113"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000114"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000115"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000116"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000117"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-000000000118"));
        }
    }
}
