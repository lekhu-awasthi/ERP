using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase27aDocumentMechanismSweep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Comments_Contacts_ContactId",
                schema: "contacts",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_ContactId",
                schema: "contacts",
                table: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_Comments_OrganizationId_ContactId",
                schema: "contacts",
                table: "Comments");

            migrationBuilder.RenameColumn(
                name: "ContactId",
                schema: "contacts",
                table: "Comments",
                newName: "ParentId");

            migrationBuilder.AlterColumn<string>(
                name: "ParentType",
                schema: "workflow",
                table: "Tasks",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomStatusId",
                schema: "sales",
                table: "SalesOrders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomStatusId",
                schema: "manufacturing",
                table: "ProductionOrders",
                type: "uniqueidentifier",
                nullable: true);

            // Hand-corrected: the scaffold emitted defaultValue "" here, which would have stamped
            // every existing comment with an unparseable ParentType and made all of them invisible
            // to ListCommentsQuery's ParentType == Contact filter -- a silent data loss that no test
            // would have caught, since the rows would still be present and merely unreachable.
            // Every Comment written before this migration is on a Contact by construction (the
            // column being renamed above IS ContactId), so "Contact" is the correct backfill.
            migrationBuilder.AddColumn<string>(
                name: "ParentType",
                schema: "contacts",
                table: "Comments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Contact");

            migrationBuilder.AlterColumn<string>(
                name: "ParentType",
                schema: "workflow",
                table: "Attachments",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.InsertData(
                schema: "tenancy",
                table: "RolePermissions",
                columns: new[] { "Id", "IsGranted", "PermissionKey", "RoleId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-0000000001a5"), true, "Workflow.Attachment.Access", new Guid("00000000-0000-0000-0001-000000000001") },
                    { new Guid("00000000-0000-0000-0002-0000000001a6"), true, "Workflow.Attachment.Access", new Guid("00000000-0000-0000-0001-000000000002") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_OrganizationId_ParentType_ParentId",
                schema: "contacts",
                table: "Comments",
                columns: new[] { "OrganizationId", "ParentType", "ParentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Comments_OrganizationId_ParentType_ParentId",
                schema: "contacts",
                table: "Comments");

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001a5"));

            migrationBuilder.DeleteData(
                schema: "tenancy",
                table: "RolePermissions",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0002-0000000001a6"));

            migrationBuilder.DropColumn(
                name: "CustomStatusId",
                schema: "sales",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "CustomStatusId",
                schema: "manufacturing",
                table: "ProductionOrders");

            migrationBuilder.DropColumn(
                name: "ParentType",
                schema: "contacts",
                table: "Comments");

            migrationBuilder.RenameColumn(
                name: "ParentId",
                schema: "contacts",
                table: "Comments",
                newName: "ContactId");

            migrationBuilder.AlterColumn<string>(
                name: "ParentType",
                schema: "workflow",
                table: "Tasks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

            migrationBuilder.AlterColumn<string>(
                name: "ParentType",
                schema: "workflow",
                table: "Attachments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

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

            migrationBuilder.AddForeignKey(
                name: "FK_Comments_Contacts_ContactId",
                schema: "contacts",
                table: "Comments",
                column: "ContactId",
                principalSchema: "contacts",
                principalTable: "Contacts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
