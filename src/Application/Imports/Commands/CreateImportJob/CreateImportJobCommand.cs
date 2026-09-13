using ErpApp.Application.Common.Security;
using ErpApp.Domain.Imports;
using MediatR;

namespace ErpApp.Application.Imports.Commands.CreateImportJob;

/// <summary>
/// Enqueues a bulk import (FR-2.9). The uploaded workbook is streamed to <c>IFileStorage</c> and
/// the request returns immediately with a Queued job -- NFR-4.3's "shall run asynchronously and not
/// block the initiating user's session".
///
/// <para>This is the one point at which the acting user is authenticated and permission-checked by
/// a real request; <c>ImportJob.InitiatedByUserId</c> is captured here and the background runner
/// re-assumes it per row. See <see cref="IJobActingUser"/>.</para>
///
/// <para><b>This deliberately diverges from the reference product</b>, whose wizard uploads
/// synchronously to a dry-run endpoint, shows a "N records validated / N records have errors"
/// review step, and applies nothing until the user presses Confirm Upload -- with a 20-minute
/// client timeout and a "do not refresh this page" warning. That shape cannot satisfy NFR-4.3, and
/// its review step is bought by parsing the file twice and holding the parsed rows in the browser.
/// The per-row result grid this phase's job screen shows carries the same information after the
/// fact.</para>
///
/// <para><b>Phase 38 restored the review step, exactly as this comment predicted it would have to
/// be done: a validate-only pass plus a confirm command</b>, with no second runner and no second
/// table. <see cref="ReviewBeforeApply"/> defaults to <c>true</c> because that is the reference
/// product's behaviour and because a user who is about to create a thousand records should be shown
/// what will happen; it costs a second pass over the file and a wait for a human, which is stated
/// with its measured number in docs/phase-38-status.md. Turning it off is the Phase 21a behaviour
/// unchanged.</para>
/// </summary>
public sealed record CreateImportJobCommand(
    Guid OrganizationId,
    ImportEntityType EntityType,
    ImportMode Mode,
    string FileName,
    long FileSizeBytes,
    Stream Content,
    bool ReviewBeforeApply = true)
    : IRequest<ImportJobSummary>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.ImportJobManage;
}

/// <summary>List/detail row shape shared by the create, list and get responses.</summary>
public sealed record ImportJobSummary(
    Guid Id,
    ImportEntityType EntityType,
    ImportMode Mode,
    string FileName,
    ImportJobStatus Status,
    string? FailureReason,
    int TotalRowCount,
    int ProcessedRowCount,
    int SucceededRowCount,
    int FailedRowCount,
    bool CancellationRequested,
    Guid InitiatedByUserId,
    string InitiatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool ReviewBeforeApply = false,
    DateTimeOffset? ReviewConfirmedAt = null,
    int ValidatedRowCount = 0);
