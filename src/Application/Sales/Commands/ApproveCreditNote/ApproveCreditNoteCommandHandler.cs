using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Sales.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveCreditNote;

/// <summary>
/// Post-Phase-7 fix: a CreditNote whose ReferrerType/ReferrerId point at the Invoice it reverses
/// now puts stock back, instead of leaving the FIFO ledger untouched (phase-7-status.md's flagged
/// gap). For each of this CreditNote's Goods lines, finds the matching source InvoiceLine(s) by the
/// same exact (ProductId, Rate, VatRate) triple SalesValidation already caps quantities against, and
/// calls IStockLedgerService.IncrementAsync at the Invoice's own WarehouseId, using the
/// quantity-weighted average of those lines' InvoiceLine.CogsUnitCost (recorded when the Invoice was
/// originally approved -- see InvoiceLine's doc comment) -- not a freshly-guessed cost, so a return
/// re-enters stock at what it actually left at. A standalone CreditNote (no ReferrerId, or one whose
/// referrer isn't an Invoice) skips this entirely -- there is no source to know a WarehouseId or
/// cost from, same as CreateCreditNoteCommandHandler already treats it for quantity-capping.
/// </summary>
public sealed class ApproveCreditNoteCommandHandler(
    IAppDbContext db,
    IDocumentNumberGenerator numberGenerator,
    ICurrentUserService currentUser,
    IGlPostingRule<CreditNotePostingInput> postingRule,
    IStockLedgerService stockLedgerService)
    : IRequestHandler<ApproveCreditNoteCommand, ApproveCreditNoteResult>
{
    public async Task<ApproveCreditNoteResult> Handle(ApproveCreditNoteCommand request, CancellationToken cancellationToken)
    {
        var creditNote = await db.CreditNotes
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Credit note not found.");

        if (creditNote.Status != CreditNoteStatus.Draft)
        {
            throw new ConflictException("Only a Draft credit note can be approved.");
        }

        if (creditNote.Lines.Count == 0)
        {
            throw new ConflictException("A credit note needs at least one line to be approved.");
        }

        // Phase 63 -- the approve core moved to CreditNoteApprovalPosting, which the till's refund
        // also calls; see the doc comment above for what it does to stock.
        await CreditNoteApprovalPosting.ApproveAndPostAsync(
            db, numberGenerator, postingRule, stockLedgerService, currentUser.UserId, creditNote, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveCreditNoteResult(creditNote.Id, creditNote.Code, creditNote.Status, creditNote.ApprovedAt);
    }
}
