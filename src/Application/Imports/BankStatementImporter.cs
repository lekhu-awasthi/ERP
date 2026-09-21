using ErpApp.Application.Accounting.Commands.CreateBankStatementLine;
using ErpApp.Domain.Imports;
using MediatR;

namespace ErpApp.Application.Imports;

/// <summary>
/// Bank statement import (phase 55). One spreadsheet row becomes one <c>BankStatementLine</c>
/// through <c>CreateBankStatementLineCommand</c> and the normal MediatR pipeline.
///
/// <para><b>Decision A, in one class: this is an ordinary importer, and the kickoff's reason for
/// doubting that does not survive contact with phase 21c.</b> The doubt was that a statement line
/// "resolves into no command -- it is a raw row awaiting a match, and its only destination is a
/// table". So is a <c>MigratedSalesRegisterEntry</c>: it posts nothing, numbers nothing, approves
/// nothing, and is read by exactly two reports. It has had a create command and a place in this
/// list since phase 21c. What <c>PlanAsync</c> needs is that the command <i>exist before it is
/// sent</i>, so the dry run and the apply pass share one resolution; it asks nothing at all about
/// what the command then does. Phase 38's own precedent for the other answer -- the landed-cost
/// grid, which is neither an <c>ImportTemplateDefinition</c> nor an <c>ImportJob</c> -- turns on
/// something different and absent here: that template is generated from the document in front of
/// you, nothing is written, and there is nothing to resume. A 5,000-line statement is exactly the
/// thing the row ledger, the heartbeat and the resumable claim exist for.</para>
///
/// <para><b>The column set is the reference product's own "Two Amount Column" template</b>, read
/// off the file it serves (<c>bank_statement_sample2.xlsx</c>, downloaded and decoded 2026-09-21):
/// Date, Deposit, Withdrawal, Description, in that order. Its second template, "Single Amount
/// Column", carries one signed <c>Amount</c> instead and is <b>not</b> shipped -- see the note on
/// <see cref="Template"/>.</para>
///
/// <para><b>Three differences from the reference product's parser, each deliberate.</b>
/// <list type="number">
/// <item><b>Columns are matched by name, not by position.</b> Its parser skips row 1 whatever it
/// says and reads columns B..E positionally -- a file whose header reads "Txn Date" imports fine
/// and a file with the columns reordered fails every row with "invalid date" (both probed live).
/// <c>ImportRowReader</c> matches on the header text, which this codebase has done since phase 21a
/// and which turns a reordered file into a clear whole-file rejection.</item>
/// <item><b>Text dates parse.</b> Its parser accepts only a real Excel date cell: ISO, dd-MM-yyyy
/// and dd/MM/yyyy text all came back "Row [N]: invalid date". <c>GetRequiredDate</c> accepts all
/// three, day-first ahead of month-first (phase 21c). A user pasting from a bank's CSV export gets
/// text dates, so refusing them is a defect rather than a rule.</item>
/// <item><b>A negative Deposit is rejected.</b> Its parser stores one verbatim, producing a line
/// whose displayed direction contradicts its sign. See
/// <c>CreateBankStatementLineCommandValidator</c>.</item>
/// </list></para>
/// </summary>
public sealed class BankStatementImporter : IEntityImporter
{
    private const string ColumnDate = "Date";
    private const string ColumnDeposit = "Deposit";
    private const string ColumnWithdrawal = "Withdrawal";
    private const string ColumnDescription = "Description";

    public ImportEntityType EntityType => ImportEntityType.BankStatement;

    /// <summary>
    /// <para><b>One template, not the reference product's two.</b> It offers "Single Amount
    /// Column" (Date, Amount, Description -- deposits positive, withdrawals negative) beside this
    /// one, and both exist for the same reason: to save a user a transformation while copying out
    /// of whatever their bank exports. But the user is copying into <i>our</i> template either
    /// way, so the transformation happens regardless, and the split-column form is the one that
    /// cannot be misread -- a signed column puts the entire deposit/withdrawal distinction on a
    /// minus sign that a spreadsheet will happily strip with a number format. Shipping one is also
    /// what keeps <see cref="ImportTemplateDefinition"/> a single column set per entity type;
    /// supporting both would mean variants, which is real machinery for a convenience.</para>
    ///
    /// <para>Recorded as a divergence rather than parity, and additive if it is ever wanted.</para>
    /// </summary>
    public ImportTemplateDefinition Template { get; } = new(
        ImportEntityType.BankStatement,
        SheetName: "Bank Statement",
        FileNameStem: "BankStatementTemplate",
        Columns:
        [
            new ImportColumn(ColumnDate, Required: true),
            new ImportColumn(ColumnDeposit, Required: false),
            new ImportColumn(ColumnWithdrawal, Required: false),
            new ImportColumn(ColumnDescription, Required: false),
        ],
        SampleRow: ["2026-09-01", "20000", null, "Sample deposit transaction description"],
        Instructions:
        [
            "Copy the rows from your bank's statement for ONE account into the columns provided. "
                + "The account is the one you started this import from -- it is not a column here.",
            "Each row is either a deposit or a withdrawal, never both and never neither. Put the "
                + "amount in the matching column and leave the other blank.",
            "Both amounts are POSITIVE numbers. A withdrawal of 250 is 250 in the Withdrawal "
                + "column, not -250 in the Deposit column.",
            "Date accepts yyyy-MM-dd (AD), for example 2026-09-01. Dates written dd/MM/yyyy are "
                + "read day-first. Bikram Sambat dates are not accepted here -- convert to AD.",
            "Description is optional and is the bank's own narration. It is what you will match "
                + "against your own transactions, so keep it if you have it.",
            "Importing a statement changes no balance and posts nothing to the General Ledger. It "
                + "records what the bank says happened so it can be compared with what you "
                + "recorded.",
            "There is no duplicate check, because a bank statement has no unique reference: two "
                + "identical withdrawals on one day are two real rows. If you upload the same file "
                + "twice, delete the second import from the statement list.",
            "Do not change the column headers.",
        ]);

    public Task<ImportRowPlan> PlanAsync(
        ImportRowContext context, ImportRowReader row, CancellationToken cancellationToken)
    {
        // Defence in depth only -- CreateImportJobCommandValidator rejects UpdateExisting for this
        // entity type at upload time, so this is unreachable through the UI. It is here because a
        // statement line has no business key to update *by*, so "update" could only ever mean
        // "create", and silently creating when asked to update is the worst reading of an
        // ambiguous request (the reasoning MigratedSalesRegisterImporter records).
        if (context.Mode != ImportMode.CreateNew)
        {
            throw new ImportRowException(
                null,
                "Bank statement rows can only be created, not updated. Re-upload with Create New Records.");
        }

        // The account is context for the run, not data on the row -- see ImportJob.BankAccountId.
        // A missing one here is a programming error rather than a bad file, so it is not an
        // ImportRowException: that type renders as a row the user can fix, and this is not.
        var bankAccountId = context.BankAccountId
            ?? throw new InvalidOperationException(
                "A bank statement import job carries no bank account. CreateImportJobCommandValidator "
                + "requires one for ImportEntityType.BankStatement, so reaching this means a job was "
                + "created by some other path that did not.");

        var date = row.GetRequiredDate(ColumnDate);
        var deposit = row.GetOptionalDecimal(ColumnDeposit);
        var withdrawal = row.GetOptionalDecimal(ColumnWithdrawal);
        var description = row.GetOptionalString(ColumnDescription);

        // Everything a row's own cells can decide is decided here, because everything decided here
        // is a rejection the review step can show before anything is written (phase 38). The two
        // amount rules are the command's validators' job and run in the dry run too -- they are
        // not restated as a second, weaker copy.
        var action = deposit != 0m && withdrawal == 0m
            ? $"Add deposit of {deposit} on {date:yyyy-MM-dd}"
            : withdrawal != 0m && deposit == 0m
                ? $"Add withdrawal of {withdrawal} on {date:yyyy-MM-dd}"
                : $"Add statement line dated {date:yyyy-MM-dd}";

        return Task.FromResult<ImportRowPlan>(
            ImportRowPlan.For<CreateBankStatementLineCommand, CreateBankStatementLineResult>(
                new CreateBankStatementLineCommand(
                    context.OrganizationId,
                    bankAccountId,
                    date,
                    description,
                    deposit,
                    withdrawal,
                    context.ImportJobId),
                action,
                // No business code: a statement line has none, which is the same answer
                // ProductAttributePool gives and for the same reason.
                targetCode: null,
                result => new ImportRowResult(result.Id, TargetCode: null)));
    }
}
