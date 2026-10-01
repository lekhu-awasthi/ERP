using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosRefund;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Queries.PreviewPosRefund;

public sealed record PosRefundPreviewLineDto(
    Guid InvoiceLineId, decimal Quantity, decimal Amount, decimal ServiceChargeAmount, decimal VatAmount, decimal LineTotal);

/// <summary>
/// Phase 63 -- what a refund of the chosen lines would be, computed by the same planner the refund
/// itself runs (<c>PosRefundPlanner</c>), so the figure the cashier hands back is the figure posted.
/// </summary>
/// <param name="OwedBefore">What the customer still owes on the sale; the refund comes off this first.</param>
/// <param name="RequiredPayout">What must be handed back; the payouts must come to exactly this.</param>
/// <param name="ToAccount">What comes off what the customer owes instead.</param>
public sealed record PosRefundPreviewDto(
    IReadOnlyList<PosRefundPreviewLineDto> Lines,
    decimal SubTotal,
    decimal ServiceCharge,
    decimal Vat,
    decimal RoundOff,
    decimal GrandTotal,
    decimal OwedBefore,
    decimal RequiredPayout,
    decimal ToAccount);

/// <summary>
/// Phase 63 -- the refund screen's figure. A query sent by POST because it carries the chosen lines.
/// Asked under the refund's own key, in the caller's own open session, so the preview refuses whatever
/// the refund would (the session, the location, the quantity cap) except the payout, which it computes.
///
/// <para><see cref="ILocationFilteredQuery"/>, because the location is the session's, which only the
/// handler can read: the pipeline admits a caller holding the key at any location, and the handler
/// re-checks it at the till's.</para>
/// </summary>
public sealed record PreviewPosRefundQuery(
    Guid OrganizationId,
    Guid SessionId,
    Guid InvoiceId,
    IReadOnlyList<PosRefundLineInput> Lines)
    : IRequest<PosRefundPreviewDto>, IRequirePermission, IOrganizationScoped, IRequireAnyFeature, ILocationFilteredQuery
{
    public string PermissionKey => PermissionKeys.CreditNoteCreate;

    public IReadOnlyCollection<TenantFeature> AnyOfFeatures => PosFeatures.Any;
}

public sealed class PreviewPosRefundQueryValidator : AbstractValidator<PreviewPosRefundQuery>
{
    public PreviewPosRefundQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.InvoiceId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Choose at least one line to refund.");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(CreatePosRefundCommandValidator.MaxLines).When(x => x.Lines is not null)
            .WithName(nameof(PreviewPosRefundQuery.Lines));
        RuleFor(x => x.Lines)
            .Must(lines => lines.Select(l => l.InvoiceLineId).Distinct().Count() == lines.Count)
            .When(x => x.Lines is not null)
            .WithMessage("Each line of the sale is named once; put the whole returned quantity on it.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.InvoiceLineId).NotEmpty();
            line.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}
