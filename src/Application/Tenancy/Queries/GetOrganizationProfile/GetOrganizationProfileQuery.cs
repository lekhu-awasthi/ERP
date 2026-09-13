using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Queries.GetOrganizationProfile;

/// <summary>
/// Phase 39 — the organization's own details and whether it has a logo.
///
/// <para>The field list is <c>Organization &gt; Overview</c>'s, read live on 2026-09-13: Name,
/// Display Name, Email, Pan No, Phone No, Registered Address, Website, Accounting Starting Date,
/// Vat Registered. This codebase has no Display Name and does not invent one.</para>
///
/// <para><b>Read-only, and that is a stated scope line rather than an oversight.</b> The reference
/// product's EDIT DETAILS dialog can change all nine; phase 39 ships the logo because that is the
/// gap the roadmap named, and leaves editing the rest as a carried item. Showing the fields the PDF
/// header prints, next to the logo it prints beside them, is what makes the screen make sense at
/// all.</para>
///
/// <para><b>No logo URL.</b> <see cref="HasLogo"/> is a flag; the bytes come from a separate
/// authenticated endpoint. <c>IFileStorage</c>'s own remarks set that rule, and honouring it here is
/// what keeps a tenant's logo from being readable by anyone who guesses a storage key.</para>
/// </summary>
public sealed record GetOrganizationProfileQuery(Guid OrganizationId)
    : IRequest<OrganizationProfileDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationProfileView;
}

public sealed record OrganizationProfileDto(
    Guid Id,
    string Name,
    string Industry,
    string? Address,
    string? Email,
    string? Phone,
    string? PanNumber,
    string? Website,
    DateOnly AccountingStartDate,
    bool IsVatRegistered,
    string WorkspaceName,
    bool HasLogo);

public sealed class GetOrganizationProfileQueryHandler(IAppDbContext db)
    : IRequestHandler<GetOrganizationProfileQuery, OrganizationProfileDto>
{
    public async Task<OrganizationProfileDto> Handle(
        GetOrganizationProfileQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations
            .Where(x => x.Id == request.OrganizationId)
            .Select(x => new OrganizationProfileDto(
                x.Id,
                x.Name,
                x.Industry,
                x.Address,
                x.Email,
                x.Phone,
                x.PanNumber,
                x.Website,
                x.AccountingStartDate,
                x.IsVatRegistered,
                x.WorkspaceName,
                x.LogoStorageKey != null))
            .FirstOrDefaultAsync(cancellationToken);

        return organization ?? throw new NotFoundException("Organization not found.");
    }
}
