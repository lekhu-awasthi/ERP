using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.ApproveQuotation;

public sealed class ApproveQuotationCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser)
    : IRequestHandler<ApproveQuotationCommand, ApproveQuotationResult>
{
    public async Task<ApproveQuotationResult> Handle(ApproveQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = await db.Quotations
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Quotation not found.");

        if (quotation.Status != QuotationStatus.Draft)
        {
            throw new ConflictException("Only a Draft quotation can be approved.");
        }

        if (quotation.Lines.Count == 0)
        {
            throw new ConflictException("A quotation needs at least one line to be approved.");
        }

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Quotation, cancellationToken);

        quotation.Approve(currentUser.UserId, code);

        await db.SaveChangesAsync(cancellationToken);

        return new ApproveQuotationResult(quotation.Id, quotation.Code, quotation.Status, quotation.ApprovedAt);
    }
}
