using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.RecentTransactions;

public sealed class RecentTransactionsQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<RecentTransactionsQuery, PagedResult<RecentTransactionRowDto>>
{
    /// <summary>A row before its Amount and ContactName have been resolved. Kept deliberately thin:
    /// the whole point of the two-pass shape below is that only the page the user actually asked for
    /// pays for its line sums.</summary>
    private sealed record Candidate(
        DateOnly Date,
        DateTimeOffset CreatedAt,
        DocumentType DocumentType,
        Guid DocumentId,
        string DocumentCode,
        Guid? ContactId,
        decimal Amount,
        PaymentDirection? Direction);

    public async Task<PagedResult<RecentTransactionRowDto>> Handle(
        RecentTransactionsQuery request, CancellationToken cancellationToken)
    {
        // The same OrganizationMemberships/RolePermissions join AuthorizationBehavior performs for a
        // single key, resolved once as a set because this query checks up to six different *.View
        // keys. Copied from TransactionApprovalQueryHandler (Phase 12), which is the precedent for a
        // multi-type feed gated per type.
        var grantedKeys = (await (
            from membership in db.OrganizationMemberships
            where membership.OrganizationId == request.OrganizationId
                  && membership.UserId == currentUser.UserId
                  && membership.Status == MembershipStatus.Accepted
            join rolePermission in db.RolePermissions
                on membership.RoleId equals rolePermission.RoleId
            where rolePermission.IsGranted
            select rolePermission.PermissionKey
        ).ToListAsync(cancellationToken)).ToHashSet();

        var filter = request.Filter;
        var wantsSales = filter is RecentTransactionFilter.All or RecentTransactionFilter.Sales;
        var wantsPurchase = filter is RecentTransactionFilter.All or RecentTransactionFilter.Purchase;
        var wantsPaid = filter is RecentTransactionFilter.All or RecentTransactionFilter.Payment;
        var wantsReceived = filter is RecentTransactionFilter.All or RecentTransactionFilter.Receipt;

        var candidates = new List<Candidate>();

        // Each block is its own concrete Where over its own DbSet rather than one generic helper
        // parameterized by a Func -- CLAUDE.md's known gotcha (and phase-9-status.md's bug #1): a
        // captured delegate inside .Where() compiles fine and then fails to translate against a real
        // SQL Server provider. Phase 12's queue made the same choice for the same reason.

        if (wantsSales && grantedKeys.Contains(PermissionKeys.InvoiceView))
        {
            var items = await db.Invoices
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Approved
                    && x.Date >= request.FromDate && x.Date <= request.ToDate)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, x.CreatedAt, DocumentType.Invoice, x.Id, x.Code, x.ContactId, 0m, null)));
        }

        if (wantsSales && grantedKeys.Contains(PermissionKeys.CreditNoteView))
        {
            var items = await db.CreditNotes
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Approved
                    && x.Date >= request.FromDate && x.Date <= request.ToDate)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, x.CreatedAt, DocumentType.CreditNote, x.Id, x.Code, x.ContactId, 0m, null)));
        }

        if (wantsPurchase && grantedKeys.Contains(PermissionKeys.PurchaseBillView))
        {
            var items = await db.PurchaseBills
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Approved
                    && x.Date >= request.FromDate && x.Date <= request.ToDate)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, x.CreatedAt, DocumentType.PurchaseBill, x.Id, x.Code, x.ContactId, 0m, null)));
        }

        if (wantsPurchase && grantedKeys.Contains(PermissionKeys.DebitNoteView))
        {
            var items = await db.DebitNotes
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Approved
                    && x.Date >= request.FromDate && x.Date <= request.ToDate)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, x.CreatedAt, DocumentType.DebitNote, x.Id, x.Code, x.ContactId, 0m, null)));
        }

        if (wantsPurchase && grantedKeys.Contains(PermissionKeys.ExpenseView))
        {
            var items = await db.Expenses
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == ExpenseStatus.Approved
                    && x.Date >= request.FromDate && x.Date <= request.ToDate)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, x.CreatedAt, DocumentType.Expense, x.Id, x.Code, x.ContactId, 0m, null)));
        }

        if ((wantsPaid || wantsReceived) && grantedKeys.Contains(PermissionKeys.PaymentView))
        {
            // One aggregate, two tabs. The Direction filter is applied in the database rather than
            // after materialising both, since All is the only case that wants both.
            var paymentQuery = db.Payments
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PaymentStatus.Approved
                    && x.Date >= request.FromDate && x.Date <= request.ToDate);
            if (!wantsPaid)
            {
                paymentQuery = paymentQuery.Where(x => x.Direction == PaymentDirection.Received);
            }
            else if (!wantsReceived)
            {
                paymentQuery = paymentQuery.Where(x => x.Direction == PaymentDirection.Paid);
            }

            var items = await paymentQuery
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Amount, x.Direction })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, x.CreatedAt, DocumentType.Payment, x.Id, x.Code, x.ContactId, x.Amount, x.Direction)));
        }

        // Most recent first -- this is a "what just happened" feed, the opposite of the approval
        // queue's oldest-first ordering. CreatedAt breaks ties within a day; DocumentId makes the
        // order total, so paging can never show or skip the same row twice.
        var ordered = candidates
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .ThenBy(x => x.DocumentId)
            .ToList();

        var paged = ordered.ToPagedResult(request.Page, request.PageSize);

        // Second pass: line sums and contact names are resolved for the returned page only, not for
        // every document in the range. The feed has no total, so nothing needs the rows it skipped.
        var rows = await ResolveAsync(request.OrganizationId, paged.Items, cancellationToken);

        return new PagedResult<RecentTransactionRowDto>(rows, paged.Page, paged.PageSize, paged.TotalCount);
    }

    private async Task<IReadOnlyList<RecentTransactionRowDto>> ResolveAsync(
        Guid organizationId, IReadOnlyList<Candidate> page, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var amounts = new Dictionary<Guid, decimal>();

        async Task SumAsync<TLine>(
            DocumentType type,
            IQueryable<TLine> lines,
            Func<TLine, Guid> parentId,
            Func<TLine, decimal> gross)
            where TLine : class
        {
            var ids = page.Where(x => x.DocumentType == type).Select(x => x.DocumentId).ToList();
            if (ids.Count == 0)
            {
                return;
            }
            // Materialise the page's lines and sum in memory. The selectors are only ever applied to
            // the materialised list -- never inside a translated Where -- so the captured-Func
            // translation gotcha does not apply here.
            var materialised = await lines.ToListAsync(cancellationToken);
            foreach (var group in materialised.GroupBy(parentId))
            {
                amounts[group.Key] = group.Sum(gross);
            }
        }

        var invoiceIds = page.Where(x => x.DocumentType == DocumentType.Invoice).Select(x => x.DocumentId).ToList();
        await SumAsync(
            DocumentType.Invoice,
            db.InvoiceLines.Where(l => invoiceIds.Contains(l.InvoiceId)),
            l => l.InvoiceId,
            l => l.Amount + l.VatAmount);

        var creditNoteIds = page.Where(x => x.DocumentType == DocumentType.CreditNote).Select(x => x.DocumentId).ToList();
        await SumAsync(
            DocumentType.CreditNote,
            db.CreditNoteLines.Where(l => creditNoteIds.Contains(l.CreditNoteId)),
            l => l.CreditNoteId,
            l => l.Amount + l.VatAmount);

        var billIds = page.Where(x => x.DocumentType == DocumentType.PurchaseBill).Select(x => x.DocumentId).ToList();
        await SumAsync(
            DocumentType.PurchaseBill,
            db.PurchaseBillLines.Where(l => billIds.Contains(l.PurchaseBillId)),
            l => l.PurchaseBillId,
            l => l.Amount + l.VatAmount);

        var debitNoteIds = page.Where(x => x.DocumentType == DocumentType.DebitNote).Select(x => x.DocumentId).ToList();
        await SumAsync(
            DocumentType.DebitNote,
            db.DebitNoteLines.Where(l => debitNoteIds.Contains(l.DebitNoteId)),
            l => l.DebitNoteId,
            l => l.Amount + l.VatAmount);

        var expenseIds = page.Where(x => x.DocumentType == DocumentType.Expense).Select(x => x.DocumentId).ToList();
        await SumAsync(
            DocumentType.Expense,
            db.ExpenseLines.Where(l => expenseIds.Contains(l.ExpenseId)),
            l => l.ExpenseId,
            l => l.Amount + l.VatAmount);

        var contactIds = page.Where(x => x.ContactId is not null).Select(x => x.ContactId!.Value).Distinct().ToList();
        var contactNames = await db.Contacts
            .Where(x => x.OrganizationId == organizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return page.Select(x => new RecentTransactionRowDto(
            x.Date,
            x.DocumentType,
            x.DocumentId,
            x.DocumentCode,
            x.ContactId,
            x.ContactId is null ? null : contactNames.GetValueOrDefault(x.ContactId.Value),
            // A Payment carries its own Amount; every other type is the sum of its lines. A document
            // whose lines somehow did not load shows 0 rather than throwing a feed off a dashboard.
            x.DocumentType == DocumentType.Payment ? x.Amount : amounts.GetValueOrDefault(x.DocumentId),
            x.Direction)).ToList();
    }
}
