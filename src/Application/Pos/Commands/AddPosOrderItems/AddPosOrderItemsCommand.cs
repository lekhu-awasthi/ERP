using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.AddPosOrderItems;

/// <summary>
/// Phase 64 -- sends more to the kitchen on an open order: new items, more of lines already on it, or
/// both. Each station gets a ticket carrying only the change (the vendor's delta KOT). The vendor posts
/// the whole item list with <c>original_quantity</c> and diffs it on the server; this takes the
/// additions themselves, so two waiters adding to one table cannot overwrite each other's lines.
///
/// <para>Not audited by <c>AuditBehavior</c>: the tickets it writes are the record (who sent what, to
/// which station, when), the way a session's cash movements are (phase 61).</para>
/// </summary>
public sealed record AddPosOrderItemsCommand(
    Guid OrganizationId,
    Guid OrderId,
    IReadOnlyList<PosOrderItemInput> NewItems,
    IReadOnlyList<PosOrderLineQuantityInput> MoreOf)
    : IRequest<PosOrderDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class AddPosOrderItemsCommandValidator : AbstractValidator<AddPosOrderItemsCommand>
{
    public AddPosOrderItemsCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        this.ValidateItems(x => x.NewItems);
        this.ValidateLineQuantities(x => x.MoreOf);
        RuleFor(x => x)
            .Must(x => (x.NewItems?.Count ?? 0) + (x.MoreOf?.Count ?? 0) > 0)
            .WithName(nameof(AddPosOrderItemsCommand.NewItems))
            .WithMessage("Nothing to send: add an item first.");
    }
}
