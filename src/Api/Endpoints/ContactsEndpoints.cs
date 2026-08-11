using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Application.Contacts.Commands.CreateContact;
using ErpApp.Application.Contacts.Commands.CreateContactGroup;
using ErpApp.Application.Contacts.Commands.DeactivateContact;
using ErpApp.Application.Contacts.Commands.UpdateContact;
using ErpApp.Application.Contacts.Commands.UpdateContactGroup;
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
    }

    private static void MapContactGroupEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/contact-groups", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListLookupsQuery<ContactGroup>(organizationId), ct);
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
            Guid organizationId, ContactType? type, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListContactsQuery(organizationId, type), ct);
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
    }

    private sealed record CreateContactGroupRequest(string Name, Guid? ParentGroupId);

    private sealed record UpdateContactGroupRequest(string Name, Guid? ParentGroupId, bool IsActive);

    private sealed record CreateContactRequest(
        ContactType Type, string Name, string? Address, string? Pan, string? Phone, string? Email,
        Guid? GroupId, decimal OpeningBalance);

    private sealed record UpdateContactRequest(
        string Name, string? Address, string? Pan, string? Phone, string? Email, Guid? GroupId, decimal OpeningBalance);
}
