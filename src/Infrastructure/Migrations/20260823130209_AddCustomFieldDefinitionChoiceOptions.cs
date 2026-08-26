using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomFieldDefinitionChoiceOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChoiceOptions",
                schema: "configuration",
                table: "CustomFieldDefinitions",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChoiceOptions",
                schema: "configuration",
                table: "CustomFieldDefinitions");
        }
    }
}
