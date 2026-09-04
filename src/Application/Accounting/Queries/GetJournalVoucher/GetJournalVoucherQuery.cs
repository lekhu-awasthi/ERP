using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.GetJournalVoucher;

public sealed record GetJournalVoucherQuery(Guid OrganizationId, Guid Id)
    : IRequest<JournalVoucherDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.JournalVoucherView;
}

public sealed record JournalVoucherLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit, Guid? ContactId);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

/// <summary>Projects JournalVoucher + (if Approved) its posted GlJournalEntry's lines in one
/// shape -- the "GL Transactions" section the roadmap's exit criteria names.</summary>
public sealed record JournalVoucherDetailDto(
    Guid Id,
    Guid OrganizationId,
    string Code,
    DateOnly Date,
    string? Reference,
    JournalVoucherStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyList<JournalVoucherLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines,
    // Phase 28 (FR-2.5) -- the document's own currency and its rate to the base currency.
    // Every amount above is denominated in CurrencyCode; the general ledger figures under
    // GlLines are in the base currency, already converted at ExchangeRate.
    string CurrencyCode,
    decimal ExchangeRate);
