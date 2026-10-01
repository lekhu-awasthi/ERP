using ErpApp.Domain.Pos;
using FluentValidation;

namespace ErpApp.Application.Pos.Commands.ClosePosSession;

public sealed class ClosePosSessionCommandValidator : AbstractValidator<ClosePosSessionCommand>
{
    public ClosePosSessionCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.CountedAmount).PosCashAmount();
        RuleFor(x => x.Denominations).PosDenominations();
        RuleFor(x => x)
            .Must(x => x.CountedAmount is not null || x.Denominations is not null)
            .WithName(nameof(ClosePosSessionCommand.CountedAmount))
            .WithMessage("Count the drawer: give the counted amount, or the count note by note.");
        RuleFor(x => x.Note).MaximumLength(PosSession.MaxNoteLength);
    }
}
