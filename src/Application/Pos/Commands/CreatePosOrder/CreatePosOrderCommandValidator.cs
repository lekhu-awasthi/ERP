using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Pos;
using FluentValidation;

namespace ErpApp.Application.Pos.Commands.CreatePosOrder;

public sealed class CreatePosOrderCommandValidator : AbstractValidator<CreatePosOrderCommand>
{
    public CreatePosOrderCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.LocationId).NotEmpty();

        RuleFor(x => x.OrderType)
            .Must(x => x is PosTab.DineIn or PosTab.TakeAway or PosTab.Delivery)
            .WithMessage("An order is Dine In, Take Away or Delivery.");

        RuleFor(x => x.TableId)
            .NotNull().When(x => x.OrderType == PosTab.DineIn)
            .WithMessage("A Dine In order is seated at a table.");
        RuleFor(x => x.TableId)
            .Null().When(x => x.OrderType != PosTab.DineIn)
            .WithMessage("Only a Dine In order has a table.");

        RuleFor(x => x.Covers)
            .InclusiveBetween(1, PosOrder.MaxCovers).When(x => x.OrderType == PosTab.DineIn);
        RuleFor(x => x.Covers)
            .InclusiveBetween(0, PosOrder.MaxCovers).When(x => x.OrderType != PosTab.DineIn);

        RuleFor(x => x.ContactId)
            .NotNull().When(x => x.OrderType == PosTab.Delivery)
            .WithMessage("A Delivery order names the customer it goes to.");

        this.ValidateItems(x => x.Items, "An order starts with at least one item.");
    }
}
