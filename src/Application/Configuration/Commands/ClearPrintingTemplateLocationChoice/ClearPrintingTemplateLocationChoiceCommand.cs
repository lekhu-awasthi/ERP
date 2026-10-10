using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.ClearPrintingTemplateLocationChoice;

/// <summary>
/// Phase 75 -- the location goes back to printing this type with the organization's default. Its own DELETE rather
/// than a null template on the PUT, so a misspelt template field is a 400 there and never a silent "use the default"
/// (phase 72's required-field rule).
/// </summary>
public sealed record ClearPrintingTemplateLocationChoiceCommand(Guid OrganizationId, Guid LocationId, DocumentType? DocumentType)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PrintingTemplateManage;
}

public sealed class ClearPrintingTemplateLocationChoiceCommandValidator : AbstractValidator<ClearPrintingTemplateLocationChoiceCommand>
{
    public ClearPrintingTemplateLocationChoiceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.DocumentType).NotNull().IsInEnum();
    }
}

public sealed class ClearPrintingTemplateLocationChoiceCommandHandler(IAppDbContext db)
    : IRequestHandler<ClearPrintingTemplateLocationChoiceCommand, Unit>
{
    public async Task<Unit> Handle(ClearPrintingTemplateLocationChoiceCommand request, CancellationToken cancellationToken)
    {
        var locationExists = await db.BillingLocations.AnyAsync(
            x => x.Id == request.LocationId && x.OrganizationId == request.OrganizationId, cancellationToken);
        if (!locationExists)
        {
            throw new NotFoundException("Location not found.");
        }

        // Clearing a location that never chose is already the state asked for, so it succeeds.
        var choice = await db.PrintingTemplateLocationChoices.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.LocationId == request.LocationId
                 && x.DocumentType == request.DocumentType,
            cancellationToken);

        if (choice is not null)
        {
            db.PrintingTemplateLocationChoices.Remove(choice);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}
