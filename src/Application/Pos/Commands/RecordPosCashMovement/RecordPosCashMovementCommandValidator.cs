using ErpApp.Domain.Pos;
using FluentValidation;

namespace ErpApp.Application.Pos.Commands.RecordPosCashMovement;

public sealed class RecordPosCashMovementCommandValidator : AbstractValidator<RecordPosCashMovementCommand>
{
    public RecordPosCashMovementCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.Direction).IsInEnum();
        RuleFor(x => x.Amount).PosPositiveCashAmount();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(PosSession.MaxNoteLength);
    }
}
