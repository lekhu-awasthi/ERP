using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.ChequeDashboard;

public sealed class ChequeDashboardSummaryQueryHandler(IAppDbContext db)
    : IRequestHandler<ChequeDashboardSummaryQuery, ChequeDashboardSummaryDto>
{
    public async Task<ChequeDashboardSummaryDto> Handle(ChequeDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var query = db.Cheques.Where(x => x.OrganizationId == request.OrganizationId);

        if (request.FromDate is { } fromDate)
        {
            query = query.Where(x => x.ChequeDate >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            query = query.Where(x => x.ChequeDate <= toDate);
        }

        if (request.ContactId is { } contactId)
        {
            query = query.Where(x => db.Payments.Any(p => p.Id == x.LinkedPaymentId && p.ContactId == contactId));
        }

        var receivedCount = await query.CountAsync(x => x.Direction == PaymentDirection.Received, cancellationToken);
        var issuedCount = await query.CountAsync(x => x.Direction == PaymentDirection.Paid, cancellationToken);

        return new ChequeDashboardSummaryDto(receivedCount, issuedCount);
    }
}
