using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase63TillReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Channel",
                schema: "sales",
                table: "CreditNotes",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Erp");

            migrationBuilder.AddColumn<Guid>(
                name: "PosSessionId",
                schema: "sales",
                table: "CreditNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "sales",
                table: "CreditNotes",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "RoundOff",
                schema: "sales",
                table: "CreditNotes",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeAmount",
                schema: "sales",
                table: "CreditNoteLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeRate",
                schema: "sales",
                table: "CreditNoteLines",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "CreditNotePayouts",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentModeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotePayouts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNotePayouts_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalSchema: "accounting",
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotePayouts_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalSchema: "sales",
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreditNotePayouts_PaymentModes_PaymentModeId",
                        column: x => x.PaymentModeId,
                        principalSchema: "configuration",
                        principalTable: "PaymentModes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotePrints",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintNumber = table.Column<int>(type: "int", nullable: false),
                    PrintedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotePrints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditNotePrints_CreditNotes_CreditNoteId",
                        column: x => x.CreditNoteId,
                        principalSchema: "sales",
                        principalTable: "CreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_PosSessionId",
                schema: "sales",
                table: "CreditNotes",
                column: "PosSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotePayouts_AccountId",
                schema: "sales",
                table: "CreditNotePayouts",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotePayouts_CreditNoteId",
                schema: "sales",
                table: "CreditNotePayouts",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotePayouts_PaymentModeId",
                schema: "sales",
                table: "CreditNotePayouts",
                column: "PaymentModeId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotePrints_CreditNoteId",
                schema: "sales",
                table: "CreditNotePrints",
                column: "CreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotePrints_OrganizationId_CreditNoteId_PrintNumber",
                schema: "sales",
                table: "CreditNotePrints",
                columns: new[] { "OrganizationId", "CreditNoteId", "PrintNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CreditNotes_PosSessions_PosSessionId",
                schema: "sales",
                table: "CreditNotes",
                column: "PosSessionId",
                principalSchema: "pos",
                principalTable: "PosSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreditNotes_PosSessions_PosSessionId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropTable(
                name: "CreditNotePayouts",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "CreditNotePrints",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "IX_CreditNotes_PosSessionId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "Channel",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "PosSessionId",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "RoundOff",
                schema: "sales",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "ServiceChargeAmount",
                schema: "sales",
                table: "CreditNoteLines");

            migrationBuilder.DropColumn(
                name: "ServiceChargeRate",
                schema: "sales",
                table: "CreditNoteLines");
        }
    }
}
