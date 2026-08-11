using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.UpdateReportingTagOption;

public sealed class UpdateReportingTagOptionCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateReportingTagOptionCommand, UpdateReportingTagOptionResult>
{
    public async Task<UpdateReportingTagOptionResult> Handle(
        UpdateReportingTagOptionCommand request, CancellationToken cancellationToken)
    {
        var option = await db.ReportingTagOptions.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Reporting tag option not found.");

        var categoryExists = await db.ReportingTagCategories.AnyAsync(
            x => x.Id == request.CategoryId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!categoryExists)
        {
            throw new NotFoundException("Reporting tag category not found.");
        }

        var nameTaken = await db.ReportingTagOptions.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Id != request.Id
                 && x.CategoryId == request.CategoryId
                 && x.Name == request.Name,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException($"A reporting tag option named '{request.Name}' already exists in this category.");
        }

        option.Update(request.Name, request.CategoryId, request.IsActive);
        await db.SaveChangesAsync(cancellationToken);

        return new UpdateReportingTagOptionResult(option.Id, option.Name, option.CategoryId, option.IsActive);
    }
}
