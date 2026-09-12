using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Inventory.Stock;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Manufacturing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Manufacturing.Commands.VoidProductionJournal;

/// <summary>
/// Unwinds a production run in both directions, which is what makes it different from every other
/// Void in this codebase -- the others either only created stock or only consumed it.
///
/// <list type="bullet">
/// <item><b>Stock created</b> (the finished good and every by-product) is reversed by
/// <c>ReverseIncrementAsync</c>, which refuses the whole void with a 409 if any of those layers has
/// already been consumed onward. That is exactly the right behaviour here and is the interesting
/// case: once some of the finished goods have been sold, the run cannot be pretended away, because
/// the sale's COGS was computed from the very cost this void would erase.</item>
/// <item><b>Stock consumed</b> (the raw materials) is put back by <c>IncrementAsync</c> at each
/// line's recorded ConsumedUnitCost -- the cost it actually left at, mirroring
/// VoidInventoryAdjustmentCommandHandler's restock of a Decrease line. Always succeeds.</item>
/// <item><b>The GL</b> is reversed by <c>SourceDocumentGlEntries.ReverseOutstandingAsync</c>, which
/// nets this run's own posted lines rather than re-deriving them from the posting rule --
/// phase-16a's guarantee against phase-6 bug #3's failure mode, over every entry the run has
/// (phase 37: a run whose output covered a shortfall has two).</item>
/// </list>
///
/// <para>Order matters: ReverseIncrementAsync runs first so a partly-consumed run fails before
/// anything at all has been mutated.</para>
/// </summary>
public sealed class VoidProductionJournalCommandHandler(
    IAppDbContext db, ICurrentUserService currentUser, IStockLedgerService stockLedgerService)
    : IRequestHandler<VoidProductionJournalCommand, VoidProductionJournalResult>
{
    public async Task<VoidProductionJournalResult> Handle(
        VoidProductionJournalCommand request, CancellationToken cancellationToken)
    {
        var journal = await db.ProductionJournals
            .Include(x => x.RawMaterials).Include(x => x.ByProducts)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Production journal not found.");

        if (journal.Status != ProductionJournalStatus.Approved)
        {
            throw new ConflictException("Only an Approved production journal can be voided.");
        }

        // Fail fast (409) before mutating anything if any layer this run created has already been
        // consumed by a later document.
        await stockLedgerService.ReverseIncrementAsync(
            request.OrganizationId, DocumentType.ProductionJournal, journal.Id, journal.Date, cancellationToken);

        journal.Void(currentUser.UserId);

        // Phase 37 -- every entry this run posted, not "the" entry: a run whose output covered a
        // shortfall has a second, and `SingleAsync` over the pair would have thrown a 500 the first
        // time a Warn tenant voided one.
        await SourceDocumentGlEntries.ReverseOutstandingAsync(
            db, DocumentType.ProductionJournal, journal.Id, cancellationToken);

        var costCatchUp = 0m;

        foreach (var line in journal.RawMaterials.Where(x => x.ConsumedUnitCost is not null))
        {
            costCatchUp += await stockLedgerService.IncrementAsync(
                request.OrganizationId, line.ProductId, journal.WarehouseId, line.Quantity, line.ConsumedUnitCost!.Value,
                DocumentType.ProductionJournal, journal.Id, journal.Date, cancellationToken, journal.LocationId);
        }

        await StockCostCatchUp.PostAsync(
            db, request.OrganizationId, DocumentType.ProductionJournal, journal.Id, journal.LocationId,
            costCatchUp, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidProductionJournalResult(journal.Id, journal.Code, journal.Status, journal.VoidedAt);
    }
}
