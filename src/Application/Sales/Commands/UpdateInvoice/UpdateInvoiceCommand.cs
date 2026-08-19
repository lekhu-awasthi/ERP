using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateInvoice;

public sealed record UpdateInvoiceCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, Guid WarehouseId, DateOnly Date, string? Reference,
    IReadOnlyList<InvoiceLineInput> Lines, decimal DiscountPct = 0)
    : IRequest<UpdateInvoiceResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.InvoiceEdit;
}

public sealed record UpdateInvoiceResult(Guid Id, string Code, InvoiceStatus Status);
