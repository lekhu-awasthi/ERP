using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;

namespace ErpApp.Application.Crm.Queries.ListSmsCreditLedger;

public sealed record ListSmsCreditLedgerQuery(Guid OrganizationId, int Page = 1, int PageSize = PagingDefaults.MaxPageSize)
    : IRequest<SmsCreditLedgerDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.SmsCreditLedgerView;
}

public sealed record SmsCreditLedgerRowDto(
    Guid Id, SmsCreditLedgerEntryType Type, int ChangeAmount, string? Reason, Guid CreatedByUserId, string CreatedByName, DateTimeOffset CreatedAt);

/// <summary>Balance is computed server-side over the *full* ledger, not a client-side sum over the
/// current page -- the Phase 16c pagination gotcha this codebase has already been bitten by once
/// (four report pages' footer totals). See docs/phase-18-status.md exit criteria #5.</summary>
public sealed record SmsCreditLedgerDto(int Balance, IReadOnlyList<SmsCreditLedgerRowDto> Rows, int Page, int PageSize, int TotalCount);
