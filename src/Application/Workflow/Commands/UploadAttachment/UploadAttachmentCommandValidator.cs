using ErpApp.Application.Common.Storage;
using FluentValidation;

namespace ErpApp.Application.Workflow.Commands.UploadAttachment;

public sealed class UploadAttachmentCommandValidator : AbstractValidator<UploadAttachmentCommand>
{
    public UploadAttachmentCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.ParentType).IsInEnum();
        RuleFor(x => x.ParentId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(150);
        RuleFor(x => x.FileSizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(AttachmentValidation.MaxSizeBytes)
            .WithMessage($"File exceeds the {AttachmentValidation.MaxSizeBytes / (1024 * 1024)} MB size limit.");
        RuleFor(x => x.FileName)
            .Must(AttachmentValidation.IsAllowedExtension)
            .WithMessage("File type is not allowed.");
    }
}
