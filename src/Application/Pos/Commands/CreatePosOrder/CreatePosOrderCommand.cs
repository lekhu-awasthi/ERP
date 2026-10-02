using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Common;
using ErpApp.Domain.Pos;
using ErpApp.Domain.Tenancy;
using MediatR;

namespace ErpApp.Application.Pos.Commands.CreatePosOrder;

/// <summary>
/// Phase 64 -- seats a table (or opens a Take Away or Delivery order) and sends its first items to the
/// kitchen, in one transaction: the vendor's <i>Save Orders</i>, which saves and tickets at once. An
/// order never holds unsent lines on the server; what a waiter has not sent yet lives in the browser.
///
/// <para>Audited (Create): opening a tab is the start of the record a void is later measured against.
/// It carries no rate and no date -- the catalogue prices the lines and the order stamps the Nepal date.</para>
/// </summary>
public sealed record CreatePosOrderCommand(
    Guid OrganizationId,
    Guid LocationId,
    PosTab OrderType,
    Guid? TableId,
    int Covers,
    Guid? ContactId,
    IReadOnlyList<PosOrderItemInput> Items)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature, IAuditableRequest
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];

    public DocumentType AuditDocumentType => DocumentType.PosOrder;
}
