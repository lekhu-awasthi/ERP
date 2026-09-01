using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Imports;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Imports.Commands.CancelImportJob;

/// <summary>
/// Asks a queued or running import to stop (NFR-4.3 implies a user may walk away, and therefore may
/// come back and change their mind).
///
/// <para><b>What cancellation does not do is roll anything back.</b> Rows already applied are real
/// Products and Contacts that other records may already reference; deleting them would be a larger
/// and less reversible surprise than stopping where the user asked. The job's counts say exactly
/// how far it got, and the per-row grid says exactly which rows landed -- see
/// <c>ImportJob.MarkCancelled</c>.</para>
///
/// <para>This only raises a flag. The runner reads it between rows and never aborts mid-command,
/// because a create command's own transaction is the smallest unit that is safe to interrupt.</para>
/// </summary>
public sealed record CancelImportJobCommand(Guid OrganizationId, Guid Id)
    : IRequest<Unit>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ImportJobManage;
}

public sealed class CancelImportJobCommandValidator : AbstractValidator<CancelImportJobCommand>
{
    public CancelImportJobCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.Id).NotEmpty();
    }
}

public sealed class CancelImportJobCommandHandler(IAppDbContext db, TimeProvider timeProvider)
    : IRequestHandler<CancelImportJobCommand, Unit>
{
    public async Task<Unit> Handle(CancelImportJobCommand request, CancellationToken cancellationToken)
    {
        var job = await db.ImportJobs.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Import job not found.");

        if (job.IsTerminal)
        {
            throw new ConflictException($"This import has already finished ({job.Status}).");
        }

        job.RequestCancellation();

        // A Queued job has no runner to notice the flag, so it is retired here and now; a Running
        // one is left for the runner to finish cleanly at its next row boundary.
        if (job.Status == ImportJobStatus.Queued)
        {
            job.MarkCancelled(timeProvider.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
