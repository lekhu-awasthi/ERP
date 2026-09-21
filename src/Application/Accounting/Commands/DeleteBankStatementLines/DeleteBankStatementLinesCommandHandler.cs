using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Accounting.Commands.DeleteBankStatementLines;

public sealed class DeleteBankStatementLinesCommandHandler(IAppDbContext db)
    : IRequestHandler<DeleteBankStatementLinesCommand, DeleteBankStatementLinesResult>
{
    public async Task<DeleteBankStatementLinesResult> Handle(
        DeleteBankStatementLinesCommand request, CancellationToken cancellationToken)
    {
        var accountExists = await db.Accounts.AnyAsync(
            x => x.Id == request.BankAccountId && x.OrganizationId == request.OrganizationId,
            cancellationToken);

        if (!accountExists)
        {
            throw new NotFoundException("Bank account not found.");
        }

        // Both halves of the filter are load-bearing and neither is redundant. OrganizationId is
        // the only tenant boundary there is -- no global query filter exists in this codebase --
        // and BankAccountId is what stops a caller passing ids belonging to another account of the
        // same tenant, which the request has no other reason to name.
        var query = db.BankStatementLines.Where(
            x => x.OrganizationId == request.OrganizationId && x.BankAccountId == request.BankAccountId);

        query = request.ImportJobId is { } importJobId
            ? query.Where(x => x.ImportJobId == importJobId)
            : query.Where(x => request.LineIds!.Contains(x.Id));

        var lines = await query.ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            // 404 rather than a silent success. A delete that reports "0 removed" when the caller
            // named specific rows is indistinguishable from a delete that hit another tenant's
            // rows and was filtered out, and the caller cannot tell which happened.
            throw new NotFoundException("No matching bank statement lines were found.");
        }

        db.BankStatementLines.RemoveRange(lines);
        await db.SaveChangesAsync(cancellationToken);

        return new DeleteBankStatementLinesResult(lines.Count);
    }
}
