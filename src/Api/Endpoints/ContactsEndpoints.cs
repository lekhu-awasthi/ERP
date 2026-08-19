using ErpApp.Api.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Commands.CreateContactGroup;
using ErpApp.Application.Contacts.Commands.DeactivateContact;
using ErpApp.Application.Contacts.Commands.UpdateContact;
using ErpApp.Application.Contacts.Commands.UpdateContactGroup;
using ErpApp.Application.Contacts.Queries.ContactAgeingSummary;
using ErpApp.Application.Contacts.Queries.ContactOverview;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Contacts.Queries.GetContact;
using ErpApp.Application.Contacts.Queries.ListContacts;
using ErpApp.Domain.Contacts;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class ContactsEndpoints
{
    public static void MapContactsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Contacts")
            .RequireAuthorization();

        MapContactGroupEndpoints(group);
        MapContactEndpoints(group);
        MapReportEndpoints(group);
    }

    private static void MapContactGroupEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/contact-groups", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<ContactGroup>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/contact-groups", async (
            Guid organizationId, CreateContactGroupRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateContactGroupCommand(organizationId, request.Name, request.ParentGroupId), ct);
            return Results.Created($"/api/organizations/{organizationId}/contact-groups/{result.Id}", result);
        });

        group.MapPut("/contact-groups/{id:guid}", async (
            Guid organizationId, Guid id, UpdateContactGroupRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateContactGroupCommand(organizationId, id, request.Name, request.ParentGroupId, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/contact-groups/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<ContactGroup>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapContactEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/contacts", async (
            Guid organizationId, ContactType? type, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListContactsQuery(organizationId, type, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/contacts/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetContactQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/contacts", async (
            Guid organizationId, CreateContactRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateContactCommand(
                    organizationId, request.Type, request.Name, request.Address, request.Pan, request.Phone,
                    request.Email, request.GroupId, request.OpeningBalance),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/contacts/{result.Id}", result);
        });

        group.MapPut("/contacts/{id:guid}", async (
            Guid organizationId, Guid id, UpdateContactRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateContactCommand(
                    organizationId, id, request.Name, request.Address, request.Pan, request.Phone, request.Email,
                    request.GroupId, request.OpeningBalance),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/contacts/{id:guid}/deactivate", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeactivateContactCommand(organizationId, id), ct);
            return Results.NoContent();
        });

        // Phase 10 -- gated on Contacts.Contact.View (the same permission this page's plain Overview
        // form already requires), not a new Reports.*.View key -- see ContactOverviewQuery's own doc
        // comment for why this diverges from Phase 9's Admin-only Statement/Ageing precedent.
        group.MapGet("/contacts/{id:guid}/overview", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ContactOverviewQuery(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    /// <summary>
    /// Four routes, each hardcoding ContactType server-side (Phase 9 -- Ageing/Statement Reports)
    /// rather than accepting it as a client-supplied parameter -- keeps a bad/Lead ContactType value
    /// impossible without a FluentValidation validator, the same "hardcode the discriminator at the
    /// route" choice CreatePaymentCommand already made for Direction (phase-5/6-status.md). One
    /// shared handler answers both Customer/Supplier variants of each report -- see
    /// ContactAgeingSummaryQuery/ContactStatementQuery's own doc comments.
    /// </summary>
    private static void MapReportEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/reports/customer-ageing-summary", async (
            Guid organizationId, DateOnly asOfDate, Guid? contactGroupId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactAgeingSummaryQuery(
                    organizationId, ContactType.Customer, asOfDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/customer-ageing-summary/export", async (
            Guid organizationId, DateOnly asOfDate, Guid? contactGroupId, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactAgeingSummaryQuery(
                    organizationId, ContactType.Customer, asOfDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportContactAgeingSummary(result, "Customer", asOfDate);
        });

        group.MapGet("/reports/supplier-ageing-summary", async (
            Guid organizationId, DateOnly asOfDate, Guid? contactGroupId, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactAgeingSummaryQuery(
                    organizationId, ContactType.Supplier, asOfDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/supplier-ageing-summary/export", async (
            Guid organizationId, DateOnly asOfDate, Guid? contactGroupId, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactAgeingSummaryQuery(
                    organizationId, ContactType.Supplier, asOfDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportContactAgeingSummary(result, "Supplier", asOfDate);
        });

        group.MapGet("/reports/customer-statement", async (
            Guid organizationId, Guid contactId, DateOnly fromDate, DateOnly toDate, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactStatementQuery(
                    organizationId, ContactType.Customer, contactId, fromDate, toDate,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/customer-statement/export", async (
            Guid organizationId, Guid contactId, DateOnly fromDate, DateOnly toDate, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactStatementQuery(
                    organizationId, ContactType.Customer, contactId, fromDate, toDate,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportContactStatement(result, "Customer");
        });

        group.MapGet("/reports/supplier-statement", async (
            Guid organizationId, Guid contactId, DateOnly fromDate, DateOnly toDate, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactStatementQuery(
                    organizationId, ContactType.Supplier, contactId, fromDate, toDate,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/supplier-statement/export", async (
            Guid organizationId, Guid contactId, DateOnly fromDate, DateOnly toDate, bool full, int? page, int? pageSize,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactStatementQuery(
                    organizationId, ContactType.Supplier, contactId, fromDate, toDate,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportContactStatement(result, "Supplier");
        });
    }

    private sealed record CreateContactGroupRequest(string Name, Guid? ParentGroupId);

    private sealed record UpdateContactGroupRequest(string Name, Guid? ParentGroupId, bool IsActive);

    private sealed record CreateContactRequest(
        ContactType Type, string Name, string? Address, string? Pan, string? Phone, string? Email,
        Guid? GroupId, decimal OpeningBalance);

    private sealed record UpdateContactRequest(
        string Name, string? Address, string? Pan, string? Phone, string? Email, Guid? GroupId, decimal OpeningBalance);
}
