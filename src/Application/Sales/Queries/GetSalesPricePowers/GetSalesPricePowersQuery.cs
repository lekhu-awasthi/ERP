using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Sales.Queries.GetSalesPricePowers;

/// <summary>
/// Phase 81 -- what a Quotation, Sales Order or Invoice form may offer before it is saved: whether the
/// caller may discount, and whether they may change a rate, at the location the document will be
/// raised from. The form disables what <see cref="SalesPriceGate"/> would refuse, as the till does
/// with its own flags (phase 76); the gate still decides on Save.
///
/// <para><paramref name="LocationId"/> is the form's choice; null is the tenant's default, resolved the
/// way the Create handler resolves it (<see cref="LocationResolver"/>), so the answer is about the
/// location the document will actually carry. Gated on the type's own View key, so only someone who can
/// open the form asks.</para>
///
/// <para><b>Location-agnostic</b> (<see cref="ILocationAgnosticRequest"/>), like the GL previews: a helper
/// for an unsaved form, with no row and nothing per-location to leak. The location it is about is the
/// price keys' location, and the handler asks that itself. Scoping the View key to it would refuse a
/// HeadOffice clerk the answer "no" about a branch they cannot write to anyway.</para>
/// </summary>
public sealed record GetSalesPricePowersQuery(Guid OrganizationId, DocumentType DocumentType, Guid? LocationId)
    : IRequest<SalesPricePowersDto>, IRequirePermission, IOrganizationScoped, ILocationAgnosticRequest
{
    public string PermissionKey => DocumentType switch
    {
        DocumentType.Quotation => PermissionKeys.QuotationView,
        DocumentType.SalesOrder => PermissionKeys.SalesOrderView,
        _ => PermissionKeys.InvoiceView,
    };
}

/// <param name="AtLocation">Phase 83 -- whether the answer is about a location (the one the document will carry) or,
/// when the type carries none under the tenant's scope, the whole organization: what the form's note says.</param>
public sealed record SalesPricePowersDto(bool CanDiscount, bool CanChangeRate, bool AtLocation);

public sealed class GetSalesPricePowersQueryValidator : AbstractValidator<GetSalesPricePowersQuery>
{
    public GetSalesPricePowersQueryValidator()
    {
        RuleFor(x => x.DocumentType)
            .Must(x => x is DocumentType.Quotation or DocumentType.SalesOrder or DocumentType.Invoice)
            .WithMessage("Price powers are read for a Quotation, Sales Order or Invoice.");
    }
}

public sealed class GetSalesPricePowersQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GetSalesPricePowersQuery, SalesPricePowersDto>
{
    public async Task<SalesPricePowersDto> Handle(GetSalesPricePowersQuery request, CancellationToken cancellationToken)
    {
        var locationId = await LocationResolver.ResolveAsync(
            db, request.OrganizationId, request.DocumentType, request.LocationId, cancellationToken);

        return new SalesPricePowersDto(
            await SalesPriceGate.IsGrantedAsync(
                db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceDiscount, locationId,
                cancellationToken),
            await SalesPriceGate.IsGrantedAsync(
                db, request.OrganizationId, currentUser.UserId, PermissionKeys.InvoiceChangeRate, locationId,
                cancellationToken),
            locationId is not null);
    }
}
