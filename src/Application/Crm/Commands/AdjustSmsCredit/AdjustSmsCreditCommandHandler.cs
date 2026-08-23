using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Crm;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Crm.Commands.AdjustSmsCredit;

public sealed class AdjustSmsCreditCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<AdjustSmsCreditCommand, SmsCreditAdjustmentResult>
{
    public async Task<SmsCreditAdjustmentResult> Handle(AdjustSmsCreditCommand request, CancellationToken cancellationToken)
    {
        var entry = SmsCreditLedgerEntry.CreateManualAdjustment(
            request.OrganizationId, request.ChangeAmount, request.Reason, currentUser.UserId);

        db.SmsCreditLedgerEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        var newBalance = await db.SmsCreditLedgerEntries
            .Where(x => x.OrganizationId == request.OrganizationId)
            .SumAsync(x => x.ChangeAmount, cancellationToken);

        return new SmsCreditAdjustmentResult(entry.Id, entry.ChangeAmount, newBalance);
    }
}
