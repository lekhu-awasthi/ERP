using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Purchasing.Queries.PurchaseRegister;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.MigratedPurchaseRegister;

/// <summary>
/// The Migrated Purchase Register (FR-9.4's "migrated" variant, sourced from FR-2.10's import).
/// Reads <c>MigratedPurchaseRegisterEntry</c> and nothing else -- no PurchaseBill, no DebitNote, no
/// GL. Returns the live register's own <c>PurchaseRegisterDto</c> for the same reason its Sales-side
/// twin does; read <c>MigratedSalesRegisterQuery</c>'s doc comment for the full argument, including
/// why the screen is deliberately not shared even though the DTO is.
/// </summary>
public sealed record MigratedPurchaseRegisterQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    string? PartySearch,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<PurchaseRegisterDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.MigratedPurchaseRegisterView;
}

public sealed class MigratedPurchaseRegisterQueryValidator : AbstractValidator<MigratedPurchaseRegisterQuery>
{
    public MigratedPurchaseRegisterQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ToDate).GreaterThanOrEqualTo(x => x.FromDate);
        PagingValidation.ValidatePaging(this, x => x.Page, x => x.PageSize);
    }
}
