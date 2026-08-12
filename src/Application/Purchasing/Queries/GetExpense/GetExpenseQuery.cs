using ErpApp.Application.Common.Security;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.GetExpense;

public sealed record GetExpenseQuery(Guid OrganizationId, Guid Id)
    : IRequest<ExpenseDetailDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ExpenseView;
}

public sealed record ExpenseLineDto(Guid Id, Guid AccountId, decimal Amount, VatRate VatRate, decimal VatAmount);

public sealed record PostedGlLineDto(Guid Id, Guid AccountId, decimal Debit, decimal Credit);

public sealed record ExpenseDetailDto(
    Guid Id,
    Guid OrganizationId,
    Guid ContactId,
    string Code,
    DateOnly Date,
    DateOnly? DueDate,
    string? SupplierInvoiceReference,
    string? Notes,
    bool TdsApplicable,
    Guid? TdsTypeId,
    decimal TdsAmount,
    ExpenseStatus Status,
    Guid? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    decimal GrandTotal,
    IReadOnlyList<ExpenseLineDto> Lines,
    IReadOnlyList<PostedGlLineDto>? GlLines);
