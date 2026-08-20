using FluentValidation;

namespace ErpApp.Application.Payments.Commands.ApplyPaymentAllocation;

public sealed class ApplyPaymentAllocationCommandValidator : AbstractValidator<ApplyPaymentAllocationCommand>
{
    public ApplyPaymentAllocationCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SourceId).NotEmpty();
        RuleFor(x => x.TargetDocumentId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
