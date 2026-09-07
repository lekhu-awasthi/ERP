using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Accounting.Commands.CreateJournalVoucher;

public sealed record CreateJournalVoucherCommand(
    Guid OrganizationId, DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines)
    : IRequest<CreateJournalVoucherResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand, ILocationBearingCommand
{
    public string PermissionKey => PermissionKeys.JournalVoucherCreate;

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
    public DocumentType AuditDocumentType => DocumentType.JournalVoucher;
}

public sealed record CreateJournalVoucherResult(Guid Id, string Code, JournalVoucherStatus Status);
