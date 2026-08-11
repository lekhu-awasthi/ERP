using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Catalog;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.CreateProductCategory;

public sealed class CreateProductCategoryCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateProductCategoryCommand, CreateProductCategoryResult>
{
    public async Task<CreateProductCategoryResult> Handle(CreateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.ProductCategories.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A product category named '{request.Name}' already exists.");
        }

        if (request.ParentCategoryId is { } parentCategoryId)
        {
            var parentExists = await db.ProductCategories.AnyAsync(
                x => x.Id == parentCategoryId && x.OrganizationId == request.OrganizationId, cancellationToken);

            if (!parentExists)
            {
                throw new NotFoundException("Parent product category not found.");
            }
        }

        var category = ProductCategory.Create(request.OrganizationId, request.Name, request.ParentCategoryId);
        db.ProductCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateProductCategoryResult(category.Id, category.Name, category.ParentCategoryId);
    }
}
