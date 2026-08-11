using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.UpdateCashTransfer;

public sealed class UpdateCashTransferCommandValidator : AbstractValidator<UpdateCashTransferCommand>
{
    public UpdateCashTransferCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.FromAccountId).NotEmpty();
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ToAccountId).NotEmpty();
            line.RuleFor(x => x.Amount).GreaterThan(0);
        });
    }
}
