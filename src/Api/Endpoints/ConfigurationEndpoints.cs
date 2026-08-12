using ErpApp.Application.Configuration.Commands.CreateCreditTerm;
using ErpApp.Application.Configuration.Commands.CreateCustomFieldDefinition;
using ErpApp.Application.Configuration.Commands.CreateCustomStatus;
using ErpApp.Application.Configuration.Commands.CreatePaymentMode;
using ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;
using ErpApp.Application.Configuration.Commands.CreateReportingTagOption;
using ErpApp.Application.Configuration.Commands.CreateTdsType;
using ErpApp.Application.Configuration.Commands.DeleteCustomFieldDefinition;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Commands.UpdateCreditTerm;
using ErpApp.Application.Configuration.Commands.UpdateCustomFieldDefinition;
using ErpApp.Application.Configuration.Commands.UpdateCustomStatus;
using ErpApp.Application.Configuration.Commands.UpdatePaymentMode;
using ErpApp.Application.Configuration.Commands.UpdateReportingTagCategory;
using ErpApp.Application.Configuration.Commands.UpdateReportingTagOption;
using ErpApp.Application.Configuration.Commands.UpdateTdsType;
using ErpApp.Application.Configuration.Queries.ListCustomFieldDefinitions;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class ConfigurationEndpoints
{
    public static void MapConfigurationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}/configuration")
            .WithTags("Configuration")
            .RequireAuthorization();

        MapCreditTermEndpoints(group);
        MapPaymentModeEndpoints(group);
        MapCustomStatusEndpoints(group);
        MapReportingTagCategoryEndpoints(group);
        MapReportingTagOptionEndpoints(group);
        MapCustomFieldDefinitionEndpoints(group);
        MapTdsTypeEndpoints(group);
    }

    private static void MapCreditTermEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/credit-terms", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListLookupsQuery<CreditTerm>(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/credit-terms", async (
            Guid organizationId, CreateCreditTermRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCreditTermCommand(organizationId, request.Name, request.DueDays), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/credit-terms/{result.Id}", result);
        });

        group.MapPut("/credit-terms/{id:guid}", async (
            Guid organizationId, Guid id, UpdateCreditTermRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCreditTermCommand(organizationId, id, request.Name, request.DueDays, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/credit-terms/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<CreditTerm>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapPaymentModeEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/payment-modes", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListLookupsQuery<PaymentMode>(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/payment-modes", async (
            Guid organizationId, CreatePaymentModeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreatePaymentModeCommand(organizationId, request.Name), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/payment-modes/{result.Id}", result);
        });

        group.MapPut("/payment-modes/{id:guid}", async (
            Guid organizationId, Guid id, UpdatePaymentModeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdatePaymentModeCommand(organizationId, id, request.Name, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/payment-modes/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<PaymentMode>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapCustomStatusEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/custom-statuses", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListLookupsQuery<CustomStatus>(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/custom-statuses", async (
            Guid organizationId, CreateCustomStatusRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCustomStatusCommand(organizationId, request.Name, request.DocumentType), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/custom-statuses/{result.Id}", result);
        });

        group.MapPut("/custom-statuses/{id:guid}", async (
            Guid organizationId, Guid id, UpdateCustomStatusRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCustomStatusCommand(organizationId, id, request.Name, request.DocumentType, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/custom-statuses/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<CustomStatus>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapReportingTagCategoryEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/reporting-tag-categories", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListLookupsQuery<ReportingTagCategory>(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/reporting-tag-categories", async (
            Guid organizationId, CreateReportingTagCategoryRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateReportingTagCategoryCommand(organizationId, request.Name), ct);
            return Results.Created(
                $"/api/organizations/{organizationId}/configuration/reporting-tag-categories/{result.Id}", result);
        });

        group.MapPut("/reporting-tag-categories/{id:guid}", async (
            Guid organizationId, Guid id, UpdateReportingTagCategoryRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateReportingTagCategoryCommand(organizationId, id, request.Name, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/reporting-tag-categories/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<ReportingTagCategory>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapReportingTagOptionEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/reporting-tag-options", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListLookupsQuery<ReportingTagOption>(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/reporting-tag-options", async (
            Guid organizationId, CreateReportingTagOptionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateReportingTagOptionCommand(organizationId, request.Name, request.CategoryId), ct);
            return Results.Created(
                $"/api/organizations/{organizationId}/configuration/reporting-tag-options/{result.Id}", result);
        });

        group.MapPut("/reporting-tag-options/{id:guid}", async (
            Guid organizationId, Guid id, UpdateReportingTagOptionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateReportingTagOptionCommand(organizationId, id, request.Name, request.CategoryId, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/reporting-tag-options/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<ReportingTagOption>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapCustomFieldDefinitionEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/custom-field-definitions", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListCustomFieldDefinitionsQuery(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/custom-field-definitions", async (
            Guid organizationId, CreateCustomFieldDefinitionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCustomFieldDefinitionCommand(organizationId, request.Name, request.Type, request.ApplicableDocumentTypes),
                ct);
            return Results.Created(
                $"/api/organizations/{organizationId}/configuration/custom-field-definitions/{result.Id}", result);
        });

        group.MapPut("/custom-field-definitions/{id:guid}", async (
            Guid organizationId, Guid id, UpdateCustomFieldDefinitionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCustomFieldDefinitionCommand(
                    organizationId, id, request.Name, request.Type, request.ApplicableDocumentTypes, request.IsActive),
                ct);
            return Results.Ok(result);
        });

        group.MapDelete("/custom-field-definitions/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteCustomFieldDefinitionCommand(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapTdsTypeEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/tds-types", async (Guid organizationId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListLookupsQuery<TdsType>(organizationId), ct);
            return Results.Ok(result);
        });

        group.MapPost("/tds-types", async (
            Guid organizationId, CreateTdsTypeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateTdsTypeCommand(organizationId, request.Code, request.Name, request.RatePct), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/tds-types/{result.Id}", result);
        });

        group.MapPut("/tds-types/{id:guid}", async (
            Guid organizationId, Guid id, UpdateTdsTypeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateTdsTypeCommand(organizationId, id, request.Code, request.Name, request.RatePct, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/tds-types/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<TdsType>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private sealed record CreateCreditTermRequest(string Name, int DueDays);

    private sealed record UpdateCreditTermRequest(string Name, int DueDays, bool IsActive);

    private sealed record CreatePaymentModeRequest(string Name);

    private sealed record UpdatePaymentModeRequest(string Name, bool IsActive);

    private sealed record CreateCustomStatusRequest(string Name, DocumentType DocumentType);

    private sealed record UpdateCustomStatusRequest(string Name, DocumentType DocumentType, bool IsActive);

    private sealed record CreateReportingTagCategoryRequest(string Name);

    private sealed record UpdateReportingTagCategoryRequest(string Name, bool IsActive);

    private sealed record CreateReportingTagOptionRequest(string Name, Guid CategoryId);

    private sealed record UpdateReportingTagOptionRequest(string Name, Guid CategoryId, bool IsActive);

    private sealed record CreateCustomFieldDefinitionRequest(
        string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes);

    private sealed record UpdateCustomFieldDefinitionRequest(
        string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes, bool IsActive);

    private sealed record CreateTdsTypeRequest(string Code, string Name, decimal RatePct);

    private sealed record UpdateTdsTypeRequest(string Code, string Name, decimal RatePct, bool IsActive);
}
