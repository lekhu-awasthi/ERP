using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GlLineAccountCoveringIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GlLines_AccountId",
                schema: "accounting",
                table: "GlLines");

            migrationBuilder.CreateIndex(
                name: "IX_GlLines_AccountId",
                schema: "accounting",
                table: "GlLines",
                column: "AccountId")
                .Annotation("SqlServer:Include", new[] { "GlJournalEntryId", "Debit", "Credit", "ReconciliationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GlLines_AccountId",
                schema: "accounting",
                table: "GlLines");

            migrationBuilder.CreateIndex(
                name: "IX_GlLines_AccountId",
                schema: "accounting",
                table: "GlLines",
                column: "AccountId");
        }
    }
}
