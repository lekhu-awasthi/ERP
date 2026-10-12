using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.FindPosSales;

public sealed class FindPosSalesQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<FindPosSalesQuery, IReadOnlyList<PosSaleMatchDto>>
{
    public async Task<IReadOnlyList<PosSaleMatchDto>> Handle(FindPosSalesQuery request, CancellationToken cancellationToken)
    {
        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceView, request.LocationId,
            cancellationToken);

        var query = db.Invoices.Where(x => x.OrganizationId == request.OrganizationId
            && x.LocationId == request.LocationId && x.Channel == SalesChannel.Pos
            && x.Status == InvoiceStatus.Approved);

        var term = request.Search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            // String.Contains, which SQL Server turns into LIKE and InMemory can evaluate (the
            // EF.Functions.Like gotcha); case-insensitive on SQL Server by collation.
            query = query.Where(x => x.Code.Contains(term));
        }

        var rows = await query
            .OrderByDescending(x => x.ApprovedAt)
            .Take(FindPosSalesQuery.MaxResults)
            .Select(x => new SaleRow(x.Id, x.Code, x.Date, x.ApprovedAt, x.PosSessionId, x.ContactId, x.RoundOff, x.LocationId))
            .ToListAsync(cancellationToken);

        // Phase 84 -- another branch's sale, named in full, to a caller who may refund here.
        if (!string.IsNullOrEmpty(term) && await MayRefundHereAsync(request, cancellationToken))
        {
            var away = await db.Invoices
                .Where(x => x.OrganizationId == request.OrganizationId && x.Code == term
                    && x.LocationId != request.LocationId && x.Channel == SalesChannel.Pos
                    && x.Status == InvoiceStatus.Approved)
                .Select(x => new SaleRow(x.Id, x.Code, x.Date, x.ApprovedAt, x.PosSessionId, x.ContactId, x.RoundOff, x.LocationId))
                .SingleOrDefaultAsync(cancellationToken);

            if (away is not null)
            {
                rows.Insert(0, away);
            }
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(x => x.Id).ToList();
        var lineSums = await db.InvoiceLines
            .Where(x => ids.Contains(x.InvoiceId))
            .GroupBy(x => x.InvoiceId)
            .Select(g => new
            {
                InvoiceId = g.Key,
                Total = g.Sum(x => x.Amount + x.ServiceChargeAmount + x.PackagingFeeAmount + x.VatAmount),
                Fees = g.Sum(x => x.ServiceChargeAmount + x.PackagingFeeAmount),
                Sold = g.Sum(x => x.Quantity),
            })
            .ToDictionaryAsync(x => x.InvoiceId, cancellationToken);

        // What the sale's returns gave back, by the rule the cap counts (SalesValidation.GetInvoiceRemainingByLineAsync):
        // a return's lines under every note not voided. A till refund copies its sale line's unit, so the two sums
        // are in the same units, and no key can be returned past what it sold, so "nothing left" is one comparison.
        var returned = await db.CreditNotes
            .Where(x => x.OrganizationId == request.OrganizationId && x.ReferrerType == DocumentType.Invoice
                && x.ReferrerId != null && ids.Contains(x.ReferrerId.Value) && x.Status != CreditNoteStatus.Void)
            .SelectMany(x => x.Lines, (note, line) => new { InvoiceId = note.ReferrerId!.Value, line.Quantity })
            .GroupBy(x => x.InvoiceId)
            .Select(g => new { InvoiceId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToDictionaryAsync(x => x.InvoiceId, x => x.Quantity, cancellationToken);

        var sessionIds = rows.Select(x => x.PosSessionId).OfType<Guid>().Distinct().ToList();
        var sessionCodes = await db.PosSessions
            .Where(x => x.OrganizationId == request.OrganizationId && sessionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var contactIds = rows.Select(x => x.ContactId).Distinct().ToList();
        var contacts = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => new { x.Name, x.IsWalkInCustomer }, cancellationToken);

        var awayLocationIds = rows.Where(x => x.LocationId != request.LocationId)
            .Select(x => x.LocationId).OfType<Guid>().Distinct().ToList();
        var locationNames = awayLocationIds.Count == 0
            ? []
            : await db.BillingLocations
                .Where(x => x.OrganizationId == request.OrganizationId && awayLocationIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return rows
            .Select(x =>
            {
                var contact = contacts.GetValueOrDefault(x.ContactId);
                var sums = lineSums.GetValueOrDefault(x.Id);
                var isHere = x.LocationId == request.LocationId;
                return new PosSaleMatchDto(
                    x.Id,
                    x.Code,
                    x.Date,
                    x.ApprovedAt,
                    x.PosSessionId is { } s ? sessionCodes.GetValueOrDefault(s) : null,
                    contact?.Name ?? "",
                    contact?.IsWalkInCustomer ?? false,
                    (sums?.Total ?? 0m) + x.RoundOff,
                    isHere ? null : x.LocationId is { } l ? locationNames.GetValueOrDefault(l, "") : "",
                    (sums?.Sold ?? 0m) - returned.GetValueOrDefault(x.Id) <= 0m,
                    // The planner's rule (PosRefundPlanner.EnsureRefundable), read from the same figures.
                    isHere || (sums?.Fees ?? 0m) == 0m);
            })
            .ToList();
    }

    /// <summary>The two keys a refund at this location asks for (phase 63 Decision E).</summary>
    private async Task<bool> MayRefundHereAsync(FindPosSalesQuery request, CancellationToken cancellationToken) =>
        await GrantedPermissionReader.IsGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.CreditNoteCreate, request.LocationId,
            cancellationToken)
        && await GrantedPermissionReader.IsGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.CreditNoteApprove, request.LocationId,
            cancellationToken);

    private sealed record SaleRow(
        Guid Id, string Code, DateOnly Date, DateTimeOffset? ApprovedAt, Guid? PosSessionId, Guid ContactId,
        decimal RoundOff, Guid? LocationId);
}
