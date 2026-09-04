using ErpApp.Application.Common.Currencies;
using FluentValidation;

namespace ErpApp.Application.Accounting.Commands.CreateJournalVoucher;

public sealed class CreateJournalVoucherCommandValidator : AbstractValidator<CreateJournalVoucherCommand>
{
    public CreateJournalVoucherCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Reference).MaximumLength(200);
        RuleFor(x => x.Lines).NotNull();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.AccountId).NotEmpty();
            line.RuleFor(x => x.Debit).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.Credit).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x)
                .Must(l => (l.Debit > 0) ^ (l.Credit > 0))
                .WithMessage("Each line must have exactly one of Debit or Credit greater than zero.");
        });

        this.AddCurrencyRules(x => x.CurrencyCode, x => x.ExchangeRate);

    }
}
