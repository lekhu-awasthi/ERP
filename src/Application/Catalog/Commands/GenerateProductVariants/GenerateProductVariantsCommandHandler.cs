using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.GenerateProductVariants;

public sealed class GenerateProductVariantsCommandHandler(IAppDbContext db, IDocumentNumberGenerator numberGenerator)
    : IRequestHandler<GenerateProductVariantsCommand, GenerateProductVariantsResult>
{
    /// <summary>Decision C's cap. 4 attributes x 5 options is 625 rows from one click, and every
    /// row is a real Product carrying a document number off the tenant's own sequence -- so the
    /// answer to overshooting is to REFUSE and say the number, never to truncate. A silent partial
    /// matrix is the worst outcome available: the user cannot tell which combinations are missing.</summary>
    private const int MaxCombinationsPerRun = 200;

    public async Task<GenerateProductVariantsResult> Handle(
        GenerateProductVariantsCommand request, CancellationToken cancellationToken)
    {
        var parent = await db.Products
            .Include(x => x.VariantAttributeUsages)
            .SingleOrDefaultAsync(
                x => x.Id == request.ProductId && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product not found.");

        if (parent.ParentProductId is not null)
        {
            throw new ConflictException("This product is itself a variant, so it cannot have variants of its own.");
        }

        var catalog = await VariantCatalogLookup.LoadAsync(db, request.OrganizationId, cancellationToken);

        var pool = request.Options is { Count: > 0 }
            ? request.Options
            : parent.VariantAttributeUsages
                .Select(x => new VariantCombinationInput(x.VariantAttributeId, x.VariantAttributeOptionId))
                .ToList();

        if (pool.Count == 0)
        {
            throw new ConflictException(
                "This product has no attribute options selected, so there is nothing to generate.");
        }

        catalog.EnsureValid(pool);

        var byAttribute = pool
            .GroupBy(x => x.AttributeId)
            .Select(g => g.DistinctBy(x => x.OptionId).ToList())
            .ToList();

        var total = byAttribute.Aggregate(1L, (acc, options) => acc * options.Count);
        if (total > MaxCombinationsPerRun)
        {
            throw new ConflictException(
                $"That selection would generate {total} variants, more than the {MaxCombinationsPerRun} allowed in one run. " +
                "Narrow the attribute options and generate again.");
        }

        var created = new List<Product>();
        var skipped = 0;

        foreach (var combination in CartesianProduct(byAttribute))
        {
            var variant = await ProductVariantFactory.TryCreateAsync(
                db, numberGenerator, parent, combination, name: null, sku: null, barcode: null,
                parent.SellingPrice, parent.PurchasePrice, catalog, cancellationToken);

            if (variant is null)
            {
                skipped++;
            }
            else
            {
                created.Add(variant);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new GenerateProductVariantsResult(
            parent.Id,
            skipped,
            created.ConvertAll(x => ProductVariantMapper.ToResult(x, catalog.AttributeNames, catalog.OptionValues)));
    }

    /// <summary>Every combination taking exactly one option from each attribute. Iterative rather
    /// than recursive so the cap above is the only thing bounding it.</summary>
    private static List<List<VariantCombinationInput>> CartesianProduct(
        List<List<VariantCombinationInput>> byAttribute)
    {
        var result = new List<List<VariantCombinationInput>> { new List<VariantCombinationInput>() };

        foreach (var options in byAttribute)
        {
            var next = new List<List<VariantCombinationInput>>(result.Count * options.Count);

            foreach (var prefix in result)
            {
                foreach (var option in options)
                {
                    next.Add([.. prefix, option]);
                }
            }

            result = next;
        }

        return result;
    }
}
