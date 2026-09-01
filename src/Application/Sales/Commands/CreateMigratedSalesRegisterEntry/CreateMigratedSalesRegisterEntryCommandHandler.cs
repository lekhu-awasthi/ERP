using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.CreateMigratedSalesRegisterEntry;

public sealed class CreateMigratedSalesRegisterEntryCommandHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<CreateMigratedSalesRegisterEntryCommand, CreateMigratedSalesRegisterEntryResult>
{
    public async Task<CreateMigratedSalesRegisterEntryResult> Handle(
        CreateMigratedSalesRegisterEntryCommand request, CancellationToken cancellationToken)
    {
        // Re-import safety, and the reason it is checked here as well as by a unique index. A
        // cutover import is the upload a user is most likely to run twice by accident, and the
        // consequence of a silent duplicate is a doubled statutory sales figure. The index in
        // MigratedSalesRegisterEntryConfiguration is the real guarantee under concurrency; this
        // check is what turns the second upload's rows into a readable per-row message instead of a
        // raw DbUpdateException. Note the InMemory provider enforces no unique index at all, so unit
        // tests only ever exercise this half -- see docs/phase-21c-status.md's testing section.
        var duplicate = await db.MigratedSalesRegisterEntries
            .AnyAsync(
                x => x.OrganizationId == request.OrganizationId && x.DocumentCode == request.DocumentCode,
                cancellationToken);

        if (duplicate)
        {
            throw new ConflictException(
                $"A migrated sales register row with document number '{request.DocumentCode}' has already been "
                + "imported for this organization. Remove the existing row before re-importing it.");
        }

        // Best-effort link only, and never by name: an exact PAN match is a strong identity claim,
        // whereas two contacts sharing a trading name are common. No Contact is ever created --
        // inventing master data to satisfy a report column would put junk in the tenant's customer
        // list, so the register falls back to the free-text party the prior system printed.
        Guid? contactId = null;
        if (!string.IsNullOrWhiteSpace(request.PartyPan))
        {
            contactId = await db.Contacts
                .Where(x => x.OrganizationId == request.OrganizationId && x.Pan == request.PartyPan)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var entry = MigratedSalesRegisterEntry.Create(
            request.OrganizationId,
            request.Date,
            request.DocumentCode,
            request.PartyName,
            request.PartyPan,
            contactId,
            request.TotalValue,
            request.TaxExemptValue,
            request.TaxableValue,
            request.VatAmount,
            request.ExportValue,
            request.ExportCountry,
            request.ExportDeclarationNo,
            request.ExportDeclarationDate,
            timeProvider.GetUtcNow());

        db.MigratedSalesRegisterEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return new CreateMigratedSalesRegisterEntryResult(entry.Id, entry.DocumentCode);
    }
}
