using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateReportingTagCategory;

public sealed class UpdateReportingTagCategoryCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateReportingTagCategoryCommand, UpdateReportingTagCategoryResult>
{
    public async Task<UpdateReportingTagCategoryResult> Handle(
        UpdateReportingTagCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await db.ReportingTagCategories.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Reporting tag category not found.");

        var nameTaken = await db.ReportingTagCategories.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Id != request.Id && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A reporting tag category named '{request.Name}' already exists.");
        }

        category.Update(request.Name, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateReportingTagCategoryResult(category.Id, category.Name, category.IsActive);
    }
}
