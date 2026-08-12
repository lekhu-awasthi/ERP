using ErpApp.Application.Purchasing;
using ErpApp.Application.Purchasing.Commands.ApproveDebitNote;
using ErpApp.Application.Purchasing.Commands.ApproveExpense;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseBill;
using ErpApp.Application.Purchasing.Commands.ApprovePurchaseOrder;
using ErpApp.Application.Purchasing.Commands.CreateDebitNote;
using ErpApp.Application.Purchasing.Commands.CreateExpense;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseBill;
using ErpApp.Application.Purchasing.Commands.CreatePurchaseOrder;
using ErpApp.Application.Purchasing.Commands.UpdateDebitNote;
using ErpApp.Application.Purchasing.Commands.UpdateExpense;
using ErpApp.Application.Purchasing.Commands.UpdatePurchaseBill;
using ErpApp.Application.Purchasing.Commands.UpdatePurchaseOrder;
using ErpApp.Application.Purchasing.Queries.GetDebitNote;
using ErpApp.Application.Purchasing.Queries.GetDebitNoteConversionTemplate;
using ErpApp.Application.Purchasing.Queries.GetExpense;
using ErpApp.Application.Purchasing.Queries.GetPurchaseBill;
using ErpApp.Application.Purchasing.Queries.GetPurchaseBillConversionTemplate;
using ErpApp.Application.Purchasing.Queries.GetPurchaseOrder;
using ErpApp.Application.Purchasing.Queries.ListDebitNotes;
using ErpApp.Application.Purchasing.Queries.ListExpenses;
using ErpApp.Application.Purchasing.Queries.ListPurchaseBills;
using ErpApp.Application.Purchasing.Queries.ListPurchaseOrders;
using ErpApp.Application.Purchasing.Queries.PreviewExpenseGlPosting;
using ErpApp.Application.Purchasing.Queries.PreviewPurchaseBillGlPosting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class PurchasingEndpoints
{
    public static void MapPurchasingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Purchasing")
            .RequireAuthorization();

        MapPurchaseOrderEndpoints(group);
        MapPurchaseBillEndpoints(group);
        MapExpenseEndpoints(group);
        MapDebitNoteEndpoints(group);
    }

    private static void MapPurchaseOrderEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/purchase-orders", async (
            Guid organizationId, PurchaseOrderStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListPurchaseOrdersQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/purchase-orders/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPurchaseOrderQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/purchase-orders", async (
            Guid organizationId, PurchaseOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreatePurchaseOrderCommand(organizationId, request.ContactId, request.Date, request.Reference, request.Lines), ct);
            return Results.Created($"/api/organizations/{organizationId}/purchase-orders/{result.Id}", result);
        });

        group.MapPut("/purchase-orders/{id:guid}", async (
            Guid organizationId, Guid id, PurchaseOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdatePurchaseOrderCommand(organizationId, id, request.ContactId, request.Date, request.Reference, request.Lines), ct);
            return Results.Ok(result);
        });

        group.MapPost("/purchase-orders/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApprovePurchaseOrderCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private static void MapPurchaseBillEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/purchase-bills", async (
            Guid organizationId, PurchaseBillStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListPurchaseBillsQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/purchase-bills/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPurchaseBillQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/purchase-bills", async (
            Guid organizationId, PurchaseBillRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreatePurchaseBillCommand(
                    organizationId,
                    request.ContactId,
                    request.WarehouseId,
                    request.Date,
                    request.Reference,
                    request.SupplierInvoiceReference,
                    request.IsImport,
                    request.ImportCountry,
                    request.ImportDate,
                    request.ImportDocumentNo,
                    request.TdsTypeId,
                    request.Lines,
                    request.ReferrerType,
                    request.ReferrerId),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/purchase-bills/{result.Id}", result);
        });

        group.MapPut("/purchase-bills/{id:guid}", async (
            Guid organizationId, Guid id, PurchaseBillRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdatePurchaseBillCommand(
                    organizationId,
                    id,
                    request.ContactId,
                    request.WarehouseId,
                    request.Date,
                    request.Reference,
                    request.SupplierInvoiceReference,
                    request.IsImport,
                    request.ImportCountry,
                    request.ImportDate,
                    request.ImportDocumentNo,
                    request.TdsTypeId,
                    request.Lines),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/purchase-bills/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApprovePurchaseBillCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/purchase-bills/preview-gl-posting", async (
            Guid organizationId, PreviewPurchaseBillGlPostingRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PreviewPurchaseBillGlPostingQuery(organizationId, request.Lines, request.TdsTypeId), ct);
            return Results.Ok(result);
        });

        group.MapGet("/purchase-orders/{purchaseOrderId:guid}/purchase-bill-conversion-template", async (
            Guid organizationId, Guid purchaseOrderId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetPurchaseBillConversionTemplateQuery(organizationId, purchaseOrderId), ct);
            return Results.Ok(result);
        });
    }

    private static void MapExpenseEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/expenses", async (
            Guid organizationId, ExpenseStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListExpensesQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/expenses/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetExpenseQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/expenses", async (
            Guid organizationId, ExpenseRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateExpenseCommand(
                    organizationId, request.ContactId, request.Date, request.DueDate, request.SupplierInvoiceReference,
                    request.Notes, request.TdsApplicable, request.TdsTypeId, request.Lines),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/expenses/{result.Id}", result);
        });

        group.MapPut("/expenses/{id:guid}", async (
            Guid organizationId, Guid id, ExpenseRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateExpenseCommand(
                    organizationId, id, request.ContactId, request.Date, request.DueDate, request.SupplierInvoiceReference,
                    request.Notes, request.TdsApplicable, request.TdsTypeId, request.Lines),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/expenses/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveExpenseCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/expenses/preview-gl-posting", async (
            Guid organizationId, PreviewExpenseGlPostingRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PreviewExpenseGlPostingQuery(organizationId, request.Lines, request.TdsApplicable, request.TdsTypeId), ct);
            return Results.Ok(result);
        });
    }

    private static void MapDebitNoteEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/debit-notes", async (
            Guid organizationId, DebitNoteStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListDebitNotesQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/debit-notes/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetDebitNoteQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/debit-notes", async (
            Guid organizationId, DebitNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateDebitNoteCommand(
                    organizationId, request.ContactId, request.Date, request.Reference, request.Lines,
                    request.ReferrerType, request.ReferrerId),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/debit-notes/{result.Id}", result);
        });

        group.MapPut("/debit-notes/{id:guid}", async (
            Guid organizationId, Guid id, DebitNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateDebitNoteCommand(organizationId, id, request.ContactId, request.Date, request.Reference, request.Lines),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/debit-notes/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveDebitNoteCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapGet("/purchase-bills/{purchaseBillId:guid}/debit-note-conversion-template", async (
            Guid organizationId, Guid purchaseBillId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetDebitNoteConversionTemplateQuery(organizationId, purchaseBillId), ct);
            return Results.Ok(result);
        });
    }

    private sealed record PurchaseOrderRequest(
        Guid ContactId, DateOnly Date, string? Reference, IReadOnlyList<PurchaseOrderLineInput> Lines);

    private sealed record PurchaseBillRequest(
        Guid ContactId,
        Guid WarehouseId,
        DateOnly Date,
        string? Reference,
        string? SupplierInvoiceReference,
        bool IsImport,
        string? ImportCountry,
        DateOnly? ImportDate,
        string? ImportDocumentNo,
        Guid? TdsTypeId,
        IReadOnlyList<PurchaseBillLineInput> Lines,
        DocumentType? ReferrerType = null,
        Guid? ReferrerId = null);

    private sealed record PreviewPurchaseBillGlPostingRequest(IReadOnlyList<PurchaseBillLineInput> Lines, Guid? TdsTypeId);

    private sealed record ExpenseRequest(
        Guid ContactId,
        DateOnly Date,
        DateOnly? DueDate,
        string? SupplierInvoiceReference,
        string? Notes,
        bool TdsApplicable,
        Guid? TdsTypeId,
        IReadOnlyList<ExpenseLineInput> Lines);

    private sealed record PreviewExpenseGlPostingRequest(IReadOnlyList<ExpenseLineInput> Lines, bool TdsApplicable, Guid? TdsTypeId);

    private sealed record DebitNoteRequest(
        Guid ContactId, DateOnly Date, string? Reference, IReadOnlyList<DebitNoteLineInput> Lines,
        DocumentType? ReferrerType = null, Guid? ReferrerId = null);
}
