using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Sales.Queries.SalesRegister;
using ErpApp.Domain.Common;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Sales.Queries.MigratedSalesRegister;

/// <summary>
/// The Migrated Sales Register (FR-9.4's "migrated" variant, sourced from FR-2.10's import). Reads
/// <c>MigratedSalesRegisterEntry</c> and <b>nothing else</b> -- no Invoice, no CreditNote, no GL.
///
/// <para><b>It returns the live register's own <c>SalesRegisterDto</c> on purpose.</b> The two
/// column sets are identical by construction: the migrated variant exists precisely so that a
/// tenant's pre-cutover history appears in the same statutory shape as its post-cutover activity.
/// Sharing the DTO means the ClosedXML export path, the pagination contract and the Angular row
/// model are the same code rather than a parallel copy that could drift from the statutory form.
/// The one field that had to move is <c>SalesRegisterRowDto.ContactId</c>, now nullable -- see its
/// comment.</para>
///
/// <para><b>What is deliberately not shared is the screen</b> (Decision B). A mode toggle on the
/// live register page was considered and rejected: the failure it invites -- reading migrated,
/// unvetted, GL-less numbers as though they were this year's real books -- is exactly the failure
/// this data makes possible, and Angular's default route-reuse strategy makes a one-component,
/// two-mode page fragile besides (phase-3's bug #1). Two routes, two components, two nav entries,
/// two permission keys, and a banner on each migrated page.</para>
///
/// <para>Every row is stamped <see cref="DocumentType.MigratedSalesEntry"/>, which is what makes the
/// Type column on the exported spreadsheet truthful rather than borrowing the word "Invoice" for a
/// document that does not exist.</para>
/// </summary>
/// <param name="PartySearch">Case-insensitive contains match over the party name and PAN. The live
/// register filters by ContactId because its rows always have one; a migrated row's party is free
/// text, so a dropdown of Contacts would be filtering on a column that is usually null.</param>
public sealed record MigratedSalesRegisterQuery(
    Guid OrganizationId,
    DateOnly FromDate,
    DateOnly ToDate,
    string? PartySearch,
    int Page = 1,
    int PageSize = PagingDefaults.DefaultPageSize,
    bool ExportAll = false)
    : IRequest<SalesRegisterDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.MigratedSalesRegisterView;
}

public sealed class MigratedSalesRegisterQueryValidator : AbstractValidator<MigratedSalesRegisterQuery>
{
    public MigratedSalesRegisterQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ToDate).GreaterThanOrEqualTo(x => x.FromDate);
        PagingValidation.ValidatePaging(this, x => x.Page, x => x.PageSize);
    }
}
