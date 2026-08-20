using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Queries.ListAllocatablePayments;

public sealed class ListAllocatablePaymentsQueryHandler(IAppDbContext db)
    : IRequestHandler<ListAllocatablePaymentsQuery, PagedResult<AllocatablePaymentDto>>
{
    public async Task<PagedResult<AllocatablePaymentDto>> Handle(
        ListAllocatablePaymentsQuery request, CancellationToken cancellationToken)
    {
        var paymentRows = await (
                from payment in db.Payments
                join contact in db.Contacts on payment.ContactId equals contact.Id
                where payment.OrganizationId == request.OrganizationId
                    && payment.Direction == request.Direction
                    && payment.Status == PaymentStatus.Approved
                    && (request.ContactId == null || payment.ContactId == request.ContactId)
                select new { payment.Id, payment.Code, payment.Date, payment.ContactId, ContactName = contact.Name, payment.Amount })
            .ToListAsync(cancellationToken);

        var paymentIds = paymentRows.Select(x => x.Id).ToList();
        var paymentAllocated = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.Payment && paymentIds.Contains(x.SourceId))
            .GroupBy(x => x.SourceId)
            .Select(g => new { SourceId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.SourceId, x => x.Allocated, cancellationToken);

        // Decision #2 -- a JournalVoucher line is an allocatable credit when it's tagged with a
        // Contact of the matching type and has a nonzero amount on the side that reduces that
        // Contact's control account (Customer/AR -> Credit, Supplier/AP -> Debit).
        var contactType = request.Direction == PaymentDirection.Received ? ContactType.Customer : ContactType.Supplier;

        var journalVoucherRows = await (
                from line in db.JournalVoucherLines
                join journalVoucher in db.JournalVouchers on line.JournalVoucherId equals journalVoucher.Id
                join contact in db.Contacts on line.ContactId equals contact.Id
                where journalVoucher.OrganizationId == request.OrganizationId
                    && journalVoucher.Status == JournalVoucherStatus.Approved
                    && contact.Type == contactType
                    && (request.ContactId == null || contact.Id == request.ContactId)
                select new
                {
                    line.Id,
                    ParentId = journalVoucher.Id,
                    journalVoucher.Code,
                    journalVoucher.Date,
                    ContactId = contact.Id,
                    ContactName = contact.Name,
                    line.Debit,
                    line.Credit,
                })
            .ToListAsync(cancellationToken);

        var lineIds = journalVoucherRows.Select(x => x.Id).ToList();
        var lineAllocated = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.JournalVoucher && lineIds.Contains(x.SourceId))
            .GroupBy(x => x.SourceId)
            .Select(g => new { SourceId = g.Key, Allocated = g.Sum(x => x.Amount) })
            .ToDictionaryAsync(x => x.SourceId, x => x.Allocated, cancellationToken);

        var merged = new List<AllocatablePaymentDto>();

        foreach (var payment in paymentRows)
        {
            var allocated = paymentAllocated.GetValueOrDefault(payment.Id);
            merged.Add(new AllocatablePaymentDto(
                DocumentType.Payment, payment.Id, null, payment.Code, payment.Date, payment.ContactId, payment.ContactName,
                payment.Amount, allocated, payment.Amount - allocated));
        }

        foreach (var line in journalVoucherRows)
        {
            var amount = contactType == ContactType.Customer ? line.Credit : line.Debit;
            if (amount <= 0)
            {
                continue;
            }

            var allocated = lineAllocated.GetValueOrDefault(line.Id);
            merged.Add(new AllocatablePaymentDto(
                DocumentType.JournalVoucher, line.Id, line.ParentId, line.Code, line.Date, line.ContactId, line.ContactName,
                amount, allocated, amount - allocated));
        }

        var filtered = request.ShowAllocated
            ? merged.Where(x => x.Balance <= 0)
            : merged.Where(x => x.Balance > 0);

        var ordered = filtered.OrderByDescending(x => x.Date).ToList();

        return ordered.ToPagedResult(request.Page, request.PageSize);
    }
}
