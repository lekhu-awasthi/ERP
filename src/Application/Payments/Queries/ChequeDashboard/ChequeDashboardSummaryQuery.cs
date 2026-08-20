using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Payments.Queries.ChequeDashboard;

/// <summary>Backs the Cheque Register's Dashboard tab counters (Received/Issued counts for the
/// selected Period + Customer/Supplier filter).</summary>
public sealed record ChequeDashboardSummaryQuery(
    Guid OrganizationId, DateOnly? FromDate = null, DateOnly? ToDate = null, Guid? ContactId = null)
    : IRequest<ChequeDashboardSummaryDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ChequeView;
}

public sealed record ChequeDashboardSummaryDto(int ReceivedCount, int IssuedCount);
