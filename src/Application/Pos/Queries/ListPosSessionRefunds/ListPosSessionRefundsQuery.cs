using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.ListPosSessionRefunds;

/// <param name="InvoiceCode">The sale it returns.</param>
/// <param name="PaidOut">What was handed back over the counter.</param>
/// <param name="ToAccount">What came off what the customer owed instead.</param>
public sealed record PosSessionRefundDto(
    Guid CreditNoteId,
    string Code,
    CreditNoteStatus Status,
    DateTimeOffset? RefundedAt,
    Guid? InvoiceId,
    string? InvoiceCode,
    string CustomerName,
    bool IsWalkIn,
    string? Reason,
    decimal GrandTotal,
    decimal PaidOut,
    decimal ToAccount,
    int PrintCount);

/// <summary>Phase 63 -- the refunds paid out of one session's drawer, for the session page's list and
/// its reprint. Read under the same rule as the session's sales (<c>ListPosSessionSalesQuery</c>): your
/// own session, or anyone's with <c>Pos.Session.ViewAll</c>.</summary>
public sealed record ListPosSessionRefundsQuery(Guid OrganizationId, Guid SessionId)
    : IRequest<IReadOnlyList<PosSessionRefundDto>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
