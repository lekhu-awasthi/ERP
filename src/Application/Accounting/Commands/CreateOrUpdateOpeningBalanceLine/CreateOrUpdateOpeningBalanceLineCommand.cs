using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateOrUpdateOpeningBalanceLine;

/// <summary>
/// Phase 17 (Configurations §18, docs/phase-17-status.md) -- sets (or corrects) one Account's
/// opening balance. No Draft/Approve lifecycle (the confirmed live screen is a single inline "Save
/// Changes" form) -- saving posts a balanced GlJournalEntry immediately against an
/// auto-provisioned "Opening Balance Equity" contra account. No ILockDateSensitive: an opening
/// balance carries no per-transaction Date field a lock date would guard (GlJournalEntry itself has
/// none either -- TrialBalanceQueryHandler cuts off by PostedAt, not a document date).
/// </summary>
public sealed record CreateOrUpdateOpeningBalanceLineCommand(Guid OrganizationId, Guid AccountId, decimal Debit, decimal Credit)
    : IRequest<OpeningBalanceLineResult>, IRequirePermission, IOrganizationScoped, ICurrencyBearingCommand, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.OpeningBalanceEdit;

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
}

public sealed record OpeningBalanceLineResult(Guid Id, Guid AccountId, decimal Debit, decimal Credit);
