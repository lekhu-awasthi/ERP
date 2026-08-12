using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Numbering;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Payments.Posting;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Payments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Payments.Commands.ApprovePayment;

public sealed class ApprovePaymentCommandHandler(
    IAppDbContext db, IDocumentNumberGenerator numberGenerator, ICurrentUserService currentUser,
    IGlPostingRule<PaymentPostingInput> postingRule)
    : IRequestHandler<ApprovePaymentCommand, ApprovePaymentResult>
{
    public async Task<ApprovePaymentResult> Handle(ApprovePaymentCommand request, CancellationToken cancellationToken)
    {
        var payment = await db.Payments
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Payment not found.");

        if (payment.Status != PaymentStatus.Draft)
        {
            throw new ConflictException("Only a Draft payment can be approved.");
        }

        if (payment.Allocations.Count == 0 || payment.Allocations.Sum(x => x.Amount) != payment.Amount)
        {
            throw new ConflictException("A payment's allocations must add up to exactly its Amount to be approved.");
        }

        var postingInput = await PaymentAccountResolver.ResolveAsync(
            db, request.OrganizationId, payment.AccountId, payment.Amount, cancellationToken);

        var code = await numberGenerator.GetNextNumberAsync(request.OrganizationId, DocumentType.Payment, cancellationToken);

        payment.Approve(currentUser.UserId, code);

        var glLines = postingRule.BuildLines(postingInput);
        var glEntry = GlJournalEntry.Post(request.OrganizationId, DocumentType.Payment, payment.Id, glLines);
        db.GlJournalEntries.Add(glEntry);

        await db.SaveChangesAsync(cancellationToken);

        return new ApprovePaymentResult(payment.Id, payment.Code, payment.Status, payment.ApprovedAt);
    }
}
