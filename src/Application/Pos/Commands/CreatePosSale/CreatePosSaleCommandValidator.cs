using ErpApp.Domain.Sales;
using FluentValidation;
using ErpApp.Application.Inventory.Stock;

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

        // Phase 74 -- a bill's discount is a percentage or an amount (the vendor's toggle), and an amount is at most
        // what the lines come to; the Domain's spread is the backstop.
        RuleFor(x => x.DiscountAmount).PosCashAmount();
        RuleFor(x => x.DiscountAmount).Equal(0m).When(x => x.DiscountPct != 0m)
            .WithMessage("A bill's discount is a percentage or an amount, not both.");
        RuleFor(x => x.DiscountAmount)
            .Must((command, amount) => amount <= ComesTo(command.Lines))
            .When(x => x.Lines is not null && x.DiscountAmount > 0m)
            .WithMessage("A bill's discount cannot be more than the bill.");
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
            line.RuleFor(x => x.DiscountPerUnit).PosCashAmount();
            line.RuleFor(x => x.DiscountPerUnit).Equal(0m).When(x => x.DiscountPct != 0m)
                .WithMessage("A line's discount is a percentage or an amount, not both.");
            line.RuleFor(x => x.DiscountPerUnit).LessThanOrEqualTo(x => x.Rate)
                .WithMessage("A discount per unit cannot be more than the line's rate.");
            line.BatchNoRule(x => x.BatchNo);
            line.SerialNumberRules(x => x.SerialNumbers);
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

    /// <summary>What the lines come to before VAT, as the Domain spreads a bill's amount over them; a line its own
    /// rules refuse counts for nothing here.</summary>
    private static decimal ComesTo(IReadOnlyList<PosSaleLineInput> lines) =>
        lines
            .Where(l => l is not null && l.Quantity > 0m && l.DiscountPerUnit >= 0m && l.DiscountPerUnit <= l.Rate
                && l.DiscountPct is >= 0m and <= 100m)
            .Sum(l => PosLineArithmetic.NetOf(l.Quantity, l.Rate, l.DiscountPerUnit, l.DiscountPct, 0m));
}
