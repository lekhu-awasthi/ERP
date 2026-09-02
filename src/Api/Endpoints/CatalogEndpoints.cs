using ErpApp.Application.Catalog.Commands.AddSecondaryUnit;
using ErpApp.Application.Catalog.Commands.AddVariantAttributeOption;
using ErpApp.Application.Catalog.Commands.CreateProductVariant;
using ErpApp.Application.Catalog.Commands.CreateVariantAttribute;
using ErpApp.Application.Catalog.Commands.DeleteProductVariant;
using ErpApp.Application.Catalog.Commands.GenerateProductVariants;
using ErpApp.Application.Catalog.Commands.SetProductVariantAttributes;
using ErpApp.Application.Catalog.Commands.UpdateProductVariant;
using ErpApp.Application.Catalog.Commands.UpdateVariantAttribute;
using ErpApp.Application.Catalog.Commands.UpdateVariantAttributeOption;
using ErpApp.Application.Catalog.Commands.CreateProduct;
using ErpApp.Application.Catalog.Commands.CreateProductCategory;
using ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;
using ErpApp.Application.Catalog.Commands.UpdateProduct;
using ErpApp.Application.Catalog.Commands.UpdateProductCategory;
using ErpApp.Application.Catalog.Commands.UpdateUnitOfMeasurement;
using ErpApp.Application.Catalog.Queries.GetProduct;
using ErpApp.Application.Catalog.Queries.ListProductVariants;
using ErpApp.Application.Catalog.Queries.ListProducts;
using ErpApp.Application.Catalog.Queries.ListVariantAttributes;
using ErpApp.Application.Catalog.Variants;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Catalog")
            .RequireAuthorization();

        MapProductCategoryEndpoints(group);
        MapUnitOfMeasurementEndpoints(group);
        MapProductEndpoints(group);
        MapVariantAttributeEndpoints(group);
        MapProductVariantEndpoints(group);
    }

    private static void MapProductCategoryEndpoints(RouteGroupBuilder group)
    {
        group.MapGet(
            "/product-categories", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<ProductCategory>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/product-categories", async (
            Guid organizationId, CreateProductCategoryRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateProductCategoryCommand(organizationId, request.Name, request.ParentCategoryId), ct);
            return Results.Created($"/api/organizations/{organizationId}/product-categories/{result.Id}", result);
        });

        group.MapPut("/product-categories/{id:guid}", async (
            Guid organizationId, Guid id, UpdateProductCategoryRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateProductCategoryCommand(organizationId, id, request.Name, request.ParentCategoryId, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/product-categories/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<ProductCategory>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapUnitOfMeasurementEndpoints(RouteGroupBuilder group)
    {
        group.MapGet(
            "/units-of-measurement", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<UnitOfMeasurement>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/units-of-measurement", async (
            Guid organizationId, CreateUnitOfMeasurementRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateUnitOfMeasurementCommand(organizationId, request.Name, request.ShortName), ct);
            return Results.Created($"/api/organizations/{organizationId}/units-of-measurement/{result.Id}", result);
        });

        group.MapPut("/units-of-measurement/{id:guid}", async (
            Guid organizationId, Guid id, UpdateUnitOfMeasurementRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateUnitOfMeasurementCommand(organizationId, id, request.Name, request.ShortName, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/units-of-measurement/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<UnitOfMeasurement>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapProductEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/products", async (
            Guid organizationId, ProductType? type, ProductVariantFilter? variantFilter, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListProductsQuery(
                    organizationId, type, variantFilter ?? ProductVariantFilter.All,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/products/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetProductQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/products", async (
            Guid organizationId, CreateProductRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateProductCommand(
                    organizationId, request.Type, request.Name, request.CategoryId, request.PrimaryUnitId,
                    request.HsCode, request.AvailableForSale, request.SellingPrice, request.PurchasePrice,
                    request.VatRate, request.ReOrderLevel, request.TrackInventory, request.Sku, request.Barcode),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/products/{result.Id}", result);
        });

        group.MapPut("/products/{id:guid}", async (
            Guid organizationId, Guid id, UpdateProductRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateProductCommand(
                    organizationId, id, request.Name, request.CategoryId, request.PrimaryUnitId, request.HsCode,
                    request.AvailableForSale, request.SellingPrice, request.PurchasePrice, request.VatRate,
                    request.ReOrderLevel, request.TrackInventory, request.IsActive,
                    request.SalesAccountId, request.SalesReturnAccountId, request.PurchaseAccountId, request.PurchaseReturnAccountId,
                    request.Sku, request.Barcode),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/products/{id:guid}/secondary-units", async (
            Guid organizationId, Guid id, AddSecondaryUnitRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new AddSecondaryUnitCommand(
                    organizationId, id, request.UnitId, request.ConversionRate, request.SellingPrice, request.PurchasePrice),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/products/{id}/secondary-units/{result.Id}", result);
        });
    }

    private static void MapVariantAttributeEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/variant-attributes", async (
            Guid organizationId, bool? activeOnly, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListVariantAttributesQuery(
                    organizationId, activeOnly ?? false, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/variant-attributes", async (
            Guid organizationId, CreateVariantAttributeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateVariantAttributeCommand(organizationId, request.Name, request.Options ?? []), ct);
            return Results.Created($"/api/organizations/{organizationId}/variant-attributes/{result.Id}", result);
        });

        group.MapPut("/variant-attributes/{id:guid}", async (
            Guid organizationId, Guid id, UpdateVariantAttributeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateVariantAttributeCommand(organizationId, id, request.Name, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapPost("/variant-attributes/{id:guid}/options", async (
            Guid organizationId, Guid id, AddVariantAttributeOptionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddVariantAttributeOptionCommand(organizationId, id, request.Value), ct);
            return Results.Ok(result);
        });

        group.MapPut("/variant-attributes/{id:guid}/options/{optionId:guid}", async (
            Guid organizationId, Guid id, Guid optionId, UpdateVariantAttributeOptionRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateVariantAttributeOptionCommand(organizationId, id, optionId, request.Value, request.IsActive), ct);
            return Results.Ok(result);
        });
    }

    private static void MapProductVariantEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/products/{id:guid}/variants", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListProductVariantsQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPut("/products/{id:guid}/variant-attributes", async (
            Guid organizationId, Guid id, SetProductVariantAttributesRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SetProductVariantAttributesCommand(organizationId, id, request.Usages ?? []), ct);
            return Results.Ok(result);
        });

        group.MapPost("/products/{id:guid}/variants", async (
            Guid organizationId, Guid id, CreateProductVariantRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateProductVariantCommand(
                    organizationId, id, request.Combination ?? [], request.Name, request.Sku, request.Barcode,
                    request.SellingPrice, request.PurchasePrice),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/products/{result.Id}", result);
        });

        group.MapPost("/products/{id:guid}/variants/generate", async (
            Guid organizationId, Guid id, GenerateProductVariantsRequest? request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GenerateProductVariantsCommand(organizationId, id, request?.Options), ct);
            return Results.Ok(result);
        });

        group.MapPut("/products/{id:guid}/variants/{variantId:guid}", async (
            Guid organizationId, Guid id, Guid variantId, UpdateProductVariantRequest request,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateProductVariantCommand(
                    organizationId, variantId, request.Name, request.Sku, request.Barcode,
                    request.SellingPrice, request.PurchasePrice, request.IsActive),
                ct);
            return Results.Ok(result);
        });

        group.MapDelete("/products/{id:guid}/variants/{variantId:guid}", async (
            Guid organizationId, Guid id, Guid variantId, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteProductVariantCommand(organizationId, variantId), ct);
            return Results.NoContent();
        });
    }

    private sealed record CreateVariantAttributeRequest(string Name, IReadOnlyList<string>? Options);

    private sealed record UpdateVariantAttributeRequest(string Name, bool IsActive);

    private sealed record AddVariantAttributeOptionRequest(string Value);

    private sealed record UpdateVariantAttributeOptionRequest(string Value, bool IsActive);

    private sealed record SetProductVariantAttributesRequest(IReadOnlyList<VariantCombinationInput>? Usages);

    private sealed record CreateProductVariantRequest(
        IReadOnlyList<VariantCombinationInput>? Combination, string? Name, string? Sku, string? Barcode,
        decimal SellingPrice, decimal PurchasePrice);

    private sealed record GenerateProductVariantsRequest(IReadOnlyList<VariantCombinationInput>? Options);

    private sealed record UpdateProductVariantRequest(
        string Name, string? Sku, string? Barcode, decimal SellingPrice, decimal PurchasePrice, bool IsActive);

    private sealed record CreateProductCategoryRequest(string Name, Guid? ParentCategoryId);

    private sealed record UpdateProductCategoryRequest(string Name, Guid? ParentCategoryId, bool IsActive);

    private sealed record CreateUnitOfMeasurementRequest(string Name, string ShortName);

    private sealed record UpdateUnitOfMeasurementRequest(string Name, string ShortName, bool IsActive);

    private sealed record CreateProductRequest(
        ProductType Type, string Name, Guid CategoryId, Guid PrimaryUnitId, string? HsCode, bool AvailableForSale,
        decimal SellingPrice, decimal PurchasePrice, VatRate VatRate, int ReOrderLevel, bool TrackInventory,
        string? Sku, string? Barcode);

    private sealed record UpdateProductRequest(
        string Name, Guid CategoryId, Guid PrimaryUnitId, string? HsCode, bool AvailableForSale,
        decimal SellingPrice, decimal PurchasePrice, VatRate VatRate, int ReOrderLevel, bool TrackInventory, bool IsActive,
        Guid? SalesAccountId, Guid? SalesReturnAccountId, Guid? PurchaseAccountId, Guid? PurchaseReturnAccountId,
        string? Sku, string? Barcode);

    private sealed record AddSecondaryUnitRequest(Guid UnitId, decimal ConversionRate, decimal SellingPrice, decimal PurchasePrice);
}
