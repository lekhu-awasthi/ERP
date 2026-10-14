using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase91CreditNoteReferrerIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OrganizationId_ReferrerId",
                schema: "sales",
                table: "CreditNotes",
                columns: new[] { "OrganizationId", "ReferrerId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_OrganizationId_ReferrerId",
                schema: "sales",
                table: "CreditNotes");
        }
    }
}
