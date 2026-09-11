using ErpApp.Application.Accounting.Reports;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Queries.CashFlowSummary;

public sealed class CashFlowSummaryQueryHandler(IAppDbContext db, ICurrentUserService currentUser) : IRequestHandler<CashFlowSummaryQuery, CashFlowSummaryDto>
{
    private sealed record BankLine(decimal Debit, decimal Credit, DateTimeOffset PostedAt, DocumentType SourceDocumentType, Guid SourceDocumentId);

    public async Task<CashFlowSummaryDto> Handle(CashFlowSummaryQuery request, CancellationToken cancellationToken)
    {
        // Phase 35b -- TenantSettings.LocationWiseReportPermission: "Restrict users to view reports
        // only for locations they have access to." Null (unrestricted) unless the tenant has turned
        // the toggle on AND this caller's role carries location-specific grants, so no existing
        // tenant's figures change.
        var reportLocations = await LocationAccessScope.ForReportsAsync(
            db, currentUser, request.OrganizationId, cancellationToken);
        var entries = GlEntryLocations.ForReport(db, request.OrganizationId, request.LocationId, reportLocations);

        var fromUtc = GlDateBoundary.StartOfDayUtc(request.FromDate);
        var toUtc = GlDateBoundary.EndOfDayUtc(request.ToDate);

        var accountsQuery = db.Accounts.Where(a =>
            a.OrganizationId == request.OrganizationId && (a.Kind == AccountKind.Bank || a.Kind == AccountKind.Cash));
        if (request.BankAccountId is { } bankAccountId)
        {
            accountsQuery = accountsQuery.Where(a => a.Id == bankAccountId);
        }
        var bankAccountIds = await accountsQuery.Select(a => a.Id).ToListAsync(cancellationToken);

        var lines = await (
            from line in db.GlLines
            join entry in entries on line.GlJournalEntryId equals entry.Id
            where bankAccountIds.Contains(line.AccountId) && entry.PostedAt <= toUtc
            select new BankLine(line.Debit, line.Credit, entry.PostedAt, entry.SourceDocumentType, entry.SourceDocumentId))
            .ToListAsync(cancellationToken);

        var paymentIds = lines.Where(x => x.SourceDocumentType == DocumentType.Payment)
            .Select(x => x.SourceDocumentId).Distinct().ToList();
        var payments = await db.Payments
            .Where(p => paymentIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Direction, p.ContactId })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var contactIds = payments.Values.Select(p => p.ContactId).Distinct().ToList();
        var contactTypes = await db.Contacts
            .Where(c => contactIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Type })
            .ToDictionaryAsync(c => c.Id, c => c.Type, cancellationToken);

        var startingBalance = 0m;
        var receivedFromCustomerCashIn = 0m;
        var receivedFromCustomerCashOut = 0m;
        var otherReceiptsCashIn = 0m;
        var otherReceiptsCashOut = 0m;
        var paidToSupplierCashIn = 0m;
        var paidToSupplierCashOut = 0m;
        var otherPaymentsCashIn = 0m;
        var otherPaymentsCashOut = 0m;

        foreach (var line in lines)
        {
            if (line.PostedAt < fromUtc)
            {
                startingBalance += line.Debit - line.Credit;
                continue;
            }

            var isCustomerPayment = false;
            var isSupplierPayment = false;
            if (line.SourceDocumentType == DocumentType.Payment && payments.TryGetValue(line.SourceDocumentId, out var payment)
                && contactTypes.TryGetValue(payment.ContactId, out var contactType))
            {
                isCustomerPayment = payment.Direction == PaymentDirection.Received && contactType == ContactType.Customer;
                isSupplierPayment = payment.Direction == PaymentDirection.Paid && contactType == ContactType.Supplier;
            }

            if (isCustomerPayment)
            {
                receivedFromCustomerCashIn += line.Debit;
                receivedFromCustomerCashOut += line.Credit;
            }
            else if (isSupplierPayment)
            {
                paidToSupplierCashIn += line.Debit;
                paidToSupplierCashOut += line.Credit;
            }
            else
            {
                otherReceiptsCashIn += line.Debit;
                otherPaymentsCashOut += line.Credit;
            }
        }

        var endingBalance = startingBalance
            + (receivedFromCustomerCashIn - receivedFromCustomerCashOut)
            + (otherReceiptsCashIn - otherReceiptsCashOut)
            + (paidToSupplierCashIn - paidToSupplierCashOut)
            + (otherPaymentsCashIn - otherPaymentsCashOut);

        return new CashFlowSummaryDto(
            request.FromDate, request.ToDate, startingBalance,
            receivedFromCustomerCashIn, receivedFromCustomerCashOut,
            otherReceiptsCashIn, otherReceiptsCashOut,
            paidToSupplierCashIn, paidToSupplierCashOut,
            otherPaymentsCashIn, otherPaymentsCashOut,
            endingBalance);
    }
}
