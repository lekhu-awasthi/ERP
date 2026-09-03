using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Trade.Queries.TradeByItem;

/// <summary>
/// Groups <see cref="TradeLineReader"/>'s facts by product, or by the product's category when the
/// live "Filter By item/category" control asks for that. The Product and Product Category filters
/// narrow which facts are counted; the grouping decides what a row is. The two are independent --
/// filtering to one category and grouping by Item is a legitimate run, and gives that category's
/// products one row each.
/// </summary>
public sealed class TradeByItemQueryHandler(IAppDbContext db)
    : IRequestHandler<TradeByItemQuery, TradeByItemDto>
{
    public async Task<TradeByItemDto> Handle(TradeByItemQuery request, CancellationToken cancellationToken)
    {
        var facts = await TradeLineReader.LoadAsync(
            db, request.OrganizationId, request.Side, request.FromDate, request.ToDate, cancellationToken);

        var productIds = facts.Select(x => x.ProductId).Distinct().ToList();

        var productsQuery = db.Products
            .Where(x => x.OrganizationId == request.OrganizationId && productIds.Contains(x.Id));

        if (request.ProductId is { } onlyProduct)
        {
            productsQuery = productsQuery.Where(x => x.Id == onlyProduct);
        }

        if (request.ProductCategoryId is { } onlyCategory)
        {
            productsQuery = productsQuery.Where(x => x.CategoryId == onlyCategory);
        }

        var products = await productsQuery
            .Select(x => new { x.Id, x.Code, x.Name, x.CategoryId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var categoryIds = products.Values.Select(x => x.CategoryId).Distinct().ToList();
        var categoryNames = await db.ProductCategories
            .Where(x => categoryIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var inScope = facts.Where(x => products.ContainsKey(x.ProductId)).ToList();

        var rows = request.GroupBy == TradeItemGrouping.Category
            ? inScope
                .GroupBy(x => products[x.ProductId].CategoryId)
                .Select(g => BuildRow(g.Key, null, categoryNames.GetValueOrDefault(g.Key) ?? string.Empty, g))
                .ToList()
            : inScope
                .GroupBy(x => x.ProductId)
                .Select(g => BuildRow(g.Key, products[g.Key].Code, products[g.Key].Name, g))
                .ToList();

        rows = [.. rows
            .Where(x => x.Quantity != 0 || x.Amount != 0 || x.Discount != 0
                || x.NetAmount != 0 || x.VatAmount != 0 || x.TotalAmount != 0)
            .OrderBy(x => x.Name, StringComparer.Ordinal)];

        var paged = request.ExportAll ? rows.ToUnpagedResult() : rows.ToPagedResult(request.Page, request.PageSize);

        return new TradeByItemDto(
            request.Side,
            request.GroupBy,
            request.FromDate,
            request.ToDate,
            paged.Items,
            paged.Page,
            paged.PageSize,
            paged.TotalCount,
            rows.Sum(x => x.Amount),
            rows.Sum(x => x.Discount),
            rows.Sum(x => x.NetAmount),
            rows.Sum(x => x.VatAmount),
            rows.Sum(x => x.TotalAmount));
    }

    private static TradeByItemRowDto BuildRow(
        Guid id, string? code, string name, IEnumerable<TradeLineReader.Fact> facts)
    {
        var list = facts as IReadOnlyCollection<TradeLineReader.Fact> ?? [.. facts];

        return new TradeByItemRowDto(
            id,
            code,
            name,
            list.Sum(x => x.Quantity),
            list.Sum(x => x.Amount),
            list.Sum(x => x.Discount),
            list.Sum(x => x.NetAmount),
            list.Sum(x => x.VatAmount),
            list.Sum(x => x.TotalAmount));
    }
}
