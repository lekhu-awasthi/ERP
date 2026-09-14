using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase42ReportPathIncludes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GlJournalEntries_OrganizationId_PostedAt",
                schema: "accounting",
                table: "GlJournalEntries");

            migrationBuilder.CreateIndex(
                name: "IX_GlJournalEntries_OrganizationId_PostedAt",
                schema: "accounting",
                table: "GlJournalEntries",
                columns: new[] { "OrganizationId", "PostedAt" })
                .Annotation("SqlServer:Include", new[] { "SourceDocumentType", "SourceDocumentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GlJournalEntries_OrganizationId_PostedAt",
                schema: "accounting",
                table: "GlJournalEntries");

            migrationBuilder.CreateIndex(
                name: "IX_GlJournalEntries_OrganizationId_PostedAt",
                schema: "accounting",
                table: "GlJournalEntries",
                columns: new[] { "OrganizationId", "PostedAt" });
        }
    }
}
