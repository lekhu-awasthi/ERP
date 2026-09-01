using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateMigratedPurchaseRegisterEntry;

/// <summary>
/// Creates one historical Purchase Book row at cutover (FR-2.10). The Purchase-side counterpart of
/// <c>CreateMigratedSalesRegisterEntryCommand</c> -- read that command's doc comment for Decision D
/// (why a job-only command still travels the MediatR pipeline) and for why it is deliberately not
/// lock-date sensitive. Both apply here unchanged.
///
/// <para>See <see cref="ErpApp.Domain.Purchasing.MigratedPurchaseRegisterEntry"/> for the invariant:
/// no GL posting, no stock movement, no payment, no document number, no lifecycle.</para>
/// </summary>
public sealed record CreateMigratedPurchaseRegisterEntryCommand(
    Guid OrganizationId,
    DateOnly Date,
    string DocumentCode,
    string? ImportDeclarationNo,
    string PartyName,
    string? PartyPan,
    decimal TaxExemptValue,
    decimal TaxableNonCapitalLocalValue,
    decimal TaxableNonCapitalLocalVat,
    decimal TaxableNonCapitalImportValue,
    decimal TaxableNonCapitalImportVat,
    decimal TaxableCapitalValue,
    decimal TaxableCapitalVat)
    : IRequest<CreateMigratedPurchaseRegisterEntryResult>, IRequirePermission, IOrganizationScoped, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.MigratedRegisterManage;

    public DocumentType AuditDocumentType => DocumentType.MigratedPurchaseEntry;
}

public sealed record CreateMigratedPurchaseRegisterEntryResult(Guid Id, string DocumentCode);
