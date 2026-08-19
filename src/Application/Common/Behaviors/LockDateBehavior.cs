using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Common.Behaviors;

/// <summary>
/// Enforces Organization.LockDate (roadmap Phase 16a, NFR-3.4) in the one shared place every
/// create/edit/approve/void of a lock-date-sensitive document flows through, rather than a
/// per-handler copy-pasted check -- the same "one pipeline behavior, not 50 hand-written guards"
/// discipline AuthorizationBehavior already established for permissions. Runs after
/// AuthorizationBehavior (registration order in DependencyInjection.AddApplication) so a caller
/// who isn't even allowed to touch this document type gets a 403 before this ever queries the
/// Organization's LockDate.
///
/// A request that implements neither <see cref="ILockDateSensitive"/> (Create/Update -- the date
/// is already on the request) nor <see cref="ILockDateSensitiveDocument"/> (Approve/Void -- the
/// date must be looked up from the document being approved/voided, which the command itself never
/// carries) skips this behavior entirely, the same "no marker interface, no gate" pattern
/// AuthorizationBehavior uses for IRequirePermission.
/// </summary>
public sealed class LockDateBehavior<TRequest, TResponse>(IAppDbContext db) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var candidateDate = request switch
        {
            ILockDateSensitive direct => direct.Date,
            ILockDateSensitiveDocument approval =>
                await ResolveDocumentDateAsync(approval.LockDateDocumentType, approval.LockDateDocumentId, cancellationToken),
            _ => (DateOnly?)null,
        };

        if (candidateDate is { } date && request is IOrganizationScoped scoped)
        {
            var lockDate = await db.Organizations
                .Where(x => x.Id == scoped.OrganizationId)
                .Select(x => x.LockDate)
                .SingleOrDefaultAsync(cancellationToken);

            if (lockDate is { } ld && date <= ld)
            {
                throw new ConflictException(
                    $"This organization's books are locked on or before {ld:yyyy-MM-dd}. " +
                    $"This action's date ({date:yyyy-MM-dd}) falls on or before the lock date -- " +
                    "choose a later date, or ask an Admin to move the lock date.");
            }
        }

        return await next();
    }

    /// <summary>A document not found here (deleted id, wrong org) resolves to a null date -- this
    /// behavior isn't the right place to 404, the handler's own SingleOrDefaultAsync-or-throw
    /// right after does that; a null date here just means the lock-date check is skipped and the
    /// handler's own not-found path runs immediately after.</summary>
    private async Task<DateOnly?> ResolveDocumentDateAsync(
        DocumentType documentType, Guid documentId, CancellationToken cancellationToken)
    {
        return documentType switch
        {
            DocumentType.Quotation => await db.Quotations
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.SalesOrder => await db.SalesOrders
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.Invoice => await db.Invoices
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.CreditNote => await db.CreditNotes
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.PurchaseOrder => await db.PurchaseOrders
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.PurchaseBill => await db.PurchaseBills
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.Expense => await db.Expenses
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.DebitNote => await db.DebitNotes
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.JournalVoucher => await db.JournalVouchers
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.CashTransfer => await db.CashTransfers
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.WarehouseTransfer => await db.WarehouseTransfers
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.InventoryAdjustment => await db.InventoryAdjustments
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            DocumentType.Payment => await db.Payments
                .Where(x => x.Id == documentId).Select(x => (DateOnly?)x.Date).SingleOrDefaultAsync(cancellationToken),
            _ => null,
        };
    }
}
