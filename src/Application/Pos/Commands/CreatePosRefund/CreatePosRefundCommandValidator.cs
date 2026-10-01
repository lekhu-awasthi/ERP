using ErpApp.Domain.Sales;
using FluentValidation;

namespace ErpApp.Application.Pos.Commands.CreatePosRefund;

public sealed class CreatePosRefundCommandValidator : AbstractValidator<CreatePosRefundCommand>
{
    public const int MaxLines = 200;
    public const int MaxPayouts = 10;

    public CreatePosRefundCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();

        // The same rule the aggregate holds, here so a missing reason is a 400 naming the field and not
        // a Domain invariant surfacing as a 500 (phase 39).
        RuleFor(x => x.Reason)
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("Say why the goods came back.")
            .MaximumLength(CreditNote.MaxReasonLength);

        RuleFor(x => x.Lines).NotEmpty().WithMessage("Choose at least one line to refund.");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(MaxLines).When(x => x.Lines is not null)
            .WithName(nameof(CreatePosRefundCommand.Lines));
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.InvoiceLineId).Distinct().Count() == lines.Count)
            .When(x => x.Lines is not null)
            .WithMessage("Each line of the sale is named once; put the whole returned quantity on it.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.InvoiceLineId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
        });

        RuleFor(x => x.Payouts).NotNull();
        RuleFor(x => x.Payouts.Count).LessThanOrEqualTo(MaxPayouts).When(x => x.Payouts is not null)
            .WithName(nameof(CreatePosRefundCommand.Payouts));
        RuleForEach(x => x.Payouts).ChildRules(payout =>
        {
            payout.RuleFor(x => x.PaymentModeId).NotEmpty();
            payout.RuleFor(x => x.Amount).PosPositiveCashAmount();
        });
    }
}
