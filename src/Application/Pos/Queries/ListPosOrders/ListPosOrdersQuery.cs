using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosOrders;

/// <summary>
/// Phase 64 -- the ERP's <i>POS Orders</i> list: every restaurant order, open or voided, with its lines
/// and kitchen tickets, read-only. It is the surface phase 49's rule says a chosen divergence owes. The
/// vendor's open tab is a Sales Order, so its back office sees one in the Sales Orders list; ours is a
/// <see cref="PosOrder"/>, and without this an open tab would be invisible to everyone not standing at
/// the till (phase-59-status.md Decision E).
///
/// <para>The document-list shape every sales list has: status, search (code), the shell's date range,
/// the location filter and the two indexed orderings. Rows carry their lines and tickets, built after
/// the page is fetched (phase 42), so the list expands a row without a second endpoint.</para>
///
/// <para><b>Scoped to where the caller may view invoices</b>: an order is billed as an Invoice at its
/// location, so a user scoped to one branch's invoices sees that branch's orders and no other.</para>
/// </summary>
public sealed record ListPosOrdersQuery(
    Guid OrganizationId,
    PosOrderStatus? Status = null,
    PosTab? OrderType = null,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    string? Search = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    Guid? LocationId = null,
    string? Sort = null)
    : IRequest<PagedResult<PosOrderDto>>, IRequirePermission, IOrganizationScoped, IRequireFeature,
        ILocationFilteredQuery, ISearchableQuery, IDateRangeFilteredQuery, ISortableQuery
{
    public string PermissionKey => PermissionKeys.PosOrderView;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class ListPosOrdersQueryValidator : AbstractValidator<ListPosOrdersQuery>
{
    public ListPosOrdersQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.OrderType)
            .Must(x => x is null or PosTab.DineIn or PosTab.TakeAway or PosTab.Delivery)
            .WithMessage("An order is Dine In, Take Away or Delivery.");
        this.ValidatePaging(x => x.Page, x => x.PageSize);
        this.ValidateSearch(x => x.Search);
        this.ValidateDateRange(x => x.FromDate, x => x.ToDate);
        this.ValidateSort(x => x.Sort, ListSort.DocumentOrderings);
    }
}

public sealed class ListPosOrdersQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<ListPosOrdersQuery, PagedResult<PosOrderDto>>
{
    public async Task<PagedResult<PosOrderDto>> Handle(ListPosOrdersQuery request, CancellationToken cancellationToken)
    {
        var query = db.PosOrders.Where(x => x.OrganizationId == request.OrganizationId);

        // Decision F: the branches the caller may view invoices at, read the way every document list
        // reads its own key -- a role granted Sales.Invoice.View at some locations sees those, and an
        // organization-wide role is not blinded (LocationAccessScope's rule).
        var allowedLocations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, request.OrganizationId, PermissionKeys.InvoiceView, cancellationToken);
        if (allowedLocations is not null)
        {
            query = query.Where(x => allowedLocations.Contains(x.BillingLocationId));
        }

        if (request.LocationId is { } locationId)
        {
            query = query.Where(x => x.BillingLocationId == locationId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(x => x.Status == status);
        }

        if (request.OrderType is { } orderType)
        {
            query = query.Where(x => x.OrderType == orderType);
        }

        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.Code.Contains(term));
        }

        if (request.FromDate is { } fromDate)
        {
            query = query.Where(x => x.Date >= fromDate);
        }

        if (request.ToDate is { } toDate)
        {
            query = query.Where(x => x.Date <= toDate);
        }

        IOrderedQueryable<PosOrder> Order(IQueryable<PosOrder> source) => request.Sort switch
        {
            ListSort.DocumentDate => source.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt),
            _ => source.OrderByDescending(x => x.CreatedAt),
        };

        var page = await query.ToKeyPagedResultAsync(x => x.Id, Order, request.Page, request.PageSize, cancellationToken);

        if (page.Items.Count == 0)
        {
            return new PagedResult<PosOrderDto>([], page.Page, page.PageSize, page.TotalCount);
        }

        // The page's lines and tickets, fetched by the page's ids (at most a page of them), in page order.
        var ids = page.Items.Select(x => x.Id).ToList();
        var full = await db.PosOrders
            .AsNoTracking()
            .Include(x => x.Lines)
            .Include(x => x.Tickets).ThenInclude(x => x.Lines)
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var ordered = ids.Select(id => full.Single(x => x.Id == id)).ToList();

        var rows = await PosOrderView.ReadManyAsync(db, request.OrganizationId, ordered, cancellationToken);
        return new PagedResult<PosOrderDto>(rows, page.Page, page.PageSize, page.TotalCount);
    }
}
