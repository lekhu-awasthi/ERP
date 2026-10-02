using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.CreatePosSale;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.CreatePosOrderInvoice;

/// <summary>
/// Phase 65 -- bills (part of) a restaurant order: an Invoice created <b>and approved</b>, paid at the
/// till, in the caller's open session at the order's location. It is phase 61's till sale with lines
/// taken from the order instead of the cart (<c>PosSaleCompletion</c> does everything after the lines),
/// so it is a <b>second door</b> onto an approved invoice and is named as one in every guard that
/// assumed one (phase 61's gotcha): metered, location-bearing, audited, lock-date sensitive.
///
/// <para><b>What goes on it</b> is decided by the order's bill planner from <see cref="Split"/>,
/// <see cref="Items"/> and <see cref="Parts"/>, priced at the order lines' frozen rates (never the
/// request's: the command carries no rate), the last of each line taking exactly what is left of its
/// money and the bill rounded on the order's running total (phase-65-status.md Decisions C-E).</para>
///
/// <para><b>Permissions</b> are the till sale's (phase 61 Decision F): <c>Sales.Invoice.Create</c> at
/// the location in the pipeline, and <c>Sales.Invoice.Approve</c> there too when part of the bill is
/// left on credit. Named <c>Create…</c> so <c>AuditBehavior</c> writes its row.</para>
/// </summary>
public sealed record CreatePosOrderInvoiceCommand(
    Guid OrganizationId,
    Guid SessionId,
    Guid? LocationId,
    Guid OrderId,
    PosOrderSplit Split,
    IReadOnlyList<PosOrderLineQuantityInput> Items,
    int? Parts,
    IReadOnlyList<PosTenderInput> Tenders,
    decimal ChangeAmount = 0,
    Guid? ContactId = null,
    bool OverrideStockWarning = false,
    bool OverrideCreditLimitWarning = false)
    : IRequest<CreatePosOrderInvoiceResult>, IRequirePermission, IOrganizationScoped, IRequireFeature,
        ILockDateSensitive, IAuditableRequest, IMeteredTransaction, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.InvoiceCreate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    /// <summary>The Nepal date the bill is dated and posted on: now, like any till sale.</summary>
    public DateOnly Date { get; init; } = NepalTime.LocalDate(DateTimeOffset.UtcNow);

    public DocumentType AuditDocumentType => DocumentType.Invoice;

    /// <summary>A bill is an approved Invoice, so it spends the transaction allowance like a till sale.</summary>
    public DocumentType MeteredDocumentType => DocumentType.Invoice;
}

/// <param name="OrderStatus">The order after this bill: <see cref="PosOrderStatus.Settled"/> when it was
/// the last, freeing the table.</param>
/// <param name="OrderToBill">What of the order is still to be billed, in money: the order's total as
/// bills, less every bill so far.</param>
public sealed record CreatePosOrderInvoiceResult(
    Guid Id,
    string Code,
    decimal GrandTotal,
    decimal ServiceCharge,
    decimal RoundOff,
    decimal Tendered,
    decimal ChangeAmount,
    decimal CreditAmount,
    bool IsAbbreviatedTaxInvoice,
    PosOrderStatus OrderStatus,
    decimal OrderToBill);

public sealed class CreatePosOrderInvoiceCommandValidator : AbstractValidator<CreatePosOrderInvoiceCommand>
{
    public CreatePosOrderInvoiceCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.Split).IsInEnum();
        this.ValidateLineQuantities(x => x.Items);
        RuleFor(x => x.Items.Count)
            .GreaterThan(0)
            .When(x => x.Split == PosOrderSplit.Items && x.Items != null)
            .OverridePropertyName(nameof(CreatePosOrderInvoiceCommand.Items))
            .WithMessage("Choose what goes on this bill.");
        RuleFor(x => x.Parts)
            .NotNull()
            .InclusiveBetween(1, PosOrder.MaxCovers)
            .When(x => x.Split == PosOrderSplit.Equal)
            .WithMessage($"An equal split is into 1 to {PosOrder.MaxCovers} parts.");

        RuleFor(x => x.ChangeAmount).PosCashAmount();
        RuleFor(x => x.Tenders).NotNull();
        RuleFor(x => x.Tenders.Count).LessThanOrEqualTo(CreatePosSaleCommandValidator.MaxTenders)
            .When(x => x.Tenders is not null)
            .WithName(nameof(CreatePosOrderInvoiceCommand.Tenders));
        RuleForEach(x => x.Tenders).ChildRules(tender =>
        {
            tender.RuleFor(x => x.PaymentModeId).NotEmpty();
            tender.RuleFor(x => x.Amount).PosPositiveCashAmount();
        });
    }
}
