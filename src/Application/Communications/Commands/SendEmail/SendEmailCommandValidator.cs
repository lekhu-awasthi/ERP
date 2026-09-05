using FluentValidation;

namespace ErpApp.Application.Communications.Commands.SendEmail;

public sealed class SendEmailCommandValidator : AbstractValidator<SendEmailCommand>
{
    /// <summary>Matches the live dialog's own cap on a message body; generous enough that a real
    /// email cannot hit it, small enough that a runaway paste cannot fill a column.</summary>
    private const int MaxBodyLength = 100_000;

    private const int MaxSubjectLength = 500;

    /// <summary>Total recipients across To, CC and BCC. A Send Email dialog is a per-document
    /// action, not a mailing list -- <c>SendSmsCommand</c> is where bulk lives, and it has its own
    /// Admin-only key precisely because it is bulk. A cap here keeps this action from quietly
    /// becoming that one.</summary>
    private const int MaxRecipients = 50;

    public SendEmailCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ParentId).NotEmpty();

        RuleFor(x => x.To)
            .NotEmpty().WithMessage("At least one recipient is required.");

        RuleFor(x => x.Subject)
            .NotEmpty()
            .MaximumLength(MaxSubjectLength);

        RuleFor(x => x.Body)
            .NotEmpty()
            .MaximumLength(MaxBodyLength);

        RuleFor(x => x)
            .Must(x => x.To.Count + x.Cc.Count + x.Bcc.Count <= MaxRecipients)
            .WithMessage($"An email may address at most {MaxRecipients} recipients in total.")
            .WithName(nameof(SendEmailCommand.To));

        // Rules over each address list are written out rather than shared through a helper taking a
        // captured Func: FluentValidation cannot infer a property name from one, and the result is a
        // 500 on every endpoint the rule guards that no handler test can see (phase 25).
        RuleForEach(x => x.To).Must(BeAnEmailAddress).WithMessage("'{PropertyValue}' is not a valid email address.");
        RuleForEach(x => x.Cc).Must(BeAnEmailAddress).WithMessage("'{PropertyValue}' is not a valid email address.");
        RuleForEach(x => x.Bcc).Must(BeAnEmailAddress).WithMessage("'{PropertyValue}' is not a valid email address.");

        RuleFor(x => x.ReplyTo)
            .Must(BeAnEmailAddress!)
            .When(x => !string.IsNullOrWhiteSpace(x.ReplyTo))
            .WithMessage("'{PropertyValue}' is not a valid email address.");
    }

    /// <summary>
    /// Deliberately permissive. The authority on whether an address is deliverable is the receiving
    /// mail server, not a regex, and a validator strict enough to be interesting rejects addresses
    /// that are legal — so this checks only the shape that would otherwise throw inside MailKit's
    /// parser and fail the whole send with a stack trace instead of a field error.
    /// </summary>
    private static bool BeAnEmailAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);

        return at > 0
            && at == value.LastIndexOf('@')
            && at < value.Length - 1
            && !value.Any(char.IsWhiteSpace);
    }
}
