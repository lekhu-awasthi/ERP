using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Catalog.Commands.UpdateProductCategory;

public sealed class UpdateProductCategoryCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateProductCategoryCommand, UpdateProductCategoryResult>
{
    public async Task<UpdateProductCategoryResult> Handle(UpdateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await db.ProductCategories.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Product category not found.");

        if (request.ParentCategoryId == request.Id)
        {
            throw new ConflictException("A product category cannot be its own parent.");
        }

        var nameTaken = await db.ProductCategories.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
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

        category.Update(request.Name, request.ParentCategoryId, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateProductCategoryResult(category.Id, category.Name, category.ParentCategoryId, category.IsActive);
    }
}
