using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateDebitNote;

public sealed record CreateDebitNoteCommand(
    Guid OrganizationId,
    Guid ContactId,
    DateOnly Date,
    string? Reference,
    Guid? TdsTypeId,
    IReadOnlyList<DebitNoteLineInput> Lines,
    DocumentType? ReferrerType = null,
    Guid? ReferrerId = null,
    decimal DiscountPct = 0)
    : IRequest<CreateDebitNoteResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.DebitNoteCreate;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.DebitNote;
}

public sealed record CreateDebitNoteResult(Guid Id, string Code, DebitNoteStatus Status);
