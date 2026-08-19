using ErpApp.Application.Common.Security;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.UpdateExpense;

public sealed record UpdateExpenseCommand(
    Guid OrganizationId,
    Guid Id,
    Guid ContactId,
    DateOnly Date,
    DateOnly? DueDate,
    string? SupplierInvoiceReference,
    string? Notes,
    bool TdsApplicable,
    Guid? TdsTypeId,
    IReadOnlyList<ExpenseLineInput> Lines)
    : IRequest<UpdateExpenseResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive
{
    public string PermissionKey => PermissionKeys.ExpenseEdit;
}

public sealed record UpdateExpenseResult(Guid Id, string Code, ExpenseStatus Status);
