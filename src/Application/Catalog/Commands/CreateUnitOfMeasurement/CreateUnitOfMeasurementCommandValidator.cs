using FluentValidation;

namespace ErpApp.Application.Catalog.Commands.CreateUnitOfMeasurement;

public sealed class CreateUnitOfMeasurementCommandValidator : AbstractValidator<CreateUnitOfMeasurementCommand>
{
    public CreateUnitOfMeasurementCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ShortName).NotEmpty().MaximumLength(20);
    }
}
