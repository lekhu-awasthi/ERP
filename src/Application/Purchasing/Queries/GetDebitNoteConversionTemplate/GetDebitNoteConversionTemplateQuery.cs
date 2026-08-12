using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;

/// <summary>Same architecture-spec.md §3.3 pattern as GetCreditNoteConversionTemplateQuery, source
/// document is an Approved PurchaseBill instead of an Invoice.</summary>
public sealed record GetDebitNoteConversionTemplateQuery(Guid OrganizationId, Guid PurchaseBillId)
    : IRequest<DebitNoteConversionTemplateDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.PurchaseBillView;
}

public sealed record DebitNoteConversionTemplateDto(
    Guid ContactId, DateOnly Date, string? Reference, DocumentType ReferrerType, Guid ReferrerId,
    IReadOnlyList<DebitNoteLineInput> Lines);
