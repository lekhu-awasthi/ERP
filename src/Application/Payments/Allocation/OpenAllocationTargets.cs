using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Contacts.Queries.Ageing;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Allocation;

/// <summary>A document a payment may still be allocated to, with what is left of it in its own currency.</summary>
public sealed record OpenAllocationTargetDto(
    DocumentType TargetDocumentType,
    Guid Id,
    string Code,
    DateOnly Date,
    DateOnly DueDate,
    decimal Amount,
    decimal Balance,
    string CurrencyCode);

/// <summary>
/// Phase 88 -- <b>what a payment from this contact may still be allocated to</b>: the contact's approved invoices
/// (a receipt) or purchase bills (a supplier payment) with something left to settle, in the payment's currency, oldest
/// due first.
///
/// <para><b>Why it exists.</b> The four screens that allocate a payment (Customer Payment, Supplier Payment and both
/// Allocate screens) each loaded the first 200 approved documents of the whole organization and filtered them by
/// contact and currency in the browser. That listed documents already settled in full, and on a tenant past 200 an
/// old unpaid one was simply not offered. The FIFO <i>Suggest</i> did its own arithmetic again, counting only
/// payment-sourced allocations. Both now read <see cref="OutstandingDocumentReader"/>, the reader the ageing reports
/// agree through (phase 36), so what is offered, what is suggested and what the ageing reports call outstanding are
/// one figure.</para>
///
/// <para><b>Confirmed live (2026-10-12).</b> The reference product's payment form and its Allocate screen both ask the
/// server for one contact's documents with <c>allocated: false</c> and a positive total, sorted by <c>due_date</c>, and
/// show each with its amount and what is left to allocate. Neither offers a settled document.</para>
///
/// <para><b>Location.</b> The list narrows to the locations at which the caller may view the target type, exactly what
/// the invoice and bill lists it replaces did; the settlements against a listed document are never narrowed (phase
/// 35b), so a branch invoice paid at head office is still paid.</para>
/// </summary>
internal static class OpenAllocationTargets
{
    /// <summary>The permission that reads the target type: the list the picker used to be built from.</summary>
    public static string ViewKeyFor(PaymentDirection direction) =>
        direction == PaymentDirection.Received ? PermissionKeys.InvoiceView : PermissionKeys.PurchaseBillView;

    public static async Task<IReadOnlyList<OpenAllocationTargetDto>> LoadAsync(
        IAppDbContext db,
        ICurrentUserService currentUser,
        Guid organizationId,
        Guid contactId,
        PaymentDirection direction,
        string currencyCode,
        CancellationToken cancellationToken)
    {
        var locations = await LocationAccessScope.ForKeyAsync(
            db, currentUser, organizationId, ViewKeyFor(direction), cancellationToken);

        var (contactType, ageableType, targetType) = direction == PaymentDirection.Received
            ? (ContactType.Customer, AgeableDocumentType.Invoice, DocumentType.Invoice)
            : (ContactType.Supplier, AgeableDocumentType.PurchaseBill, DocumentType.PurchaseBill);

        // Every approved document, whatever its date: a payment may settle one dated after it, as it always could.
        var outstanding = (await OutstandingDocumentReader.LoadInDocumentCurrencyAsync(
                db, organizationId, contactType, DateOnly.MaxValue, cancellationToken, contactId,
                reportLocations: locations))
            .Where(x => x.Type == ageableType && x.Balance > 0)
            .ToList();

        if (outstanding.Count == 0)
        {
            return [];
        }

        // One contact's open documents: a short list, so naming them is cheap (phase 42's concern is a period's ids).
        var ids = outstanding.Select(x => x.Id).ToList();
        var currencies = targetType == DocumentType.Invoice
            ? await db.Invoices
                .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.CurrencyCode, cancellationToken)
            : await db.PurchaseBills
                .Where(x => x.OrganizationId == organizationId && ids.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.CurrencyCode, cancellationToken);

        return
        [
            .. outstanding
                .Select(x => new OpenAllocationTargetDto(
                    targetType, x.Id, x.Number, x.Date, x.DueDate, x.Amount, x.Balance,
                    currencies.GetValueOrDefault(x.Id) ?? CurrencyCatalog.BaseCode))
                .Where(x => string.Equals(x.CurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.DueDate)
                .ThenBy(x => x.Date)
                .ThenBy(x => x.Code, StringComparer.Ordinal),
        ];
    }
}
