using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments;

/// <summary>Shared existence checks for Create/UpdatePayment -- mirrors
/// Accounting.AccountingValidation/Sales.SalesValidation's precedent.</summary>
internal static class PaymentValidation
{
    /// <summary>Direction-aware: Received (Customer Payment) requires a Customer contact, Paid
    /// (Supplier Payment) requires a Supplier contact -- see PaymentDirection's doc comment.</summary>
    public static async Task EnsureContactExistsAsync(
        IAppDbContext db, Guid organizationId, Guid contactId, PaymentDirection direction, CancellationToken cancellationToken)
    {
        var expectedType = direction == PaymentDirection.Received ? ContactType.Customer : ContactType.Supplier;

        var exists = await db.Contacts.AnyAsync(
            x => x.Id == contactId && x.OrganizationId == organizationId && x.Type == expectedType, cancellationToken);

        if (!exists)
        {
            throw new NotFoundException($"{expectedType} not found.");
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

    /// <summary>Invoice (Customer Payment) and PurchaseBill (Supplier Payment) targets both
    /// validated here regardless of the Payment's own Direction -- a Contact can be both a
    /// customer and a supplier (confirmed live in erp-module-scan.md's Allocate Supplier Payments
    /// section, "Type column includes... even Invoice"), so this stays a straight existence+
    /// Approved check per target type rather than cross-checking against Direction. Validates each
    /// referenced document belongs to the same Organization and is Approved (an allocation against
    /// a Draft/nonexistent document makes no sense).</summary>
    public static async Task EnsureAllocationTargetsExistAsync(
        IAppDbContext db, Guid organizationId, IEnumerable<PaymentAllocationInput> allocations, CancellationToken cancellationToken)
    {
        var allocationList = allocations.ToList();

        var invoiceIds = allocationList
            .Where(x => x.TargetDocumentType == DocumentType.Invoice)
            .Select(x => x.TargetDocumentId)
            .Distinct()
            .ToList();

        if (invoiceIds.Count > 0)
        {
            var existingInvoiceCount = await db.Invoices.CountAsync(
                x => x.OrganizationId == organizationId && invoiceIds.Contains(x.Id)
                     && x.Status == Domain.Sales.InvoiceStatus.Approved,
                cancellationToken);

            if (existingInvoiceCount != invoiceIds.Count)
            {
                throw new NotFoundException("One or more allocation target invoices were not found or are not Approved.");
            }
        }

        var purchaseBillIds = allocationList
            .Where(x => x.TargetDocumentType == DocumentType.PurchaseBill)
            .Select(x => x.TargetDocumentId)
            .Distinct()
            .ToList();

        if (purchaseBillIds.Count > 0)
        {
            var existingPurchaseBillCount = await db.PurchaseBills.CountAsync(
                x => x.OrganizationId == organizationId && purchaseBillIds.Contains(x.Id)
                     && x.Status == Domain.Purchasing.PurchaseBillStatus.Approved,
                cancellationToken);

            if (existingPurchaseBillCount != purchaseBillIds.Count)
            {
                throw new NotFoundException("One or more allocation target purchase bills were not found or are not Approved.");
            }
        }
    }
}
