using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase75PrintingTemplateLocationChoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintingTemplateLocationChoices",
                schema: "configuration",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintingTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintingTemplateLocationChoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintingTemplateLocationChoices_BillingLocations_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "tenancy",
                        principalTable: "BillingLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintingTemplateLocationChoices_PrintingTemplates_PrintingTemplateId",
                        column: x => x.PrintingTemplateId,
                        principalSchema: "configuration",
                        principalTable: "PrintingTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintingTemplateLocationChoices_LocationId",
                schema: "configuration",
                table: "PrintingTemplateLocationChoices",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintingTemplateLocationChoices_OrganizationId_DocumentType_LocationId",
                schema: "configuration",
                table: "PrintingTemplateLocationChoices",
                columns: new[] { "OrganizationId", "DocumentType", "LocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintingTemplateLocationChoices_PrintingTemplateId",
                schema: "configuration",
                table: "PrintingTemplateLocationChoices",
                column: "PrintingTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintingTemplateLocationChoices",
                schema: "configuration");
        }
    }
}
