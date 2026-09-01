using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.DocumentExtraction;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Tenancy.Commands.UpdateAiDocumentExtractionSetting;

/// <summary>
/// The tenant's consent switch for AI-assisted extraction (FR-10.3). Its own command rather than a
/// field on a general settings save, so a routine edit of the accounting defaults can never
/// re-enable data egress as a side effect -- see <c>TenantSettings.SetAiDocumentExtractionEnabled</c>.
///
/// <para>Default is <b>off</b>, seeded that way at Organization creation. This is the first feature
/// in the product that sends tenant business documents to a third party, and an opt-out default
/// would mean the egress began the day the migration ran, with nobody having agreed to it.</para>
/// </summary>
public sealed record UpdateAiDocumentExtractionSettingCommand(Guid OrganizationId, bool Enabled)
    : IRequest<AiDocumentExtractionSettingDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.AiDocumentExtractionManage;
}

public sealed class UpdateAiDocumentExtractionSettingCommandValidator
    : AbstractValidator<UpdateAiDocumentExtractionSettingCommand>
{
    public UpdateAiDocumentExtractionSettingCommandValidator() => RuleFor(x => x.OrganizationId).NotEmpty();
}

/// <param name="ExtractorConfigured">
/// Whether this deployment has an extraction credential at all. Surfaced beside the tenant's own
/// switch so an Admin who turns it on and gets nothing is told which of the two is missing, rather
/// than being left to guess.
/// </param>
public sealed record AiDocumentExtractionSettingDto(bool Enabled, bool ExtractorConfigured, string? ModelId);

public sealed class UpdateAiDocumentExtractionSettingCommandHandler(
    IAppDbContext db, IDocumentExtractor extractor)
    : IRequestHandler<UpdateAiDocumentExtractionSettingCommand, AiDocumentExtractionSettingDto>
{
    public async Task<AiDocumentExtractionSettingDto> Handle(
        UpdateAiDocumentExtractionSettingCommand request, CancellationToken cancellationToken)
    {
        var settings = await db.TenantSettings.SingleOrDefaultAsync(
            x => x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Organization settings not found.");

        settings.SetAiDocumentExtractionEnabled(request.Enabled);
        await db.SaveChangesAsync(cancellationToken);

        return new AiDocumentExtractionSettingDto(
            settings.AiDocumentExtractionEnabled, extractor.IsConfigured, extractor.ModelId);
    }
}
