using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ErpApp.Infrastructure.Migrations
{
    /// <summary>
    /// Phase 39 — widens the template body column and converts every stored plain-text rich-text
    /// field into HTML.
    ///
    /// <para><b>Why a backfill rather than a render-time guess.</b> Phases 20d and 27b stored these
    /// seven columns as plain text from a textarea; phase 39 stores HTML. Deciding at render time
    /// which of the two a given row is would leave both representations in one column forever and
    /// put the guess on every future read path. Converting once means there is exactly one
    /// representation, which is the same argument phase 31 makes for backfilling a NOT NULL column
    /// by hand rather than letting a default lie about the rows already there.</para>
    ///
    /// <para><b>The conversion is the SQL equivalent of <c>RichText.FromPlainText</c>, with one
    /// stated simplification</b>: it wraps the whole value in a single paragraph and turns every
    /// newline into a <c>&lt;br /&gt;</c>, where the C# splits blank-line-separated runs into
    /// separate paragraphs. The two render identically in the browser and in the PDF — a blank line
    /// is a blank line either way — and the simpler form is what T-SQL can express without a loop.
    /// Re-saving a converted row through the editor re-canonicalises it.</para>
    ///
    /// <para>Escaping runs before the newline substitution, and <c>&amp;</c> before <c>&lt;</c> and
    /// <c>&gt;</c>, or the escapes would escape each other. The <c>NOT LIKE '&lt;p&gt;%'</c> guard
    /// makes the statement safe to run twice; a plain-text value that genuinely began with the
    /// characters <c>&lt;p&gt;</c> is skipped and left for the editor to canonicalise on its next
    /// save, which is the harmless direction to be wrong in.</para>
    /// </summary>
    public partial class Phase39RichText : Migration
    {
        /// <summary>The seven columns, as (schema, table, column). Five documents' Terms plus the
        /// two template bodies — the same set <c>RichTextWritePathSweepGuardTests</c> proves the
        /// Domain sanitises.</summary>
        private static readonly (string Schema, string Table, string Column)[] PlainTextColumns =
        [
            ("sales", "Quotations", "Terms"),
            ("sales", "SalesOrders", "Terms"),
            ("sales", "Invoices", "Terms"),
            ("sales", "CreditNotes", "Terms"),
            ("purchasing", "PurchaseOrders", "Terms"),
            ("configuration", "CustomTemplates", "Body"),
            ("configuration", "EmailTemplates", "Body"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ordered by hand: the column has to be widened before anything writes markup into it,
            // and markup is longer than the prose it wraps. `migrations add` orders by model diff,
            // not by data safety (CLAUDE.md, phase-1c bug #1).
            migrationBuilder.AlterColumn<string>(
                name: "Body",
                schema: "configuration",
                table: "CustomTemplates",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(4000)",
                oldMaxLength: 4000);

            foreach (var (schema, table, column) in PlainTextColumns)
            {
                migrationBuilder.Sql($"""
                    UPDATE [{schema}].[{table}]
                    SET [{column}] =
                        '<p>' +
                        REPLACE(
                            REPLACE(
                                REPLACE(
                                    REPLACE(
                                        REPLACE(
                                            REPLACE(LTRIM(RTRIM([{column}])), '&', '&amp;'),
                                            '<', '&lt;'),
                                        '>', '&gt;'),
                                    CHAR(13) + CHAR(10), '<br />'),
                                CHAR(13), '<br />'),
                            CHAR(10), '<br />') +
                        '</p>'
                    WHERE [{column}] IS NOT NULL
                      AND LTRIM(RTRIM([{column}])) <> ''
                      AND [{column}] NOT LIKE '<p>%';
                    """);
            }

            // A Terms value that held only whitespace stored a lie: the form showed an empty field
            // and the column said otherwise. Now that the Domain normalises that to null on every
            // write, the rows already there should say the same thing.
            foreach (var (schema, table, column) in PlainTextColumns)
            {
                if (column != "Terms")
                {
                    continue;
                }

                migrationBuilder.Sql(
                    $"UPDATE [{schema}].[{table}] SET [{column}] = NULL "
                    + $"WHERE [{column}] IS NOT NULL AND LTRIM(RTRIM([{column}])) = '';");
            }
        }

        /// <summary>
        /// <b>Down narrows the column and leaves the content as HTML</b>, deliberately. Stripping
        /// tags back out would not restore the original text — the escaping is reversible but the
        /// paragraph and line-break structure is not distinguishable from markup the user has since
        /// typed — so a "reversal" would quietly damage rows that were edited after the upgrade.
        ///
        /// <para>The narrowing itself will fail if any template body has grown past 4,000
        /// characters, which is the correct behaviour: that is the migration telling you the data no
        /// longer fits the shape you are asking for, rather than truncating somebody's terms.</para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Body",
                schema: "configuration",
                table: "CustomTemplates",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
