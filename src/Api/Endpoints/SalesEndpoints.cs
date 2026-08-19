using ErpApp.Application.Sales;
using ErpApp.Application.Sales.Commands.ApproveCreditNote;
using ErpApp.Application.Sales.Commands.ApproveInvoice;
using ErpApp.Application.Sales.Commands.ApproveQuotation;
using ErpApp.Application.Sales.Commands.ApproveSalesOrder;
using ErpApp.Application.Sales.Commands.CreateCreditNote;
using ErpApp.Application.Sales.Commands.CreateInvoice;
using ErpApp.Application.Sales.Commands.CreateQuotation;
using ErpApp.Application.Sales.Commands.CreateSalesOrder;
using ErpApp.Application.Sales.Commands.UpdateCreditNote;
using ErpApp.Application.Sales.Commands.UpdateInvoice;
using ErpApp.Application.Sales.Commands.UpdateQuotation;
using ErpApp.Application.Sales.Commands.UpdateSalesOrder;
using ErpApp.Application.Sales.Commands.VoidCreditNote;
using ErpApp.Application.Sales.Commands.VoidInvoice;
using ErpApp.Application.Sales.Commands.VoidQuotation;
using ErpApp.Application.Sales.Commands.VoidSalesOrder;
using ErpApp.Application.Sales.Queries.GetCreditNote;
using ErpApp.Application.Sales.Queries.GetCreditNoteConversionTemplate;
using ErpApp.Application.Sales.Queries.GetInvoice;
using ErpApp.Application.Sales.Queries.GetInvoiceConversionTemplate;
using ErpApp.Application.Sales.Queries.GetQuotation;
using ErpApp.Application.Sales.Queries.GetSalesOrder;
using ErpApp.Application.Sales.Queries.ListCreditNotes;
using ErpApp.Application.Sales.Queries.ListInvoices;
using ErpApp.Application.Sales.Queries.ListQuotations;
using ErpApp.Application.Sales.Queries.ListSalesOrders;
using ErpApp.Application.Sales.Queries.PreviewInvoiceGlPosting;
using ErpApp.Application.Sales.Queries.AnnexFiveReport;
using ErpApp.Application.Sales.Queries.SalesMasterReport;
using ErpApp.Domain.Common;
using ErpApp.Domain.Sales;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class SalesEndpoints
{
    public static void MapSalesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Sales")
            .RequireAuthorization();

        MapQuotationEndpoints(group);
        MapInvoiceEndpoints(group);
        MapSalesOrderEndpoints(group);
        MapCreditNoteEndpoints(group);
        MapReportEndpoints(group);
    }

    private static void MapQuotationEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/quotations", async (
            Guid organizationId, QuotationStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListQuotationsQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/quotations/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetQuotationQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/quotations", async (
            Guid organizationId, QuotationRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateQuotationCommand(
                    organizationId, request.ContactId, request.Date, request.ExpiryDate, request.Reference, request.Lines,
                    request.DiscountPct),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/quotations/{result.Id}", result);
        });

        group.MapPut("/quotations/{id:guid}", async (
            Guid organizationId, Guid id, QuotationRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateQuotationCommand(
                    organizationId, id, request.ContactId, request.Date, request.ExpiryDate, request.Reference, request.Lines,
                    request.DiscountPct),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/quotations/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveQuotationCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/quotations/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidQuotationCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private static void MapInvoiceEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/invoices", async (
            Guid organizationId, InvoiceStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListInvoicesQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/invoices/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetInvoiceQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/invoices", async (
            Guid organizationId, InvoiceRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateInvoiceCommand(
                    organizationId, request.ContactId, request.WarehouseId, request.Date, request.Reference, request.Lines,
                    request.ReferrerType, request.ReferrerId, request.DiscountPct),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/invoices/{result.Id}", result);
        });

        group.MapPut("/invoices/{id:guid}", async (
            Guid organizationId, Guid id, InvoiceRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateInvoiceCommand(
                    organizationId, id, request.ContactId, request.WarehouseId, request.Date, request.Reference, request.Lines,
                    request.DiscountPct),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/invoices/{id:guid}/approve", async (
            Guid organizationId, Guid id, ApproveInvoiceRequest? request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ApproveInvoiceCommand(organizationId, id, request?.OverrideWarning ?? false), ct);
            return Results.Ok(result);
        });

        group.MapPost("/invoices/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidInvoiceCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/invoices/preview-gl-posting", async (
            Guid organizationId, PreviewInvoiceGlPostingRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new PreviewInvoiceGlPostingQuery(organizationId, request.Lines, request.DiscountPct), ct);
            return Results.Ok(result);
        });

        group.MapGet("/quotations/{quotationId:guid}/invoice-conversion-template", async (
            Guid organizationId, Guid quotationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetInvoiceConversionTemplateQuery(organizationId, quotationId), ct);
            return Results.Ok(result);
        });
    }

    private static void MapSalesOrderEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/sales-orders", async (
            Guid organizationId, SalesOrderStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListSalesOrdersQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/sales-orders/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetSalesOrderQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/sales-orders", async (
            Guid organizationId, SalesOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateSalesOrderCommand(
                    organizationId, request.ContactId, request.Date, request.DeliveryDate, request.Reference, request.Lines,
                    request.DiscountPct),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/sales-orders/{result.Id}", result);
        });

        group.MapPut("/sales-orders/{id:guid}", async (
            Guid organizationId, Guid id, SalesOrderRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateSalesOrderCommand(
                    organizationId, id, request.ContactId, request.Date, request.DeliveryDate, request.Reference, request.Lines,
                    request.DiscountPct),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/sales-orders/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveSalesOrderCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/sales-orders/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidSalesOrderCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private static void MapCreditNoteEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/credit-notes", async (
            Guid organizationId, CreditNoteStatus? status, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListCreditNotesQuery(organizationId, status), ct);
            return Results.Ok(result);
        });

        group.MapGet("/credit-notes/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCreditNoteQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/credit-notes", async (
            Guid organizationId, CreditNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCreditNoteCommand(
                    organizationId, request.ContactId, request.Date, request.Reference, request.Lines,
                    request.ReferrerType, request.ReferrerId, request.DiscountPct),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/credit-notes/{result.Id}", result);
        });

        group.MapPut("/credit-notes/{id:guid}", async (
            Guid organizationId, Guid id, CreditNoteRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCreditNoteCommand(
                    organizationId, id, request.ContactId, request.Date, request.Reference, request.Lines, request.DiscountPct),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/credit-notes/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveCreditNoteCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/credit-notes/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidCreditNoteCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapGet("/invoices/{invoiceId:guid}/credit-note-conversion-template", async (
            Guid organizationId, Guid invoiceId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCreditNoteConversionTemplateQuery(organizationId, invoiceId), ct);
            return Results.Ok(result);
        });
    }

    private static void MapReportEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/reports/sales-master-report", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactId, Guid? productId, Guid? warehouseId,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SalesMasterReportQuery(organizationId, fromDate, toDate, contactId, productId, warehouseId), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/annex-five", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new AnnexFiveReportQuery(organizationId, fromDate, toDate), ct);
            return Results.Ok(result);
        });
    }

    private sealed record QuotationRequest(
        Guid ContactId, DateOnly Date, DateOnly? ExpiryDate, string? Reference, IReadOnlyList<QuotationLineInput> Lines,
        decimal DiscountPct = 0);

    private sealed record InvoiceRequest(
        Guid ContactId, Guid WarehouseId, DateOnly Date, string? Reference, IReadOnlyList<InvoiceLineInput> Lines,
        DocumentType? ReferrerType = null, Guid? ReferrerId = null, decimal DiscountPct = 0);

    private sealed record ApproveInvoiceRequest(bool OverrideWarning = false);

    private sealed record PreviewInvoiceGlPostingRequest(IReadOnlyList<InvoiceLineInput> Lines, decimal DiscountPct = 0);

    private sealed record SalesOrderRequest(
        Guid ContactId, DateOnly Date, DateOnly? DeliveryDate, string? Reference, IReadOnlyList<SalesOrderLineInput> Lines,
        decimal DiscountPct = 0);

    private sealed record CreditNoteRequest(
        Guid ContactId, DateOnly Date, string? Reference, IReadOnlyList<CreditNoteLineInput> Lines,
        DocumentType? ReferrerType = null, Guid? ReferrerId = null, decimal DiscountPct = 0);
}
