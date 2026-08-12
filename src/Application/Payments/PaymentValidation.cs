using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments;

/// <summary>Shared existence checks for Create/UpdatePayment -- mirrors
/// Accounting.AccountingValidation/Sales.SalesValidation's precedent.</summary>
internal static class PaymentValidation
{
    public static async Task EnsureContactExistsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, CancellationToken cancellationToken)
    {
        var exists = await db.Contacts.AnyAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId && x.Type == ContactType.Customer, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Customer not found.");
        }
    }

    public static async Task EnsurePaymentModeExistsAsync(
        IAppDbContext db, Guid organizationId, Guid? paymentModeId, CancellationToken cancellationToken)
    {
        if (paymentModeId is not { } id)
        {
            return;
        }

        var exists = await db.PaymentModes.AnyAsync(x => x.Id == id && x.OrganizationId == organizationId, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException("Payment mode not found.");
        }
    }

    /// <summary>Only Invoice targets exist this phase -- validates the referenced Invoice belongs
    /// to the same Organization and is Approved (an allocation against a Draft/nonexistent
    /// document makes no sense).</summary>
    public static async Task EnsureAllocationTargetsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<PaymentAllocationInput> allocations, CancellationToken cancellationToken)
    {
        var invoiceIds = allocations
            .Where(x => x.TargetDocumentType == DocumentType.Invoice)
            .Select(x => x.TargetDocumentId)
            .Distinct()
            .ToList();

        if (invoiceIds.Count == 0)
        {
            return;
        }

        var existingCount = await db.Invoices.CountAsync(
            x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id)
                 && x.Status == Domain.Sales.InvoiceStatus.Approved,
            cancellationToken);

        if (existingCount != invoiceIds.Count)
        {
            throw new NotFoundException("One or more allocation target invoices were not found or are not Approved.");
        }
    }
}
