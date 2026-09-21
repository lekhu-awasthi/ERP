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

        // Phase 56, and the guard phase 55 could only describe because the table did not exist yet.
        //
        // The reference product does NOT do this: it deleted a reconciled line without complaint
        // (probed live, 2026-09-21). Its cascade is at least correct -- the matched book rows were
        // released -- but it leaves the reconciliation itself alive holding two empty lists, so its
        // tenants accumulate shells. We refuse instead, for a reason its behaviour makes plain: the
        // undo here is "undo this import", one click over a whole file, and letting that silently
        // dissolve reconciliations somebody made afterwards is the kind of invisible side effect
        // this codebase rules against. The user unreconciles first, which is one extra step and is
        // the step where they see what they are undoing.
        var reconciled = lines.Where(x => x.ReconciliationId is not null).ToList();

        if (reconciled.Count > 0)
        {
            throw new ConflictException(
                $"{reconciled.Count} of these bank statement lines have been reconciled and cannot "
                + "be deleted. Unreconcile them first.");
        }

        db.BankStatementLines.RemoveRange(lines);
        await db.SaveChangesAsync(cancellationToken);

        return new DeleteBankStatementLinesResult(lines.Count);
    }
}
