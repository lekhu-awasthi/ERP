using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.TransactionList;

public sealed class TransactionListQueryHandler(IAppDbContext db)
    : IRequestHandler<TransactionListQuery, PagedResult<TransactionListRowDto>>
{
    /// <summary>A row before its Amount, Description and user names have been resolved -- the same
    /// two-pass shape RecentTransactionsQueryHandler uses, so only the returned page pays for the
    /// extra round trips.</summary>
    private sealed record Candidate(
        DateOnly Date,
        DocumentType DocumentType,
        Guid DocumentId,
        string Code,
        string? Reference,
        TransactionListStatus Status,
        Guid? ApprovedByUserId,
        DateTimeOffset? ApprovedAt,
        DateTimeOffset CreatedAt,
        Guid? ContactId,
        string? Notes,
        decimal OwnAmount,
        PaymentDirection? Direction);

    public async Task<PagedResult<TransactionListRowDto>> Handle(
        TransactionListQuery request, CancellationToken cancellationToken)
    {
        var organizationId = request.OrganizationId;
        var from = request.FromDate;
        var to = request.ToDate;

        bool Wants(DocumentType type) =>
            request.DocumentTypes is null || request.DocumentTypes.Count == 0 || request.DocumentTypes.Contains(type);

        var candidates = new List<Candidate>();

        // One concrete block per document type. Not a generic Func-parameterised helper -- see the
        // query's own doc comment and phase-9 bug #1.

        if (Wants(DocumentType.Quotation))
        {
            var statuses = TypeStatuses<QuotationStatus>(request.Statuses);
            var query = db.Quotations.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.Quotation, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, 0m, null)));
        }

        if (Wants(DocumentType.SalesOrder))
        {
            var statuses = TypeStatuses<SalesOrderStatus>(request.Statuses);
            var query = db.SalesOrders.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.SalesOrder, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, 0m, null)));
        }

        if (Wants(DocumentType.Invoice))
        {
            var statuses = TypeStatuses<InvoiceStatus>(request.Statuses);
            var query = db.Invoices.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.Invoice, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, 0m, null)));
        }

        if (Wants(DocumentType.CreditNote))
        {
            var statuses = TypeStatuses<CreditNoteStatus>(request.Statuses);
            var query = db.CreditNotes.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.CreditNote, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, 0m, null)));
        }

        if (Wants(DocumentType.PurchaseOrder))
        {
            var statuses = TypeStatuses<PurchaseOrderStatus>(request.Statuses);
            var query = db.PurchaseOrders.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.PurchaseOrder, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, 0m, null)));
        }

        if (Wants(DocumentType.PurchaseBill))
        {
            var statuses = TypeStatuses<PurchaseBillStatus>(request.Statuses);
            var query = db.PurchaseBills.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.PurchaseBill, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, 0m, null)));
        }

        if (Wants(DocumentType.Expense))
        {
            // Expense has no plain Reference field -- SupplierInvoiceReference is its closest
            // equivalent, the same substitution TransactionApprovalQueryHandler makes. It is also
            // the one document type in the codebase carrying freetext Notes, which the live report's
            // Description column appends to the contact name.
            var statuses = TypeStatuses<ExpenseStatus>(request.Statuses);
            var query = db.Expenses.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new
                {
                    x.Id, x.Code, x.Date, x.SupplierInvoiceReference, x.Status, x.ApprovedByUserId,
                    x.ApprovedAt, x.CreatedAt, x.ContactId, x.Notes,
                })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.Expense, x.Id, x.Code, x.SupplierInvoiceReference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, x.Notes, 0m, null)));
        }

        if (Wants(DocumentType.DebitNote))
        {
            var statuses = TypeStatuses<DebitNoteStatus>(request.Statuses);
            var query = db.DebitNotes.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.DebitNote, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, 0m, null)));
        }

        if (Wants(DocumentType.JournalVoucher))
        {
            var statuses = TypeStatuses<JournalVoucherStatus>(request.Statuses);
            var query = db.JournalVouchers.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.JournalVoucher, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, null, null, 0m, null)));
        }

        if (Wants(DocumentType.CashTransfer))
        {
            var statuses = TypeStatuses<CashTransferStatus>(request.Statuses);
            var query = db.CashTransfers.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.CashTransfer, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, null, null, 0m, null)));
        }

        if (Wants(DocumentType.WarehouseTransfer))
        {
            var statuses = TypeStatuses<WarehouseTransferStatus>(request.Statuses);
            var query = db.WarehouseTransfers.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.WarehouseTransfer, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, null, null, 0m, null)));
        }

        if (Wants(DocumentType.InventoryAdjustment))
        {
            var statuses = TypeStatuses<InventoryAdjustmentStatus>(request.Statuses);
            var query = db.InventoryAdjustments.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new { x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.InventoryAdjustment, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, null, null, 0m, null)));
        }

        if (Wants(DocumentType.Payment))
        {
            var statuses = TypeStatuses<PaymentStatus>(request.Statuses);
            var query = db.Payments.Where(x => x.OrganizationId == organizationId);
            if (statuses is not null) query = query.Where(x => statuses.Contains(x.Status));
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } t) query = query.Where(x => x.Date <= t);
            var items = await query
                .Select(x => new
                {
                    x.Id, x.Code, x.Date, x.Reference, x.Status, x.ApprovedByUserId, x.ApprovedAt,
                    x.CreatedAt, x.ContactId, x.Amount, x.Direction,
                })
                .ToListAsync(cancellationToken);
            candidates.AddRange(items.Select(x => new Candidate(
                x.Date, DocumentType.Payment, x.Id, x.Code, x.Reference, ListStatus(x.Status),
                x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt, x.ContactId, null, x.Amount, x.Direction)));
        }

        // Newest first, the way a "what exists" register reads. CreatedAt breaks ties within a day
        // and DocumentId makes the order total, so paging can never show or skip a row twice.
        var ordered = candidates
            .OrderByDescending(x => x.Date)
            .ThenByDescending(x => x.CreatedAt)
            .ThenBy(x => x.DocumentId)
            .ToList();

        var paged = request.ExportAll ? ordered.ToUnpagedResult() : ordered.ToPagedResult(request.Page, request.PageSize);
        var rows = await ResolveAsync(organizationId, paged.Items, cancellationToken);

        return new PagedResult<TransactionListRowDto>(rows, paged.Page, paged.PageSize, paged.TotalCount);
    }

    private async Task<IReadOnlyList<TransactionListRowDto>> ResolveAsync(
        Guid organizationId, IReadOnlyList<Candidate> page, CancellationToken cancellationToken)
    {
        if (page.Count == 0)
        {
            return [];
        }

        var amounts = new Dictionary<Guid, decimal>();

        async Task SumAsync<TLine>(IQueryable<TLine> lines, Func<TLine, Guid> parentId, Func<TLine, decimal> amount)
            where TLine : class
        {
            // The selectors are only ever applied to the materialised list, never inside a
            // translated Where, so the captured-Func translation gotcha does not apply.
            var materialised = await lines.ToListAsync(cancellationToken);
            foreach (var group in materialised.GroupBy(parentId))
            {
                amounts[group.Key] = group.Sum(amount);
            }
        }

        List<Guid> IdsOf(DocumentType type) =>
            [.. page.Where(x => x.DocumentType == type).Select(x => x.DocumentId)];

        var quotationIds = IdsOf(DocumentType.Quotation);
        if (quotationIds.Count > 0)
        {
            await SumAsync(db.QuotationLines.Where(l => quotationIds.Contains(l.QuotationId)), l => l.QuotationId, l => l.Amount + l.VatAmount);
        }

        var salesOrderIds = IdsOf(DocumentType.SalesOrder);
        if (salesOrderIds.Count > 0)
        {
            await SumAsync(db.SalesOrderLines.Where(l => salesOrderIds.Contains(l.SalesOrderId)), l => l.SalesOrderId, l => l.Amount + l.VatAmount);
        }

        var invoiceIds = IdsOf(DocumentType.Invoice);
        if (invoiceIds.Count > 0)
        {
            await SumAsync(db.InvoiceLines.Where(l => invoiceIds.Contains(l.InvoiceId)), l => l.InvoiceId, l => l.Amount + l.VatAmount);
        }

        var creditNoteIds = IdsOf(DocumentType.CreditNote);
        if (creditNoteIds.Count > 0)
        {
            await SumAsync(db.CreditNoteLines.Where(l => creditNoteIds.Contains(l.CreditNoteId)), l => l.CreditNoteId, l => l.Amount + l.VatAmount);
        }

        var purchaseOrderIds = IdsOf(DocumentType.PurchaseOrder);
        if (purchaseOrderIds.Count > 0)
        {
            await SumAsync(db.PurchaseOrderLines.Where(l => purchaseOrderIds.Contains(l.PurchaseOrderId)), l => l.PurchaseOrderId, l => l.Amount + l.VatAmount);
        }

        var purchaseBillIds = IdsOf(DocumentType.PurchaseBill);
        if (purchaseBillIds.Count > 0)
        {
            await SumAsync(db.PurchaseBillLines.Where(l => purchaseBillIds.Contains(l.PurchaseBillId)), l => l.PurchaseBillId, l => l.Amount + l.VatAmount);
        }

        var expenseIds = IdsOf(DocumentType.Expense);
        if (expenseIds.Count > 0)
        {
            await SumAsync(db.ExpenseLines.Where(l => expenseIds.Contains(l.ExpenseId)), l => l.ExpenseId, l => l.Amount + l.VatAmount);
        }

        var debitNoteIds = IdsOf(DocumentType.DebitNote);
        if (debitNoteIds.Count > 0)
        {
            await SumAsync(db.DebitNoteLines.Where(l => debitNoteIds.Contains(l.DebitNoteId)), l => l.DebitNoteId, l => l.Amount + l.VatAmount);
        }

        // A Journal Voucher is balanced by construction, so its debit side is its headline figure.
        var journalVoucherIds = IdsOf(DocumentType.JournalVoucher);
        if (journalVoucherIds.Count > 0)
        {
            await SumAsync(db.JournalVoucherLines.Where(l => journalVoucherIds.Contains(l.JournalVoucherId)), l => l.JournalVoucherId, l => l.Debit);
        }

        var cashTransferIds = IdsOf(DocumentType.CashTransfer);
        if (cashTransferIds.Count > 0)
        {
            await SumAsync(db.CashTransferLines.Where(l => cashTransferIds.Contains(l.CashTransferId)), l => l.CashTransferId, l => l.Amount);
        }

        // An Inventory Adjustment's value: the FIFO cost actually consumed where one was stamped at
        // Approve, and the entered unit cost otherwise (a Draft, or an Increase, has no consumed cost).
        var inventoryAdjustmentIds = IdsOf(DocumentType.InventoryAdjustment);
        if (inventoryAdjustmentIds.Count > 0)
        {
            await SumAsync(
                db.InventoryAdjustmentLines.Where(l => inventoryAdjustmentIds.Contains(l.InventoryAdjustmentId)),
                l => l.InventoryAdjustmentId,
                l => l.Quantity * (l.ConsumedUnitCost ?? l.UnitCost));
        }

        var contactIds = page.Where(x => x.ContactId is not null).Select(x => x.ContactId!.Value).Distinct().ToList();
        var contactNames = await db.Contacts
            .Where(x => x.OrganizationId == organizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        // Created By is derived from the audit trail -- no transactional aggregate stores a creator.
        // See the DTO's own doc comment for why a missing Create row reports null rather than a guess.
        var documentIds = page.Select(x => x.DocumentId).ToList();
        var creators = (await db.Audits
                .Where(x => x.OrganizationId == organizationId && x.Action == "Create" && documentIds.Contains(x.DocumentId))
                .Select(x => new { x.DocumentId, x.UserId, x.CreatedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.DocumentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.CreatedAt).First().UserId);

        var userIds = creators.Values
            .Concat(page.Where(x => x.ApprovedByUserId is not null).Select(x => x.ApprovedByUserId!.Value))
            .Distinct()
            .ToList();
        var userNames = await db.Users
            .Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName })
            .ToDictionaryAsync(x => x.Id, x => x.FullName, cancellationToken);

        return
        [
            .. page.Select(x =>
            {
                var createdByUserId = creators.TryGetValue(x.DocumentId, out var creator) ? creator : (Guid?)null;
                var contactName = x.ContactId is null ? null : contactNames.GetValueOrDefault(x.ContactId.Value);
                return new TransactionListRowDto(
                    x.Date,
                    x.DocumentType,
                    x.DocumentId,
                    x.Code,
                    x.Reference,
                    x.Status,
                    x.DocumentType == DocumentType.Payment ? x.OwnAmount : amounts.GetValueOrDefault(x.DocumentId),
                    createdByUserId,
                    createdByUserId is null ? null : userNames.GetValueOrDefault(createdByUserId.Value),
                    x.ApprovedByUserId,
                    x.ApprovedByUserId is null ? null : userNames.GetValueOrDefault(x.ApprovedByUserId.Value),
                    x.ApprovedAt,
                    x.CreatedAt,
                    Description(contactName, x.Notes),
                    x.Direction);
            }),
        ];
    }

    private static string? Description(string? contactName, string? notes) =>
        string.Join(" — ", new[] { contactName, notes }.Where(x => !string.IsNullOrWhiteSpace(x))) is { Length: > 0 } text
            ? text
            : null;

    /// <summary>
    /// Maps one document type's own status onto the shared report status <b>by name</b>, never by
    /// ordinal. Every one of the 13 status enums is a by-name subset of TransactionListStatus, and
    /// TransactionListStatusMappingTests asserts that -- so this parse cannot fail for a status that
    /// exists, and a future enum member added to one type without adding it here fails a test rather
    /// than silently reporting the wrong state.
    /// </summary>
    private static TransactionListStatus ListStatus<TStatus>(TStatus status)
        where TStatus : struct, Enum =>
        Enum.Parse<TransactionListStatus>(status.ToString());

    /// <summary>
    /// Translates the request's shared statuses into the ones this document type actually has,
    /// dropping any it does not (only Quotation and PurchaseOrder have Converted). Null means "no
    /// status filter"; an empty list means the filter selected nothing this type can be in, which
    /// correctly matches no rows.
    /// </summary>
    private static List<TStatus>? TypeStatuses<TStatus>(IReadOnlyList<TransactionListStatus>? statuses)
        where TStatus : struct, Enum =>
        statuses is null || statuses.Count == 0
            ? null
            : [.. statuses
                .Select(s => Enum.TryParse<TStatus>(s.ToString(), out var mapped) ? mapped : (TStatus?)null)
                .Where(x => x is not null)
                .Select(x => x!.Value)];
}
