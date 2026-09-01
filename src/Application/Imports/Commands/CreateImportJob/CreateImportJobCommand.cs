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
/// fact. Restoring a pre-commit review step on top of this design is additive (a validate-only mode
/// plus a confirm command), and is recorded as deferred rather than dismissed.</para>
/// </summary>
public sealed record CreateImportJobCommand(
    Guid OrganizationId,
    ImportEntityType EntityType,
    ImportMode Mode,
    string FileName,
    long FileSizeBytes,
    Stream Content)
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
    DateTimeOffset? CompletedAt);
