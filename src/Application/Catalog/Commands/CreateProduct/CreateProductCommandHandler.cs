using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.CreateProduct;

public sealed class CreateProductCommandHandler(IAppDbContext db, IDocumentNumberGenerator numberGenerator)
    : IRequestHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<CreateProductResult> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var categoryExists = await db.ProductCategories.AnyAsync(
            x => x.Id == request.CategoryId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!categoryExists)
        {
            throw new NotFoundException("Product category not found.");
        }

        var unitExists = await db.UnitsOfMeasurement.AnyAsync(
            x => x.Id == request.PrimaryUnitId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!unitExists)
        {
            throw new NotFoundException("Primary unit of measurement not found.");
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Product, cancellationToken);

        var product = Product.Create(
            request.OrganizationId,
            request.Type,
            request.Name,
            code,
            request.CategoryId,
            request.PrimaryUnitId,
            request.HsCode,
            request.AvailableForSale,
            request.SellingPrice,
            request.PurchasePrice,
            request.VatRate,
            request.ReOrderLevel,
            request.TrackInventory);

        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateProductResult(product.Id, product.Code, product.Type, product.Name);
    }
}
