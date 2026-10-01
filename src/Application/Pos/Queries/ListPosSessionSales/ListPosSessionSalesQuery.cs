using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Queries.ListPosSessionSales;

/// <summary>One sale rung up in a session, as the till's recent-sales list shows it.</summary>
/// <param name="PrintCount">How many times its receipt has been printed. The next print is a copy
/// whenever this is above zero (phase-62-status.md Decision B).</param>
public sealed record PosSessionSaleDto(
    Guid InvoiceId,
    string Code,
    InvoiceStatus Status,
    DateTimeOffset? SoldAt,
    string CustomerName,
    bool IsWalkIn,
    decimal GrandTotal,
    decimal Tendered,
    decimal ChangeAmount,
    decimal CreditAmount,
    bool IsAbbreviatedTaxInvoice,
    int PrintCount);

/// <summary>
/// Phase 62 -- the sales of one session, newest first: what the till reprints from, and the rows
/// beneath its X report. Read on the session's own terms (<c>PosSessionAccess.LoadReadableAsync</c>):
/// your own session, or anyone's with <c>Pos.Session.ViewAll</c>.
///
/// <para>Not paged. A session is one cashier's shift, and its sales are what the shift's own
/// figures are summed from (<c>PosSalesReader</c>); a list that showed a page of them beside a total
/// over all of them would be phase 16c's footer bug.</para>
/// </summary>
public sealed record ListPosSessionSalesQuery(Guid OrganizationId, Guid SessionId)
    : IRequest<IReadOnlyList<PosSessionSaleDto>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}
