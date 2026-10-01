using FluentValidation;

namespace ErpApp.Application.Pos.Commands.CreatePosSale;

public sealed class CreatePosSaleCommandValidator : AbstractValidator<CreatePosSaleCommand>
{
    public const int MaxLines = 200;
    public const int MaxTenders = 10;

    public CreatePosSaleCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
        RuleFor(x => x.OrderType).IsInEnum();
        RuleFor(x => x.ChangeAmount).PosCashAmount();

        RuleFor(x => x.Lines).NotEmpty().WithMessage("A sale needs at least one line.");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(MaxLines).When(x => x.Lines is not null)
            .WithName(nameof(CreatePosSaleCommand.Lines));
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
            line.RuleFor(x => x.Rate).GreaterThanOrEqualTo(0);
            line.RuleFor(x => x.VatRate).IsInEnum();
            line.RuleFor(x => x.DiscountPct).InclusiveBetween(0, 100);
            line.RuleFor(x => x.BatchNo).MaximumLength(100);
        });

        RuleFor(x => x.Tenders).NotNull();
        RuleFor(x => x.Tenders.Count).LessThanOrEqualTo(MaxTenders).When(x => x.Tenders is not null)
            .WithName(nameof(CreatePosSaleCommand.Tenders));
        RuleForEach(x => x.Tenders).ChildRules(tender =>
        {
            tender.RuleFor(x => x.PaymentModeId).NotEmpty();
            tender.RuleFor(x => x.Amount).PosPositiveCashAmount();
        });
    }
}
