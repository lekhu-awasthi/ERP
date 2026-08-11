using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;

public sealed class CreateReportingTagCategoryCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateReportingTagCategoryCommand, CreateReportingTagCategoryResult>
{
    public async Task<CreateReportingTagCategoryResult> Handle(
        CreateReportingTagCategoryCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.ReportingTagCategories.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Name == request.Name, cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A reporting tag category named '{request.Name}' already exists.");
        }

        var category = ReportingTagCategory.Create(request.OrganizationId, request.Name);
        db.ReportingTagCategories.Add(category);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateReportingTagCategoryResult(category.Id, category.Name);
    }
}
