using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;

public sealed class SetTransactionReportingTagsCommandHandler(IAppDbContext db)
    : IRequestHandler<SetTransactionReportingTagsCommand, Unit>
{
    public async Task<Unit> Handle(SetTransactionReportingTagsCommand request, CancellationToken cancellationToken)
    {
        await EnsureDocumentExistsAsync(db, request.OrganizationId, request.DocumentType, request.DocumentId, cancellationToken);

        var distinctTagOptionIds = request.TagOptionIds.Distinct().ToList();
        if (distinctTagOptionIds.Count > 0)
        {
            var validTagOptionCount = await db.ReportingTagOptions
                .CountAsync(x => x.OrganizationId == request.OrganizationId && distinctTagOptionIds.Contains(x.Id), cancellationToken);
            if (validTagOptionCount != distinctTagOptionIds.Count)
            {
                throw new NotFoundException("One or more reporting tag options were not found.");
            }
        }

        var existing = await db.TransactionReportingTags
            .Where(x => x.OrganizationId == request.OrganizationId
                && x.DocumentType == request.DocumentType && x.DocumentId == request.DocumentId)
            .ToListAsync(cancellationToken);
        db.TransactionReportingTags.RemoveRange(existing);

        foreach (var tagOptionId in distinctTagOptionIds)
        {
            db.TransactionReportingTags.Add(
                TransactionReportingTag.Create(request.OrganizationId, request.DocumentType, request.DocumentId, tagOptionId));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }

    private static async Task EnsureDocumentExistsAsync(
        IAppDbContext db, Guid organizationId, DocumentType documentType, Guid documentId, CancellationToken cancellationToken)
    {
        var exists = documentType switch
        {
            DocumentType.Quotation => await db.Quotations.AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            DocumentType.Invoice => await db.Invoices.AnyAsync(x => x.Id == documentId && x.OrganizationId == organizationId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(documentType), documentType, "Reporting tags are not supported on this document type."),
        };

        if (!exists)
        {
            throw new NotFoundException($"{documentType} not found.");
        }
    }
}
