using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Configuration;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Communications.Commands.CreateEmailTemplate;

/// <summary>Creates an email template. <see cref="Context"/> is set here and never again — see
/// <see cref="EmailTemplate"/> for why that is a live invariant with real force.</summary>
public sealed record CreateEmailTemplateCommand(
    Guid OrganizationId,
    string Name,
    EmailTemplateContext Context,
    string Subject,
    string Body,
    string? ReplyTo,
    string? Cc,
    string? Bcc)
    : IRequest<EmailTemplateDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => PermissionKeys.EmailTemplateManage;
}

public sealed record EmailTemplateDto(
    Guid Id,
    string Name,
    EmailTemplateContext Context,
    string ContextName,
    string Subject,
    string Body,
    string? ReplyTo,
    string? Cc,
    string? Bcc,
    bool IsDefault,
    bool IsActive);

public sealed class CreateEmailTemplateCommandValidator : AbstractValidator<CreateEmailTemplateCommand>
{
    public CreateEmailTemplateCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Body).NotEmpty();
        RuleFor(x => x.Context).IsInEnum();
    }
}

public sealed class CreateEmailTemplateCommandHandler(IAppDbContext db)
    : IRequestHandler<CreateEmailTemplateCommand, EmailTemplateDto>
{
    public async Task<EmailTemplateDto> Handle(
        CreateEmailTemplateCommand request, CancellationToken cancellationToken)
    {
        var nameExists = await db.EmailTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId
                 && x.Context == request.Context
                 && x.Name == request.Name,
            cancellationToken);

        if (nameExists)
        {
            throw new ConflictException(
                $"An email template named '{request.Name}' already exists for {request.Context}.");
        }

        // The first template for a context becomes its default, so a context is never left with
        // templates but no default -- the same rule CreateCustomTemplateCommandHandler applies.
        var isFirstForContext = !await db.EmailTemplates.AnyAsync(
            x => x.OrganizationId == request.OrganizationId && x.Context == request.Context, cancellationToken);

        var template = EmailTemplate.Create(
            request.OrganizationId, request.Name, request.Context, request.Subject, request.Body,
            request.ReplyTo, request.Cc, request.Bcc, isFirstForContext);

        db.EmailTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        return EmailTemplateMapping.ToDto(template);
    }
}

/// <summary>One mapping, so the four endpoints that return a template cannot drift.</summary>
public static class EmailTemplateMapping
{
    public static EmailTemplateDto ToDto(EmailTemplate template) => new(
        template.Id,
        template.Name,
        template.Context,
        EmailMergeFields.GroupNameFor(template.Context),
        template.Subject,
        template.Body,
        template.ReplyTo,
        template.Cc,
        template.Bcc,
        template.IsDefault,
        template.IsActive);
}
