using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BankReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationId",
                schema: "accounting",
                table: "GlLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BankReconciliations",
                schema: "accounting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BankAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReconciledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankReconciliations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankReconciliations_OrganizationId_BankAccountId",
                schema: "accounting",
                table: "BankReconciliations",
                columns: new[] { "OrganizationId", "BankAccountId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankReconciliations",
                schema: "accounting");

            migrationBuilder.DropColumn(
                name: "ReconciliationId",
                schema: "accounting",
                table: "GlLines");

            migrationBuilder.DropColumn(
                name: "ReconciliationId",
                schema: "accounting",
                table: "BankStatementLines");
        }
    }
}
