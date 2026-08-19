using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Application.Sales.Commands.CreateQuotation;

public sealed record CreateQuotationCommand(
    Guid OrganizationId, Guid ContactId, DateOnly Date, DateOnly? ExpiryDate, string? Reference,
    IReadOnlyList<QuotationLineInput> Lines)
    : IRequest<CreateQuotationResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.QuotationCreate;
}

public sealed record CreateQuotationResult(Guid Id, string Code, QuotationStatus Status);
