using ErpApp.Application.Common.Security;
using ErpApp.Application.Pos.Commands.AddPosOrderItems;
using ErpApp.Application.Pos.Commands.CreateKitchenStation;
using ErpApp.Application.Pos.Commands.CreatePosArea;
using ErpApp.Application.Pos.Commands.CreatePosOrder;
using ErpApp.Application.Pos.Commands.SavePosAreaLayout;
using ErpApp.Application.Pos.Commands.ServePosOrderItems;
using ErpApp.Application.Pos.Commands.SetKitchenStationProducts;
using ErpApp.Application.Pos.Commands.VoidPosOrder;
using ErpApp.Application.Pos.Commands.VoidPosOrderItems;
using ErpApp.Application.Pos.Queries.GetPosFloorPlan;
using ErpApp.Application.Pos.Restaurant;
using ErpApp.Application.UnitTests.TestSupport;
using ErpApp.Domain.Pos;

namespace ErpApp.Application.UnitTests.Pos;

/// <summary>
/// Phase 64 -- phase 61's till run as a Restaurant location, with a floor (Ground Floor: T1, T2;
/// Rooftop: R1) and a Kitchen station that Chicken Momo is sent to. Coke has no station, so it goes to
/// Default, as the vendor's did.
/// </summary>
internal sealed class PosTestRestaurant
{
    public required PosTestTill Till { get; init; }
    public required Guid GroundFloorId { get; init; }
    public required Guid RooftopId { get; init; }
    public required Guid T1 { get; init; }
    public required Guid T2 { get; init; }
    public required Guid R1 { get; init; }
    public required Guid KitchenId { get; init; }

    public Guid OrganizationId => Till.OrganizationId;
    public Guid LocationId => Till.Location.Id;

    public static readonly string[] WaiterKeys =
    [
        .. PosTestTill.CashierKeys,
        PermissionKeys.InvoiceView,
        PermissionKeys.PosOrderOperate, PermissionKeys.PosOrderVoid, PermissionKeys.PosOrderView,
        PermissionKeys.PosFloorPlanManage, PermissionKeys.PosSettingsManage,
    ];

    public static async Task<PosTestRestaurant> CreateAsync(string[]? grantedKeys = null)
    {
        var till = await PosTestTill.CreateAsync(grantedKeys ?? WaiterKeys, restaurant: true);
        var db = till.Db;
        var orgId = till.OrganizationId;

        var floor = await new CreatePosAreaCommandHandler(db).Handle(
            new CreatePosAreaCommand(orgId, till.Location.Id, "Ground Floor"), CancellationToken.None);
        floor = await new CreatePosAreaCommandHandler(db).Handle(
            new CreatePosAreaCommand(orgId, till.Location.Id, "Rooftop"), CancellationToken.None);

        var ground = floor.Areas.Single(x => x.Name == "Ground Floor").Id;
        var rooftop = floor.Areas.Single(x => x.Name == "Rooftop").Id;

        await new SavePosAreaLayoutCommandHandler(db).Handle(
            new SavePosAreaLayoutCommand(orgId, ground,
            [
                new PosTableLayoutInput(null, "T1", 4, PosTableShape.Rectangle, 100, 100, 250, 100, true),
                new PosTableLayoutInput(null, "T2", 2, PosTableShape.Circle, 450, 100, 100, 100, true),
            ]),
            CancellationToken.None);
        floor = await new SavePosAreaLayoutCommandHandler(db).Handle(
            new SavePosAreaLayoutCommand(orgId, rooftop,
                [new PosTableLayoutInput(null, "R1", 6, PosTableShape.Rectangle, 100, 100, 250, 100, true)]),
            CancellationToken.None);

        var stations = await new CreateKitchenStationCommandHandler(db).Handle(
            new CreateKitchenStationCommand(orgId, "Kitchen"), CancellationToken.None);
        var kitchen = stations.Single().Id;
        await new SetKitchenStationProductsCommandHandler(db).Handle(
            new SetKitchenStationProductsCommand(orgId, kitchen, [till.MomoId]), CancellationToken.None);

        Guid Table(string name) => floor.Areas.SelectMany(a => a.Tables).Single(t => t.Name == name).Id;

        return new PosTestRestaurant
        {
            Till = till,
            GroundFloorId = ground,
            RooftopId = rooftop,
            T1 = Table("T1"),
            T2 = Table("T2"),
            R1 = Table("R1"),
            KitchenId = kitchen,
        };
    }

    public FakeCurrentUserService CurrentUser(Guid? userId = null) => Till.CurrentUser(userId);

    public PosOrderItemInput Momo(decimal quantity, string? note = null) => new(Till.MomoId, quantity, null, note);

    public PosOrderItemInput Coke(decimal quantity) => new(Till.CokeId, quantity, null, null);

    public Task<PosOrderDto> SeatAsync(
        Guid tableId, IReadOnlyList<PosOrderItemInput> items, int covers = 2, Guid? userId = null) =>
        new CreatePosOrderCommandHandler(Till.Db, Till.Seed.NumberGenerator, CurrentUser(userId)).Handle(
            new CreatePosOrderCommand(OrganizationId, LocationId, PosTab.DineIn, tableId, covers, null, items),
            CancellationToken.None);

    public Task<PosOrderDto> OpenAsync(
        PosTab orderType, IReadOnlyList<PosOrderItemInput> items, Guid? contactId = null, Guid? userId = null) =>
        new CreatePosOrderCommandHandler(Till.Db, Till.Seed.NumberGenerator, CurrentUser(userId)).Handle(
            new CreatePosOrderCommand(OrganizationId, LocationId, orderType, null, 0, contactId, items),
            CancellationToken.None);

    public Task<PosOrderDto> AddAsync(
        Guid orderId,
        IReadOnlyList<PosOrderItemInput>? newItems = null,
        IReadOnlyList<PosOrderLineQuantityInput>? moreOf = null,
        Guid? userId = null) =>
        new AddPosOrderItemsCommandHandler(Till.Db, CurrentUser(userId)).Handle(
            new AddPosOrderItemsCommand(OrganizationId, orderId, newItems ?? [], moreOf ?? []), CancellationToken.None);

    public Task<PosOrderDto> ServeAsync(Guid orderId, params PosOrderLineQuantityInput[] items) =>
        new ServePosOrderItemsCommandHandler(Till.Db, CurrentUser()).Handle(
            new ServePosOrderItemsCommand(OrganizationId, orderId, items), CancellationToken.None);

    public Task<PosOrderDto> DiscardAsync(Guid orderId, string reason, params PosOrderLineQuantityInput[] items) =>
        new VoidPosOrderItemsCommandHandler(Till.Db, CurrentUser()).Handle(
            new VoidPosOrderItemsCommand(OrganizationId, orderId, items, reason), CancellationToken.None);

    public Task<PosOrderDto> VoidAsync(Guid orderId, string reason = "Guests left") =>
        new VoidPosOrderCommandHandler(Till.Db, CurrentUser()).Handle(
            new VoidPosOrderCommand(OrganizationId, orderId, reason), CancellationToken.None);

    public Task<PosFloorPlanDto> FloorAsync() =>
        new GetPosFloorPlanQueryHandler(Till.Db).Handle(
            new GetPosFloorPlanQuery(OrganizationId, LocationId), CancellationToken.None);

    public static PosOrderLineQuantityInput Line(PosOrderDto order, string productName, decimal quantity) =>
        new(order.Lines.Single(x => x.ProductName == productName).Id, quantity);
}
