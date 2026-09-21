using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Imports;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports.Commands.CreateImportJob;

public sealed class CreateImportJobCommandHandler(
    IAppDbContext db,
    IFileStorage fileStorage,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<CreateImportJobCommand, ImportJobSummary>
{
    public async Task<ImportJobSummary> Handle(CreateImportJobCommand request, CancellationToken cancellationToken)
    {
        // Phase 55 -- the account is checked before the file is stored, not per row. It is the
        // run's context, so a wrong one is a whole-file mistake and gets one 404 at upload rather
        // than the same row error 5,000 times (the reasoning behind the create-only rule above).
        // The per-row create command re-checks it anyway, because it must hold for any caller.
        if (request.BankAccountId is { } bankAccountId)
        {
            var account = await db.Accounts.SingleOrDefaultAsync(
                x => x.Id == bankAccountId && x.OrganizationId == request.OrganizationId, cancellationToken)
                ?? throw new NotFoundException("Bank account not found.");

            if (account.Kind is not (AccountKind.Bank or AccountKind.Cash))
            {
                throw new ValidationException(
                    [new ValidationFailure(
                        nameof(request.BankAccountId),
                        $"'{account.Name}' is not a cash or bank account, so it has no bank statement.")]);
            }
        }

        // The file is persisted before the job row, not after: a storage key on a job row must
        // always resolve, whereas an orphaned blob whose job row was never written is inert.
        var storageKey = await fileStorage.SaveAsync(request.Content, request.FileName, cancellationToken);

        var job = ImportJob.Create(
            request.OrganizationId,
            request.EntityType,
            request.Mode,
            storageKey,
            request.FileName,
            currentUser.UserId,
            timeProvider.GetUtcNow(),
            request.BankAccountId,
            request.ReviewBeforeApply);

        db.ImportJobs.Add(job);
        await db.SaveChangesAsync(cancellationToken);

        var initiatedByName = await db.Users
            .Where(u => u.Id == job.InitiatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return ImportJobMapper.ToSummary(job, initiatedByName);
    }
}
