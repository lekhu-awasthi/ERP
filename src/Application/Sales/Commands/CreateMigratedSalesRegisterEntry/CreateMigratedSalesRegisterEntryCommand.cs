using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateMigratedSalesRegisterEntry;

/// <summary>
/// Creates one historical Sales Book row at cutover (FR-2.10). See
/// <see cref="ErpApp.Domain.Sales.MigratedSalesRegisterEntry"/> for the invariant this command is
/// the only writer of.
///
/// <para><b>Decision D -- why this command exists at all, when its only caller is a background
/// job.</b> Phase 21a's rule ("a job that writes reuses the real Create command") was justified by
/// the rules already living in <c>CreateProductCommandHandler</c>; there is no such pre-existing
/// handler here, so the choice was genuinely open and had to be re-argued rather than inherited. It
/// went the same way for different reasons. Routing through the pipeline buys three things a direct
/// write would have had to reimplement or forgo: <c>ValidationBehavior</c> runs the validator on
/// every row, so a malformed row is one clearly-attributed row error instead of a provider
/// exception; <c>AuthorizationBehavior</c> re-checks <c>MigratedRegisterManage</c> <b>per row at
/// execution time</b>, so a user stripped of it between upload and run has the job stopped rather
/// than honoured -- which matters more here than anywhere else in the tree, because these rows go
/// straight into a statutory return; and <c>AuditBehavior</c> attributes every seeded row to the
/// person who ran the cutover. The cost is honestly a command with one caller, which is a small
/// price for those three.</para>
///
/// <para><b>It deliberately implements neither <c>ILockDateSensitive</c> nor
/// <c>ILockDateSensitiveDocument</c></b>, so <c>LockDateBehavior</c> skips it entirely -- the "no
/// marker interface, no gate" pattern used as a decision, not an omission. Every migrated row is by
/// definition dated before the tenant's accounting start date and so before any plausible lock
/// date; gating it would make the feature unusable for the only thing it is for. What makes that
/// safe is the invariant: there are no books behind a lock date to retro-edit, because this row
/// posts nothing.</para>
/// </summary>
public sealed record CreateMigratedSalesRegisterEntryCommand(
    Guid OrganizationId,
    DateOnly Date,
    string DocumentCode,
    string PartyName,
    string? PartyPan,
    decimal TotalValue,
    decimal TaxExemptValue,
    decimal TaxableValue,
    decimal VatAmount,
    decimal ExportValue,
    string? ExportCountry,
    string? ExportDeclarationNo,
    DateOnly? ExportDeclarationDate)
    : IRequest<CreateMigratedSalesRegisterEntryResult>, IRequirePermission, IOrganizationScoped, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.MigratedRegisterManage;

    public DocumentType AuditDocumentType => DocumentType.MigratedSalesEntry;
}

public sealed record CreateMigratedSalesRegisterEntryResult(Guid Id, string DocumentCode);
