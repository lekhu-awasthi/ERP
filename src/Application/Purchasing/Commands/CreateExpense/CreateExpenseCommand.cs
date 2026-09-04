using ErpApp.Application.Common.Currencies;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Commands.CreateExpense;

public sealed record CreateExpenseCommand(
    Guid OrganizationId,
    Guid ContactId,
    DateOnly Date,
    DateOnly? DueDate,
    string? SupplierInvoiceReference,
    string? Notes,
    bool TdsApplicable,
    Guid? TdsTypeId,
    IReadOnlyList<ExpenseLineInput> Lines)
    : IRequest<CreateExpenseResult>, IRequirePermission, IOrganizationScoped, ILockDateSensitive, IAuditableRequest, ICurrencyBearingCommand
{
    public string PermissionKey => PermissionKeys.ExpenseCreate;

    /// <summary>Phase 28 (FR-2.5). Null means the base currency at rate 1 -- see
    /// <see cref="ICurrencyBearingCommand"/>.</summary>
    public string? CurrencyCode { get; init; }

    /// <inheritdoc cref="CurrencyCode"/>
    public decimal? ExchangeRate { get; init; }
    public DocumentType AuditDocumentType => DocumentType.Expense;
}

public sealed record CreateExpenseResult(Guid Id, string Code, ExpenseStatus Status);
