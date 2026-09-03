using ErpApp.Api.Reports;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.Contacts.Commands.AddComment;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Commands.CreateContactGroup;
using ErpApp.Application.Contacts.Commands.CreateContactPersonnel;
using ErpApp.Application.Contacts.Commands.DeactivateContact;
using ErpApp.Application.Contacts.Commands.RemoveContactPersonnel;
using ErpApp.Application.Contacts.Commands.UpdateContact;
using ErpApp.Application.Contacts.Commands.UpdateContactGroup;
using ErpApp.Application.Contacts.Commands.UpdateContactPersonnel;
using ErpApp.Application.Contacts.Queries.ContactAgeingSummary;
using ErpApp.Application.Contacts.Queries.ContactBalanceSummary;
using ErpApp.Application.Contacts.Queries.ContactOverview;
using ErpApp.Application.Contacts.Queries.ContactStatement;
using ErpApp.Application.Contacts.Queries.DocumentAge;
using ErpApp.Application.Contacts.Queries.GetContact;
using ErpApp.Application.Contacts.Queries.ListActivities;
using ErpApp.Application.Contacts.Queries.ListComments;
using ErpApp.Application.Contacts.Queries.ListContactPersonnel;
using ErpApp.Application.Contacts.Queries.ListContacts;
using ErpApp.Application.Crm.Queries.ListSmsLogs;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Workflow;
using MediatR;
using Microsoft.AspNetCore.Mvc;

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
        MapContactPersonnelEndpoints(group);
        MapCommentAndActivityEndpoints(group);
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

    private static void MapContactPersonnelEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/contacts/{contactId:guid}/contact-personnel", async (
            Guid organizationId, Guid contactId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListContactPersonnelQuery(organizationId, contactId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/contacts/{contactId:guid}/contact-personnel", async (
            Guid organizationId, Guid contactId, ContactPersonnelRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateContactPersonnelCommand(
                    organizationId, contactId, request.Name, request.Address, request.Code, request.Phone,
                    request.GroupId, request.Email, request.OrganizationTitle),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/contacts/{contactId}/contact-personnel/{result.Id}", result);
        });

        group.MapPut("/contacts/{contactId:guid}/contact-personnel/{id:guid}", async (
            Guid organizationId, Guid contactId, Guid id, ContactPersonnelRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateContactPersonnelCommand(
                    organizationId, contactId, id, request.Name, request.Address, request.Code, request.Phone,
                    request.GroupId, request.Email, request.OrganizationTitle),
                ct);
            return Results.Ok(result);
        });

        group.MapDelete("/contacts/{contactId:guid}/contact-personnel/{id:guid}", async (
            Guid organizationId, Guid contactId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new RemoveContactPersonnelCommand(organizationId, contactId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapCommentAndActivityEndpoints(RouteGroupBuilder group)
    {
        // The Contact routes keep their shape -- the parent kind is hardcoded at the route, the same
        // "don't accept the discriminator from the client" choice the Ageing/Statement routes below
        // make. Phase 27a's document equivalents live in DocumentTabsEndpoints, which takes its
        // parent from its own route segments for exactly the same reason.
        group.MapGet("/contacts/{contactId:guid}/comments", async (
            Guid organizationId, Guid contactId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListCommentsQuery(
                    organizationId, CommentParentType.Contact, contactId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/contacts/{contactId:guid}/comments", async (
            Guid organizationId, Guid contactId, AddCommentRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new AddCommentCommand(organizationId, CommentParentType.Contact, contactId, request.Content), ct);
            return Results.Created($"/api/organizations/{organizationId}/contacts/{contactId}/comments/{result.Id}", result);
        });

        group.MapGet("/contacts/{contactId:guid}/activities", async (
            Guid organizationId, Guid contactId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListActivitiesQuery(
                    organizationId, DocumentType.Contact, contactId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/contacts/{contactId:guid}/sms-history", async (
            Guid organizationId, Guid contactId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListSmsLogsQuery(organizationId, contactId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
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

        // Phase 26b -- Receivable/Payable. Same "hardcode the discriminator at the route" choice as
        // the ageing/statement pair above: one shared handler, two report identities, two
        // permission keys, and no way for a caller to ask for the other side's data.

        group.MapGet("/reports/customer-receivable-summary", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactBalanceSummaryQuery(
                    organizationId, ContactType.Customer, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/customer-receivable-summary/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactBalanceSummaryQuery(
                    organizationId, ContactType.Customer, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportContactBalanceSummary(result, "Customer", "Customer Receivable Summary");
        });

        group.MapGet("/reports/supplier-payable-summary", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactBalanceSummaryQuery(
                    organizationId, ContactType.Supplier, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/supplier-payable-summary/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? contactGroupId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ContactBalanceSummaryQuery(
                    organizationId, ContactType.Supplier, fromDate, toDate, contactGroupId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportContactBalanceSummary(result, "Supplier", "Supplier Payable Summary");
        });

        group.MapGet("/reports/invoice-age", async (
            Guid organizationId, DateOnly fromDate, DateOnly asOfDate, Guid? contactId,
            [FromQuery(Name = "documentType")] AgeableDocumentType[]? documentType,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new DocumentAgeQuery(
                    organizationId, ContactType.Customer, fromDate, asOfDate, contactId, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/invoice-age/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly asOfDate, Guid? contactId,
            [FromQuery(Name = "documentType")] AgeableDocumentType[]? documentType,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new DocumentAgeQuery(
                    organizationId, ContactType.Customer, fromDate, asOfDate, contactId, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportDocumentAge(result, "Customer", "Invoice Age");
        });

        group.MapGet("/reports/purchase-bill-age", async (
            Guid organizationId, DateOnly fromDate, DateOnly asOfDate, Guid? contactId,
            [FromQuery(Name = "documentType")] AgeableDocumentType[]? documentType,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new DocumentAgeQuery(
                    organizationId, ContactType.Supplier, fromDate, asOfDate, contactId, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/purchase-bill-age/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly asOfDate, Guid? contactId,
            [FromQuery(Name = "documentType")] AgeableDocumentType[]? documentType,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new DocumentAgeQuery(
                    organizationId, ContactType.Supplier, fromDate, asOfDate, contactId, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportDocumentAge(result, "Supplier", "Purchase Bill Age");
        });

    }

    private sealed record CreateContactGroupRequest(string Name, Guid? ParentGroupId);

    private sealed record UpdateContactGroupRequest(string Name, Guid? ParentGroupId, bool IsActive);

    private sealed record CreateContactRequest(
        ContactType Type, string Name, string? Address, string? Pan, string? Phone, string? Email,
        Guid? GroupId, decimal OpeningBalance);

    private sealed record UpdateContactRequest(
        string Name, string? Address, string? Pan, string? Phone, string? Email, Guid? GroupId, decimal OpeningBalance);

    private sealed record ContactPersonnelRequest(
        string Name, string? Address, string? Code, string? Phone, Guid? GroupId, string? Email, string? OrganizationTitle);

    private sealed record AddCommentRequest(string Content);
}
