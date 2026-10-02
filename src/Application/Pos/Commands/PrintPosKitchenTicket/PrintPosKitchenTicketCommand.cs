using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Domain.Tenancy;
using FluentValidation;
using MediatR;

namespace ErpApp.Application.Pos.Commands.PrintPosKitchenTicket;

/// <summary>A ticket to print: the order it belongs to (header and tickets) and this print's number.</summary>
public sealed record PosKitchenTicketPrintDto(PosOrderDto Order, Guid TicketId, int PrintNumber);

/// <summary>
/// Phase 64 -- records one printing of a kitchen ticket and returns it. Number 1 is the original; any
/// later print is marked <i>REPRINT</i> on the paper, so a kitchen handed a second copy does not cook the
/// order twice. Not statutory, unlike a bill's copy marking (phase 62 Decision B), but the same reason
/// it is the server's count: a browser cannot know another till printed the ticket already.
/// </summary>
public sealed record PrintPosKitchenTicketCommand(Guid OrganizationId, Guid OrderId, Guid TicketId)
    : IRequest<PosKitchenTicketPrintDto>, IRequirePermission, IOrganizationScoped, IRequireFeature
{
    public string PermissionKey => PermissionKeys.PosOrderOperate;

    public IReadOnlyCollection<TenantFeature> RequiredFeatures => [TenantFeature.PosRestaurant];
}

public sealed class PrintPosKitchenTicketCommandValidator : AbstractValidator<PrintPosKitchenTicketCommand>
{
    public PrintPosKitchenTicketCommandValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.OrderId).NotEmpty();
        RuleFor(x => x.TicketId).NotEmpty();
    }
}

public sealed class PrintPosKitchenTicketCommandHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<PrintPosKitchenTicketCommand, PosKitchenTicketPrintDto>
{
    public async Task<PosKitchenTicketPrintDto> Handle(
        PrintPosKitchenTicketCommand request, CancellationToken cancellationToken)
    {
        // A ticket of a settled or voided order still prints (a kitchen asks for the paper again);
        // only the location rules and the branch boundary apply, not the order's state.
        var order = await PosRestaurant.LoadOrderAsync(db, request.OrganizationId, request.OrderId, cancellationToken);
        await PosRestaurant.LoadAsync(db, request.OrganizationId, order.BillingLocationId, cancellationToken);
        await PosRestaurant.EnsureMayOrderAtAsync(
            db, request.OrganizationId, currentUser.UserId, order.BillingLocationId, cancellationToken);

        if (order.Tickets.All(x => x.Id != request.TicketId))
        {
            throw new NotFoundException("Kitchen ticket not found.");
        }

        var printNumber = order.RecordTicketPrint(request.TicketId);
        await db.SaveChangesAsync(cancellationToken);

        return new PosKitchenTicketPrintDto(
            await PosOrderView.ReadAsync(db, order, cancellationToken), request.TicketId, printNumber);
    }
}
