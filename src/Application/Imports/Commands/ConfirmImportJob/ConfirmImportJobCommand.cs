using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Imports.Commands.CreateImportJob;
using ErpApp.Domain.Imports;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports.Commands.ConfirmImportJob;

/// <summary>
/// <b>Confirm Upload</b> (Phase 38) -- the user has read the dry run's findings and wants the rows
/// that validated to be applied.
///
/// <para>This is the reference product's own wizard step 3 button, confirmed live in Phase 21a:
/// "Validating Records" shows <i>N records validated</i> and <i>N records have errors</i>, with
/// <b>Confirm Upload</b> and <b>Reupload New File</b> beneath it. There, the browser holds the
/// parsed rows between the two halves and posts them back; here the file is already durable and the
/// job already exists, so confirming is a state change on one row and the runner picks it up.</para>
///
/// <para><b>It re-queues rather than starting anything.</b> <c>ImportJob.ConfirmReview</c> stamps
/// <c>ReviewConfirmedAt</c> and puts the job back to Queued, which makes
/// <c>ImportJob.AwaitsValidation</c> false, which is the single condition
/// <c>ImportJobProcessor</c> reads to tell the two passes apart. Nothing else needed a new column,
/// and no second runner, table or queue exists for reviewed imports.</para>
///
/// <para><b>Only from PendingConfirmation.</b> Confirming a Queued job would be a no-op that reads
/// like an action; confirming a Running one would be a race with the runner; confirming a terminal
/// one is a stale screen. All three are the same 409, naming the status the job is actually in.
/// Discarding a reviewed import is <c>CancelImportJobCommand</c> -- nothing was written, so there is
/// nothing to undo, and no separate Discard command earns its place.</para>
/// </summary>
public sealed record ConfirmImportJobCommand(Guid OrganizationId, Guid Id)
    : IRequest<ImportJobSummary>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ImportJobManage;
}

public sealed class ConfirmImportJobCommandValidator : AbstractValidator<ConfirmImportJobCommand>
{
    public ConfirmImportJobCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class ConfirmImportJobCommandHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<ConfirmImportJobCommand, ImportJobSummary>
{
    public async Task<ImportJobSummary> Handle(ConfirmImportJobCommand request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Import job not found.");

        if (job.Status != ImportJobStatus.PendingConfirmation)
        {
            throw new ConflictException(
                $"This import is not waiting for confirmation (it is {job.Status}).");
        }

        job.ConfirmReview(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        var initiatedByName = await db.Users
            .Where(u => u.Id == job.InitiatedByUserId)
            .Select(u => u.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return ImportJobMapper.ToSummary(job, initiatedByName);
    }
}
