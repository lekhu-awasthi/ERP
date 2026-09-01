using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Commands.CreateMigratedPurchaseRegisterEntry;

public sealed class CreateMigratedPurchaseRegisterEntryCommandHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<CreateMigratedPurchaseRegisterEntryCommand, CreateMigratedPurchaseRegisterEntryResult>
{
    public async Task<CreateMigratedPurchaseRegisterEntryResult> Handle(
        CreateMigratedPurchaseRegisterEntryCommand request, CancellationToken cancellationToken)
    {
        // Re-import safety -- see the Sales-side handler for the full reasoning. The unique index is
        // the guarantee; this check is what makes the second upload readable per row.
        var duplicate = await db.MigratedPurchaseRegisterEntries
            .AnyAsync(
                x => x.OrganizationId == request.OrganizationId && x.DocumentCode == request.DocumentCode,
                cancellationToken);

        if (duplicate)
        {
            throw new ConflictException(
                $"A migrated purchase register row with document number '{request.DocumentCode}' has already been "
                + "imported for this organization. Remove the existing row before re-importing it.");
        }

        // Exact-PAN match only, and never a Contact creation -- same rule as the Sales side.
        Guid? contactId = null;
        if (!string.IsNullOrWhiteSpace(request.PartyPan))
        {
            contactId = await db.Contacts
                .Where(x => x.OrganizationId == request.OrganizationId && x.Pan == request.PartyPan)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var entry = MigratedPurchaseRegisterEntry.Create(
            request.OrganizationId,
            request.Date,
            request.DocumentCode,
            request.ImportDeclarationNo,
            request.PartyName,
            request.PartyPan,
            contactId,
            request.TaxExemptValue,
            request.TaxableNonCapitalLocalValue,
            request.TaxableNonCapitalLocalVat,
            request.TaxableNonCapitalImportValue,
            request.TaxableNonCapitalImportVat,
            request.TaxableCapitalValue,
            request.TaxableCapitalVat,
            timeProvider.GetUtcNow());

        db.MigratedPurchaseRegisterEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateMigratedPurchaseRegisterEntryResult(entry.Id, entry.DocumentCode);
    }
}
