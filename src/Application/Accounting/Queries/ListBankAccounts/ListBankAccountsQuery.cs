using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.ListBankAccounts;

/// <summary>
/// Backs the Bank Accounts card-grid screen (docs/phase-17-status.md decision #3) -- every
/// AccountKind.Bank/Cash Account with a live running balance, All/Inactive tabs.
/// </summary>
public sealed record ListBankAccountsQuery(
    Guid OrganizationId,
    bool IsActive = true,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize)
    : IRequest<PagedResult<BankAccountDto>>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.BankAccountView;
}

public sealed record BankAccountDto(
    Guid Id, string Code, string Name, string Kind, Guid? BankId, string? BankName,
    string? AccountNumber, bool IsActive, decimal Balance);
