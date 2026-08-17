using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.TransactionApproval;

public sealed class TransactionApprovalQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<TransactionApprovalQuery, TransactionApprovalQueueDto>
{
    public async Task<TransactionApprovalQueueDto> Handle(
        TransactionApprovalQuery request, CancellationToken cancellationToken)
    {
        // Same OrganizationMemberships/RolePermissions join AuthorizationBehavior performs for a
        // single PermissionKey -- resolved once here as a set, since this query needs to check up
        // to 13 different *.Approve keys against the acting user's Role.
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

        var rows = new List<TransactionApprovalRowDto>();

        if (grantedKeys.Contains(PermissionKeys.QuotationApprove))
        {
            var items = await db.Quotations
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == QuotationStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.Quotation, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.SalesOrderApprove))
        {
            var items = await db.SalesOrders
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == SalesOrderStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.SalesOrder, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.InvoiceApprove))
        {
            var items = await db.Invoices
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InvoiceStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.Invoice, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.CreditNoteApprove))
        {
            var items = await db.CreditNotes
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CreditNoteStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.CreditNote, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.PurchaseOrderApprove))
        {
            var items = await db.PurchaseOrders
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseOrderStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.PurchaseOrder, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.PurchaseBillApprove))
        {
            var items = await db.PurchaseBills
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PurchaseBillStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.PurchaseBill, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.ExpenseApprove))
        {
            // Expense has no plain Reference field -- SupplierInvoiceReference is its closest
            // equivalent (see phase-12-status.md's scope decision).
            var items = await db.Expenses
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == ExpenseStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.SupplierInvoiceReference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.Expense, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null,
                x.SupplierInvoiceReference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.DebitNoteApprove))
        {
            var items = await db.DebitNotes
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == DebitNoteStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.DebitNote, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.JournalVoucherApprove))
        {
            var items = await db.JournalVouchers
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == JournalVoucherStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.JournalVoucher, x.Id, x.Code, x.Date, x.CreatedAt, null, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.CashTransferApprove))
        {
            var items = await db.CashTransfers
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == CashTransferStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.CashTransfer, x.Id, x.Code, x.Date, x.CreatedAt, null, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.WarehouseTransferApprove))
        {
            var items = await db.WarehouseTransfers
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == WarehouseTransferStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.WarehouseTransfer, x.Id, x.Code, x.Date, x.CreatedAt, null, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.InventoryAdjustmentApprove))
        {
            var items = await db.InventoryAdjustments
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == InventoryAdjustmentStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.Reference })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.InventoryAdjustment, x.Id, x.Code, x.Date, x.CreatedAt, null, null, x.Reference, null)));
        }

        if (grantedKeys.Contains(PermissionKeys.PaymentApprove))
        {
            var items = await db.Payments
                .Where(x => x.OrganizationId == request.OrganizationId && x.Status == PaymentStatus.Draft)
                .Select(x => new { x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, x.Reference, x.Direction })
                .ToListAsync(cancellationToken);
            rows.AddRange(items.Select(x => new TransactionApprovalRowDto(
                DocumentType.Payment, x.Id, x.Code, x.Date, x.CreatedAt, x.ContactId, null, x.Reference, x.Direction)));
        }

        var contactIds = rows.Where(x => x.ContactId is not null).Select(x => x.ContactId!.Value).Distinct().ToList();
        var contactNames = await db.Contacts
            .Where(x => x.OrganizationId == request.OrganizationId && contactIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var orderedRows = rows
            .Select(x => x.ContactId is not null
                ? x with { ContactName = contactNames.GetValueOrDefault(x.ContactId.Value) }
                : x)
            .OrderBy(x => x.Date).ThenBy(x => x.CreatedAt)
            .ToList();

        return new TransactionApprovalQueueDto(orderedRows);
    }
}
