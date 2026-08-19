using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.UpdateQuotation;

public sealed record UpdateQuotationCommand(
    Guid OrganizationId, Guid Id, Guid ContactId, DateOnly Date, DateOnly? ExpiryDate, string? Reference,
    IReadOnlyList<QuotationLineInput> Lines)
    : IRequest<UpdateQuotationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.QuotationEdit;
}

public sealed record UpdateQuotationResult(Guid Id, string Code, QuotationStatus Status);
