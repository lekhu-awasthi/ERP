using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase20dPrintingAndCustomTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomTemplates",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrintingTemplates",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintingTemplates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomTemplates_OrganizationId_Type_Name",
                schema: "configuration",
                table: "CustomTemplates",
                columns: new[] { "OrganizationId", "Type", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintingTemplates_OrganizationId_DocumentType_Name",
                schema: "configuration",
                table: "PrintingTemplates",
                columns: new[] { "OrganizationId", "DocumentType", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomTemplates",
                schema: "configuration");

            migrationBuilder.DropTable(
                name: "PrintingTemplates",
                schema: "configuration");
        }
    }
}
