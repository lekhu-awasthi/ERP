using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Commands.UpdatePayment;

public sealed record UpdatePaymentCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, Guid? PaymentModeId, Guid AccountId, decimal Amount,
    string? Reference, IReadOnlyList<PaymentAllocationInput> Allocations, ChequeDetailsInput? ChequeDetails = null)
    : IRequest<UpdatePaymentResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequestWithId, ICurrencyBearingCommand, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.PaymentEdit;

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
    public Guid AuditDocumentId => Id;
}

public sealed record UpdatePaymentResult(Guid Id, string Code, PaymentStatus Status);
