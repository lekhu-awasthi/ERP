using ErpApp.Application.Common.Currencies;
using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.CreateCashTransfer;

public sealed class CreateCashTransferCommandValidator : AbstractValidator<CreateCashTransferCommand>
{
    public CreateCashTransferCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.FromAccountId).NotEmpty();
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ToAccountId).NotEmpty();
            line.RuleFor(x => x.Amount).GreaterThan(0);
        });

        this.AddCurrencyRules(x => x.CurrencyCode, x => x.ExchangeRate);

    }
}
