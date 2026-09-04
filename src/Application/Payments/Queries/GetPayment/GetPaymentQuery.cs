using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;

namespace ErpApp.Application.Payments.Queries.GetPayment;

public sealed record GetPaymentQuery(Guid OrganizationId, Guid Id)
    : IRequest<PaymentDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PaymentView;
}

public sealed record PaymentAllocationDto(Guid Id, DocumentType TargetDocumentType, Guid TargetDocumentId, decimal Amount);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record PaymentDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    PaymentDirection Direction,
    string Code,
    DateOnly Date,
    Guid? PaymentModeId,
    Guid AccountId,
    decimal Amount,
    string? Reference,
    PaymentStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PaymentAllocationDto> Allocations,
    IReadOnlyList<PostedGlLineDto>? GlLines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate);
