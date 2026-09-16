using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChequeBusinessDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Cheques_OrganizationId_ChequeDate",
                schema: "payments",
                table: "Cheques",
                columns: new[] { "OrganizationId", "ChequeDate" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cheques_OrganizationId_ChequeDate",
                schema: "payments",
                table: "Cheques");
        }
    }
}
