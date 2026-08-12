using FluentValidation;

namespace ErpApp.Application.Payments.Commands.UpdatePayment;

public sealed class UpdatePaymentCommandValidator : AbstractValidator<UpdatePaymentCommand>
{
    public UpdatePaymentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ContactId).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Allocations).NotNull();
        RuleForEach(x => x.Allocations).ChildRules(allocation =>
        {
            allocation.RuleFor(x => x.TargetDocumentId).NotEmpty();
            allocation.RuleFor(x => x.Amount).GreaterThan(0);
        });
    }
}
