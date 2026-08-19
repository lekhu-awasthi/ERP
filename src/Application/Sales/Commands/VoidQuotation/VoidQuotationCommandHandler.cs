using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Sales.Commands.VoidQuotation;

/// <summary>
/// Only an Approved-not-Converted quotation reaches here -- Quotation.Void's own EnsureApproved
/// guard rejects a Converted one (409) since Converted is a distinct status, no separate
/// dependent-lookup needed (roadmap Phase 16a). No GL, no stock -- Quotation never posts either.
/// </summary>
public sealed class VoidQuotationCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<VoidQuotationCommand, VoidQuotationResult>
{
    public async Task<VoidQuotationResult> Handle(VoidQuotationCommand request, CancellationToken cancellationToken)
    {
        var quotation = await db.Quotations.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Quotation not found.");

        if (quotation.Status != QuotationStatus.Approved)
        {
            throw new ConflictException("Only an Approved quotation can be voided.");
        }

        quotation.Void(currentUser.UserId);

        await db.SaveChangesAsync(cancellationToken);

        return new VoidQuotationResult(quotation.Id, quotation.Code, quotation.Status, quotation.VoidedAt);
    }
}
