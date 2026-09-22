using FluentValidation;

namespace ErpApp.Application.Accounting.Queries.BankReconciliationReport;

public sealed class BankReconciliationReportQueryValidator
    : AbstractValidator<BankReconciliationReportQuery>
{
    public BankReconciliationReportQueryValidator()
    {
        RuleFor(x => x.BankAccountId).NotEmpty();
        RuleFor(x => x.UnrecognizedPage).GreaterThan(0);
        // Phase 57 raised the ceiling from the usual 200 to the export's cap. The screen asks for
        // 15 and pages within each section; the export asks for the whole of both lists, because an
        // export of one page would be a lie. Two small DTOs for one account is a bounded response
        // either way, and the cap is stated rather than absent -- ClosedXML materialises every cell
        // before a byte is written, so an unbounded export is a memory decision made by whoever has
        // the largest backlog (phase 21b).
        RuleFor(x => x.UnrecognizedPageSize)
            .InclusiveBetween(1, BankReconciliationReportQuery.MaxUnrecognizedPageSize);
    }
}
