using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase73PrintingTemplateLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CompactMargins",
                schema: "configuration",
                table: "PrintingTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "FooterText",
                schema: "configuration",
                table: "PrintingTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HiddenElements",
                schema: "configuration",
                table: "PrintingTemplates",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            // Phase 73 -- hand-edited: the scaffold defaulted Layout to "", which Enum.Parse refuses, so every
            // existing template would have failed to load. Standard is what each of them printed. The two
            // lists' "" is right as scaffolded: nothing hidden, no custom fields.
            migrationBuilder.AddColumn<string>(
                name: "Layout",
                schema: "configuration",
                table: "PrintingTemplates",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.AddColumn<string>(
                name: "PrintedCustomFieldIds",
                schema: "configuration",
                table: "PrintingTemplates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompactMargins",
                schema: "configuration",
                table: "PrintingTemplates");

            migrationBuilder.DropColumn(
                name: "FooterText",
                schema: "configuration",
                table: "PrintingTemplates");

            migrationBuilder.DropColumn(
                name: "HiddenElements",
                schema: "configuration",
                table: "PrintingTemplates");

            migrationBuilder.DropColumn(
                name: "Layout",
                schema: "configuration",
                table: "PrintingTemplates");

            migrationBuilder.DropColumn(
                name: "PrintedCustomFieldIds",
                schema: "configuration",
                table: "PrintingTemplates");
        }
    }
}
