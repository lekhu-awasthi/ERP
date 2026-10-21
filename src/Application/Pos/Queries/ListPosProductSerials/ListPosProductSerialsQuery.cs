using ErpApp.Application.Inventory.Stock;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosProductSerials;

/// <summary>
/// Phase 91 -- the serials of one product in stock at the warehouse a till sells from: what the Retail till's picker
/// offers when a cashier taps a serial-tracked product (phase-91-status.md Decision B). The vendor's till offers no
/// list, only a box to scan or type into; ours offers both, because a cashier with the box in hand and no scanner
/// should not have to read an IMEI off a sticker.
///
/// <para>A serial in stock is a FIFO layer of quantity one with <c>QuantityRemaining</c> left (phase 51), so this is
/// the layers, oldest first, which is the order FIFO would have taken them in. Under the cashier's key
/// (<c>Pos.Session.Operate</c>), as the grid is, not <c>Catalog.Product.View</c>, which the product page's Serial
/// tab rides: a till role sells what it is given without the catalogue.</para>
/// </summary>
public sealed record ListPosProductSerialsQuery(
    Guid OrganizationId,
    Guid LocationId,
    Guid ProductId,
    string? Search = null)
    : IRequest<IReadOnlyList<string>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    /// <summary>Enough for a counter's shelf; a longer list is narrowed by typing part of the serial.</summary>
    public const int MaxSerials = 200;

    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class ListPosProductSerialsQueryValidator : AbstractValidator<ListPosProductSerialsQuery>
{
    public ListPosProductSerialsQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Search).MaximumLength(DocumentLineSerial.SerialNoMaxLength);
    }
}

public sealed class ListPosProductSerialsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPosProductSerialsQuery, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(
        ListPosProductSerialsQuery request, CancellationToken cancellationToken)
    {
        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);

        var tracked = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && x.Id == request.ProductId)
            .Select(x => (bool?)x.SerialTracking)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        // Phase 95 -- the company's switch off overrides the product's own: nothing to pick.
        tracked = tracked && (await StockTrackingSwitches.LoadAsync(db, request.OrganizationId, cancellationToken)).Serial;

        // A till with no warehouse sells nothing that keeps stock (PosSaleCompletion.ResolveWarehouseAsync's 409), and
        // an untracked product has no serials: both are an empty list, not an error, so the picker says so.
        if (!tracked || till.Location.WarehouseId is not { } warehouseId)
        {
            return [];
        }

        var query = db.StockLedgerEntries
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId
                && x.WarehouseId == warehouseId && x.SerialNo != null && x.QuantityRemaining > 0);

        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            query = query.Where(x => x.SerialNo!.Contains(term));
        }

        return await query
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.SerialNo)
            .Select(x => x.SerialNo!)
            .Take(ListPosProductSerialsQuery.MaxSerials)
            .ToListAsync(cancellationToken);
    }
}
