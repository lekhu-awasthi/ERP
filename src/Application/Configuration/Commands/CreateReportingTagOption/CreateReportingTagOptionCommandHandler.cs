using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.CreateReportingTagOption;

public sealed class CreateReportingTagOptionCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateReportingTagOptionCommand, CreateReportingTagOptionResult>
{
    public async Task<CreateReportingTagOptionResult> Handle(
        CreateReportingTagOptionCommand request, CancellationToken cancellationToken)
    {
        var categoryExists = await db.ReportingTagCategories.AnyAsync(
            x => x.Id == request.CategoryId && x.OrganizationId == request.OrganizationId, cancellationToken);

        if (!categoryExists)
        {
            throw new NotFoundException("Reporting tag category not found.");
        }

        var nameExists = await db.ReportingTagOptions.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.CategoryId == request.CategoryId && x.Name == request.Name,
            cancellationToken);

        if (nameExists)
        {
            throw new ConflictException($"A reporting tag option named '{request.Name}' already exists in this category.");
        }

        var option = ReportingTagOption.Create(request.OrganizationId, request.Name, request.CategoryId);
        db.ReportingTagOptions.Add(option);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateReportingTagOptionResult(option.Id, option.Name, option.CategoryId);
    }
}
