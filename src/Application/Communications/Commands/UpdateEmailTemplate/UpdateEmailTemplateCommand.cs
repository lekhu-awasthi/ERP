using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Communications.Commands.CreateEmailTemplate;
using ErpApp.Domain.Configuration;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications.Commands.UpdateEmailTemplate;

/// <summary>
/// Updates an email template. <b>There is deliberately no Context parameter</b> — the reference
/// product renders its Template Type picker disabled on edit, and a template's body is written
/// against one context's merge fields, so a silent context move would turn a working template into
/// one that mails raw placeholders. See <see cref="EmailTemplate"/>.
/// </summary>
public sealed record UpdateEmailTemplateCommand(
    Guid OrganizationId,
    Guid Id,
    string Name,
    string Subject,
    string Body,
    string? ReplyTo,
    string? Cc,
    string? Bcc,
    bool IsActive)
    : IRequest<EmailTemplateDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.EmailTemplateManage;
}

public sealed class UpdateEmailTemplateCommandValidator : AbstractValidator<UpdateEmailTemplateCommand>
{
    public UpdateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Body).NotEmpty();
    }
}

public sealed class UpdateEmailTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<UpdateEmailTemplateCommand, EmailTemplateDto>
{
    public async Task<EmailTemplateDto> Handle(
        UpdateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        var template = await db.EmailTemplates.SingleOrDefaultAsync(
            x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Email template not found.");

        var nameTaken = await db.EmailTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Context == template.Context
                 && x.Name == request.Name
                 && x.Id != request.Id,
            cancellationToken);

        if (nameTaken)
        {
            throw new ConflictException(
                $"An email template named '{request.Name}' already exists for {template.Context}.");
        }

        // Deactivating the context's default would leave the Send Email dialog with templates but
        // no default to pre-select, which live is never the case. Naming the fix is friendlier than
        // silently promoting some other template the admin did not choose.
        if (template.IsDefault && !request.IsActive)
        {
            throw new ConflictException(
                "This is the default template for its context. Make another template the default before "
                    + "deactivating this one.");
        }

        template.Update(
            request.Name, request.Subject, request.Body, request.ReplyTo, request.Cc, request.Bcc, request.IsActive);

        await db.SaveChangesAsync(cancellationToken);

        return EmailTemplateMapping.ToDto(template);
    }
}
