using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Exports;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Exports.Commands.CancelExportJob;

/// <summary>
/// Asks a queued or running export to stop.
///
/// <para><b>Cancellation is cleaner here than for an import, and the reason is worth stating.</b> A
/// cancelled import leaves behind real Products and Contacts that other records may already
/// reference, so Phase 21a had to keep them and explain itself. An export has produced nothing until
/// its very last step -- the workbook is built in memory and only saved to storage in the same
/// commit that marks the job Completed -- so a cancelled export leaves no artifact and nothing to
/// reconcile.</para>
///
/// <para>This only raises a flag; the runner reads it between categories. It never aborts a
/// category mid-read, because a partially-read sheet has no meaning and re-reading is free.</para>
/// </summary>
public sealed record CancelExportJobCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ExportJobManage;
}

public sealed class CancelExportJobCommandValidator : AbstractValidator<CancelExportJobCommand>
{
    public CancelExportJobCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class CancelExportJobCommandHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<CancelExportJobCommand, Unit>
{
    public async Task<Unit> Handle(CancelExportJobCommand request, CancellationToken cancellationToken)
    {
        var job = await db.ExportJobs.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Export job not found.");

        if (job.IsTerminal)
        {
            throw new ConflictException($"This export has already finished ({job.Status}).");
        }

        job.RequestCancellation();

        // A Queued job has no runner to notice the flag, so it is retired here and now; a Running
        // one is left for the runner to finish cleanly at its next category boundary.
        if (job.Status == ExportJobStatus.Queued)
        {
            job.MarkCancelled(timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
