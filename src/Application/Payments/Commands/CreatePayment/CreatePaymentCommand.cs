using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.CreatePayment;

/// <summary>
/// Direction is client-supplied (Customer Payment screens send Received, Supplier Payment screens
/// send Paid) rather than hardcoded -- safe because Payments.Payment.Create is a single permission
/// shared across both directions (see phase-6-status.md's scope decision on why the permission
/// matrix wasn't split Sales.Payment/Purchasing.Payment: the aggregate, lifecycle, and
/// maker-checker story are identical, only the GL direction and the Contact/allocation-target type
/// differ), so a client can't escalate privilege by choosing one direction over the other.
/// </summary>
public sealed record CreatePaymentCommand(
    Guid OrganizationId, Guid ContactId, PaymentDirection Direction, DateOnly Date, Guid? PaymentModeId, Guid AccountId,
    decimal Amount, string? Reference, IReadOnlyList<PaymentAllocationInput> Allocations,
    ChequeDetailsInput? ChequeDetails = null)
    : IRequest<CreatePaymentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.PaymentCreate;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }

    /// <summary>Phase 32 (FR-2.3/FR-3.3). The billing location this document is raised from. Null
    /// means "the tenant's default", which <see cref="LocationResolver"/> resolves to HeadOffice --
    /// or to a real null when this document type is out of the tenant's LocationScopeMode. See
    /// <see cref="ILocationBearingCommand"/>.</summary>
    public Guid? LocationId { get; init; }
    public DocumentType AuditDocumentType => DocumentType.Payment;
}

public sealed record CreatePaymentResult(Guid Id, string Code, PaymentStatus Status);
