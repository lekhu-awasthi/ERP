using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Queries.FindPosSales;

public sealed record PosSaleMatchDto(
    Guid InvoiceId,
    string Code,
    DateOnly Date,
    DateTimeOffset? SoldAt,
    string? SessionCode,
    string CustomerName,
    bool IsWalkIn,
    decimal GrandTotal,
    string? SoldAtLocationName,
    bool FullyRefunded,
    bool RefundableHere);

/// <summary>
/// Phase 63 -- the refund screen's "find a sale": approved till sales of one location whose number
/// contains <see cref="Search"/>, newest first, at most <see cref="MaxResults"/>. A receipt's number is
/// what a returning customer hands over; the session's own sales are listed by
/// <c>ListPosSessionSalesQuery</c>.
///
/// <para><b>Permission: <c>Sales.Invoice.View</c> at that location</b>, re-checked in the handler
/// against <see cref="LocationId"/> because the rows are invoices of that branch -- the rule the
/// receipt print follows (phase 62 Decision B). Hence <see cref="ILocationFilteredQuery"/>: the
/// pipeline admits a caller holding the key at any location, and the handler narrows to the one asked
/// for, refusing it with the key's own 403 when it is not one of theirs.</para>
///
/// <para><b>Phase 84 -- another branch's sale, by its full number only.</b> When <see cref="Search"/> is exactly the
/// number of an approved till sale of another location, that sale is listed too (<c>SoldAtLocationName</c> says
/// where), to a caller who may refund here: <c>Sales.CreditNote.Create</c> and <c>.Approve</c> at this location, the
/// two keys the refund itself asks for. No grant at the selling branch is needed; the customer is holding the bill.
/// There is no browsing of another branch's sales, and a caller without those keys simply finds nothing more.
/// <c>RefundableHere</c> is false for one carrying a service charge or packaging fee (it is refunded where it was
/// sold), and <c>FullyRefunded</c> marks a sale with nothing left to give back.</para>
/// </summary>
public sealed record FindPosSalesQuery(Guid OrganizationId, Guid LocationId, string? Search)
    : IRequest<IReadOnlyList<PosSaleMatchDto>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature,
        ILocationFilteredQuery
{
    public const int MaxResults = 20;
    public const int MaxSearchLength = 50;

    public string PermissionKey => PermissionKeys.InvoiceView;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class FindPosSalesQueryValidator : AbstractValidator<FindPosSalesQuery>
{
    public FindPosSalesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.Search).MaximumLength(FindPosSalesQuery.MaxSearchLength);
    }
}
