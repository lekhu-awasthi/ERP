using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Refunds;
using ErpApp.Application.Pos.Sessions;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Pos.Queries.PreviewPosRefund;

public sealed class PreviewPosRefundQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PreviewPosRefundQuery, PosRefundPreviewDto>
{
    /// <summary>The note the planner builds needs a reason (the aggregate refuses a refund without
    /// one); a preview is never saved, so this stands in for the one the cashier types.</summary>
    private const string PreviewReason = "Preview";

    public async Task<PosRefundPreviewDto> Handle(PreviewPosRefundQuery request, CancellationToken cancellationToken)
    {
        var session = await PosSessionAccess.LoadOwnOpenAsync(
            db, request.OrganizationId, request.SessionId, currentUser.UserId, cancellationToken);

        var till = await PosTill.LoadAsync(db, request.OrganizationId, session.BillingLocationId, cancellationToken);

        await GrantedPermissionReader.EnsureGrantedAtLocationAsync(
            db, request.OrganizationId, currentUser.UserId, PermissionKeys.CreditNoteCreate, till.Location.Id,
            cancellationToken);

        var plan = await PosRefundPlanner.PlanAsync(
            db, request.OrganizationId, till, session.Id, request.InvoiceId, request.Lines, PreviewReason,
            NepalTime.LocalDate(DateTimeOffset.UtcNow), cancellationToken);

        var note = plan.CreditNote;

        // The planner adds lines in the sale's order; map them back to the sale line each returns by
        // position in that same order.
        var chosenIds = plan.Invoice.Lines
            .Select(x => x.Id)
            .Where(id => request.Lines.Any(l => l.InvoiceLineId == id))
            .ToList();

        return new PosRefundPreviewDto(
            [.. note.Lines.Select((x, i) => new PosRefundPreviewLineDto(
                chosenIds[i], x.Quantity, x.Amount, x.ServiceChargeAmount, x.VatAmount, x.LineTotal))],
            note.Lines.Sum(x => x.Amount),
            note.ServiceChargeTotal,
            note.Lines.Sum(x => x.VatAmount),
            note.RoundOff,
            note.GrandTotal,
            plan.OwedBefore,
            plan.RequiredPayout,
            note.GrandTotal - plan.RequiredPayout);
    }
}
