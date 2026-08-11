using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.UpdateUnitOfMeasurement;

public sealed class UpdateUnitOfMeasurementCommandValidator : AbstractValidator<UpdateUnitOfMeasurementCommand>
{
    public UpdateUnitOfMeasurementCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShortName).NotEmpty().MaximumLength(20);
    }
}
