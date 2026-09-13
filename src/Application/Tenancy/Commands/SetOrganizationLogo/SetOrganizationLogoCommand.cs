using ErpApp.Application.Common.Exceptions;
using FluentValidation;
using FluentValidation.Results;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Common.Storage;
using ErpApp.Domain.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.SetOrganizationLogo;

/// <summary>
/// Phase 39 — uploads the organization's logo, closing the phase-1b wizard gap.
///
/// <para><b>The rules come from the reference product's own hint text</b> (erp-module-scan.md §5):
/// optional, JPG/PNG/GIF, minimum 300×300, maximum 5 MB. What is different here is <i>how</i> they
/// are checked: format and dimensions are read from the file's own bytes by
/// <see cref="ImageHeader"/>, never from the multipart part's declared content type, which the
/// client writes and can lie about. This logo is embedded in every PDF the tenant sends a customer
/// and served back to every browser that opens the profile page, so "is this actually an image" is
/// the question worth answering.</para>
///
/// <para><b>Admin-only</b>, the same bar as <c>OrganizationLockDateManage</c> and for a
/// brand-shaped version of the same reason: the logo is on every outbound document, so changing it
/// changes what every customer sees the organization as. Reading it is Admin+Member, because a
/// Member who can print an invoice is already looking at it.</para>
///
/// <para><b>The deletion story, decided here</b> rather than deferred (phase-21b Decision E): a
/// replacement deletes the blob it replaced, and the delete happens <i>after</i> the row is
/// committed. The other order can leave the organization pointing at a file that is gone, which is a
/// broken logo on every document; this order can at worst leave one orphaned blob, which is
/// invisible.</para>
/// </summary>
public sealed record SetOrganizationLogoCommand(
    Guid OrganizationId, string FileName, long FileSizeBytes, Stream Content)
    : IRequest<OrganizationLogoResult>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.OrganizationProfileManage;
}

public sealed record OrganizationLogoResult(Guid OrganizationId, bool HasLogo, string? ContentType);

public sealed class SetOrganizationLogoCommandHandler(IAppDbContext db, IFileStorage storage)
    : IRequestHandler<SetOrganizationLogoCommand, OrganizationLogoResult>
{
    /// <summary>5 MB, the reference product's own cap.</summary>
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    /// <summary>300×300, likewise. Enforced on the real pixel dimensions rather than trusted.</summary>
    public const int MinimumSide = 300;

    public async Task<OrganizationLogoResult> Handle(
        SetOrganizationLogoCommand request, CancellationToken cancellationToken)
    {
        if (request.FileSizeBytes > MaxSizeBytes)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Content),
                $"The logo is larger than the {MaxSizeBytes / (1024 * 1024)} MB limit.")]);
        }

        var organization = await db.Organizations
            .FirstOrDefaultAsync(x => x.Id == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        // Buffered, because the header has to be read before the bytes are stored and an IFormFile's
        // stream is not reliably seekable. A 5 MB ceiling is what makes buffering safe to do at all,
        // and it is checked above rather than after.
        using var buffer = new MemoryStream();
        await request.Content.CopyToAsync(buffer, cancellationToken);

        if (buffer.Length > MaxSizeBytes)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Content),
                $"The logo is larger than the {MaxSizeBytes / (1024 * 1024)} MB limit.")]);
        }

        buffer.Position = 0;
        var header = ImageHeader.TryRead(buffer)
            ?? throw new ValidationException([new ValidationFailure(
                nameof(request.Content),
                "The logo must be a PNG, JPEG or GIF image. This file's contents are not one of those, "
                + "whatever its name or type says.")]);

        if (header.Width < MinimumSide || header.Height < MinimumSide)
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.Content),
                $"The logo must be at least {MinimumSide}×{MinimumSide} pixels. This one is "
                + $"{header.Width}×{header.Height}.")]);
        }

        buffer.Position = 0;
        var key = await storage.SaveAsync(
            buffer, $"organization-logo-{request.OrganizationId}{header.FileExtension}", cancellationToken);

        var replaced = organization.SetLogo(key, header.ContentType);
        await db.SaveChangesAsync(cancellationToken);

        if (replaced is not null)
        {
            await storage.DeleteAsync(replaced, cancellationToken);
        }

        return new OrganizationLogoResult(organization.Id, true, header.ContentType);
    }
}
