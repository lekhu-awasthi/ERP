using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Filtering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Pos.Queries.ListPosProductBatches;

/// <summary>
/// Phase 94 -- the batches of one product in stock at the warehouse a till sells from: what the Retail till's picker
/// offers when a cashier taps or scans a batch-tracked product. The vendor's till (read live 2026-10-20) refuses a
/// batch-tracked line with no batch ("Select a batch to continue") and offers every batch of the product with its
/// expiry, soonest first; its "Balance: 0" is a field its list never carries. Ours offers the batches this warehouse
/// still holds, with what is left of each, which is the question a cashier is asking.
///
/// <para>A batch row stores no quantity (phase 51): what is left is <c>SUM(QuantityRemaining)</c> over its layers
/// here, a shortfall layer included, so the picker and Stock Position cannot disagree. Ordered by expiry (none last),
/// then by when the batch was first received, which is the order a shop sells perishable stock in. Under the
/// cashier's key (<c>Pos.Session.Operate</c>), as phase 91's serial list is, not <c>Catalog.Product.View</c>.</para>
/// </summary>
public sealed record ListPosProductBatchesQuery(
    Guid OrganizationId,
    Guid LocationId,
    Guid ProductId,
    string? Search = null)
    : IRequest<IReadOnlyList<PosProductBatchDto>>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature
{
    /// <summary>Enough for a counter's shelf; a longer list is narrowed by typing part of the batch number.</summary>
    public const int MaxBatches = 200;

    public string PermissionKey => PermissionKeys.PosSessionOperate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

/// <summary>One batch in stock at the till's warehouse; <paramref name="Quantity"/> is in the product's primary
/// unit.</summary>
public sealed record PosProductBatchDto(string BatchNo, DateOnly? ExpiryDate, decimal Quantity);

public sealed class ListPosProductBatchesQueryValidator : AbstractValidator<ListPosProductBatchesQuery>
{
    public ListPosProductBatchesQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Search).MaximumLength(ProductBatch.BatchNoMaxLength);
    }
}

public sealed class ListPosProductBatchesQueryHandler(IAppDbContext db)
    : IRequestHandler<ListPosProductBatchesQuery, IReadOnlyList<PosProductBatchDto>>
{
    public async Task<IReadOnlyList<PosProductBatchDto>> Handle(
        ListPosProductBatchesQuery request, CancellationToken cancellationToken)
    {
        var till = await PosTill.LoadAsync(db, request.OrganizationId, request.LocationId, cancellationToken);

        var tracked = await db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && x.Id == request.ProductId)
            .Select(x => (bool?)x.BatchTracking)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        // As the serial list: a till with no warehouse sells nothing that keeps stock, and an untracked product has no
        // batches. Both are an empty list, so the picker says there is nothing to choose.
        if (!tracked || till.Location.WarehouseId is not { } warehouseId)
        {
            return [];
        }

        var batches = db.ProductBatches
            .Where(x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId);

        if (SearchTerm.Normalize(request.Search) is { } term)
        {
            batches = batches.Where(x => x.BatchNo.Contains(term));
        }

        // Every non-zero layer of the product's batches here, a shortfall's negative one included, so a batch that
        // was oversold reads as what it really holds. Joined as a query, never a list of ids (phase 42).
        var layers = await db.StockLedgerEntries
            .AsNoTracking()
            .Where(x => x.OrganizationId == request.OrganizationId && x.ProductId == request.ProductId
                && x.WarehouseId == warehouseId && x.BatchId != null && x.QuantityRemaining != 0
                && batches.Any(b => b.Id == x.BatchId))
            .Select(x => new { BatchId = x.BatchId!.Value, x.QuantityRemaining, x.CreatedAt })
            .ToListAsync(cancellationToken);

        // Summed in memory after ToListAsync: a store-side aggregate over a projected record is untranslatable, and
        // InMemory would evaluate it anyway (phase 42).
        var held = layers
            .GroupBy(x => x.BatchId)
            .Select(g => new { BatchId = g.Key, Quantity = g.Sum(x => x.QuantityRemaining), First = g.Min(x => x.CreatedAt) })
            .Where(x => x.Quantity > 0)
            .ToDictionary(x => x.BatchId);

        if (held.Count == 0)
        {
            return [];
        }

        // One product's batches with stock at one warehouse: a shelf's worth of ids, not a period's.
        var heldIds = held.Keys.ToList();
        var rows = await batches
            .Where(x => heldIds.Contains(x.Id))
            .Select(x => new { x.Id, x.BatchNo, x.ExpiryDate })
            .ToListAsync(cancellationToken);

        return rows
            .OrderBy(x => x.ExpiryDate ?? DateOnly.MaxValue)
            .ThenBy(x => held[x.Id].First)
            .ThenBy(x => x.BatchNo, StringComparer.Ordinal)
            .Take(ListPosProductBatchesQuery.MaxBatches)
            .Select(x => new PosProductBatchDto(x.BatchNo, x.ExpiryDate, held[x.Id].Quantity))
            .ToList();
    }
}
