using ErpApp.Application.Configuration.Commands.CreateBank;
using ErpApp.Application.Configuration.Commands.CreateCostTerm;
using ErpApp.Application.Configuration.Commands.CreateCreditTerm;
using ErpApp.Application.Configuration.Commands.CreateCustomFieldDefinition;
using ErpApp.Application.Configuration.Commands.CreateCustomStatus;
using ErpApp.Application.Configuration.Commands.CreateDealStage;
using ErpApp.Application.Configuration.Commands.CreateLeadSource;
using ErpApp.Application.Configuration.Commands.CreatePaymentMode;
using ErpApp.Application.Configuration.Commands.CreateReportingTagCategory;
using ErpApp.Application.Configuration.Commands.CreateReportingTagOption;
using ErpApp.Application.Configuration.Commands.CreateTaskType;
using ErpApp.Application.Configuration.Commands.CreateTdsType;
using ErpApp.Application.Configuration.Commands.DeleteCustomFieldDefinition;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Commands.SetCustomFieldValues;
using ErpApp.Application.Configuration.Commands.SetCustomStatus;
using ErpApp.Application.Configuration.Commands.SetTransactionReportingTags;
using ErpApp.Application.Configuration.Commands.UpdateBank;
using ErpApp.Application.Configuration.Commands.UpdateCostTerm;
using ErpApp.Application.Configuration.Commands.UpdateCreditTerm;
using ErpApp.Application.Configuration.Commands.UpdateCustomFieldDefinition;
using ErpApp.Application.Configuration.Commands.UpdateCustomStatus;
using ErpApp.Application.Configuration.Commands.UpdateDealStage;
using ErpApp.Application.Configuration.Commands.UpdateLeadSource;
using ErpApp.Application.Configuration.Commands.UpdatePaymentMode;
using ErpApp.Application.Configuration.Commands.UpdateReportingTagCategory;
using ErpApp.Application.Configuration.Commands.UpdateReportingTagOption;
using ErpApp.Application.Configuration.Commands.UpdateTaskType;
using ErpApp.Application.Configuration.Commands.UpdateTdsType;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Queries.GetCustomFieldValues;
using ErpApp.Application.Configuration.Queries.GetTransactionReportingTags;
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
        MapBankEndpoints(group);
        MapCustomStatusEndpoints(group);
        MapReportingTagCategoryEndpoints(group);
        MapReportingTagOptionEndpoints(group);
        MapCustomFieldDefinitionEndpoints(group);
        MapTdsTypeEndpoints(group);
        MapTaskTypeEndpoints(group);
        MapLeadSourceEndpoints(group);
        MapDealStageEndpoints(group);
        MapTransactionReportingTagEndpoints(group);
        MapCustomFieldValueEndpoints(group);
        MapCustomStatusAssignmentEndpoints(group);
        MapCostTermEndpoints(group);
    }

    private static void MapCreditTermEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/credit-terms", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<CreditTerm>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
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
        group.MapGet("/payment-modes", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<PaymentMode>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/payment-modes", async (
            Guid organizationId, CreatePaymentModeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreatePaymentModeCommand(organizationId, request.Name, request.RequiresChequeDetails), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/payment-modes/{result.Id}", result);
        });

        group.MapPut("/payment-modes/{id:guid}", async (
            Guid organizationId, Guid id, UpdatePaymentModeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdatePaymentModeCommand(organizationId, id, request.Name, request.IsActive, request.RequiresChequeDetails), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/payment-modes/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<PaymentMode>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapBankEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/banks", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<Bank>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/banks", async (
            Guid organizationId, CreateBankRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateBankCommand(organizationId, request.Name), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/banks/{result.Id}", result);
        });

        group.MapPut("/banks/{id:guid}", async (
            Guid organizationId, Guid id, UpdateBankRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateBankCommand(organizationId, id, request.Name, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/banks/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<Bank>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapCustomStatusEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/custom-statuses", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<CustomStatus>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
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
        group.MapGet(
            "/reporting-tag-categories", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<ReportingTagCategory>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
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
        group.MapGet(
            "/reporting-tag-options", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<ReportingTagOption>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
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
        group.MapGet(
            "/custom-field-definitions", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListCustomFieldDefinitionsQuery(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/custom-field-definitions", async (
            Guid organizationId, CreateCustomFieldDefinitionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCustomFieldDefinitionCommand(
                    organizationId, request.Name, request.Type, request.ApplicableDocumentTypes, request.ChoiceOptions),
                ct);
            return Results.Created(
                $"/api/organizations/{organizationId}/configuration/custom-field-definitions/{result.Id}", result);
        });

        group.MapPut("/custom-field-definitions/{id:guid}", async (
            Guid organizationId, Guid id, UpdateCustomFieldDefinitionRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCustomFieldDefinitionCommand(
                    organizationId, id, request.Name, request.Type, request.ApplicableDocumentTypes, request.IsActive, request.ChoiceOptions),
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
        group.MapGet("/tds-types", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<TdsType>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
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

    private static void MapTaskTypeEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/task-types", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<TaskType>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/task-types", async (
            Guid organizationId, CreateTaskTypeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateTaskTypeCommand(organizationId, request.Name, request.Color), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/task-types/{result.Id}", result);
        });

        group.MapPut("/task-types/{id:guid}", async (
            Guid organizationId, Guid id, UpdateTaskTypeRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateTaskTypeCommand(organizationId, id, request.Name, request.Color, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/task-types/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<TaskType>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapLeadSourceEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/lead-sources", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<LeadSource>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/lead-sources", async (
            Guid organizationId, CreateLeadSourceRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateLeadSourceCommand(organizationId, request.Name), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/lead-sources/{result.Id}", result);
        });

        group.MapPut("/lead-sources/{id:guid}", async (
            Guid organizationId, Guid id, UpdateLeadSourceRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateLeadSourceCommand(organizationId, id, request.Name, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/lead-sources/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<LeadSource>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapDealStageEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/deal-stages", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<DealStage>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/deal-stages", async (
            Guid organizationId, CreateDealStageRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateDealStageCommand(organizationId, request.Name, request.SortOrder, request.Color), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/deal-stages/{result.Id}", result);
        });

        group.MapPut("/deal-stages/{id:guid}", async (
            Guid organizationId, Guid id, UpdateDealStageRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateDealStageCommand(organizationId, id, request.Name, request.SortOrder, request.Color, request.IsActive),
                ct);
            return Results.Ok(result);
        });

        group.MapDelete("/deal-stages/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<DealStage>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    /// <summary>
    /// Phase 20c -- Cost Terms (erp-module-scan.md Configurations §7). Reference data only; the
    /// two categories are one list with a discriminator, not two endpoints, matching how the
    /// reference product's single screen splits into two sections over one CostTerm shape.
    /// </summary>
    private static void MapCostTermEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/cost-terms", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<CostTerm>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/cost-terms", async (
            Guid organizationId, CreateCostTermRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCostTermCommand(organizationId, request.Name, request.Category), ct);
            return Results.Created($"/api/organizations/{organizationId}/configuration/cost-terms/{result.Id}", result);
        });

        group.MapPut("/cost-terms/{id:guid}", async (
            Guid organizationId, Guid id, UpdateCostTermRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCostTermCommand(organizationId, id, request.Name, request.Category, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/cost-terms/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<CostTerm>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private sealed record CreateCostTermRequest(string Name, CostTermCategory Category);

    private sealed record UpdateCostTermRequest(string Name, CostTermCategory Category, bool IsActive);

    private sealed record CreateCreditTermRequest(string Name, int DueDays);

    private sealed record UpdateCreditTermRequest(string Name, int DueDays, bool IsActive);

    private sealed record CreatePaymentModeRequest(string Name, bool RequiresChequeDetails = false);

    private sealed record UpdatePaymentModeRequest(string Name, bool IsActive, bool RequiresChequeDetails);

    private sealed record CreateBankRequest(string Name);

    private sealed record UpdateBankRequest(string Name, bool IsActive);

    private sealed record CreateCustomStatusRequest(string Name, DocumentType DocumentType);

    private sealed record UpdateCustomStatusRequest(string Name, DocumentType DocumentType, bool IsActive);

    private sealed record CreateReportingTagCategoryRequest(string Name);

    private sealed record UpdateReportingTagCategoryRequest(string Name, bool IsActive);

    private sealed record CreateReportingTagOptionRequest(string Name, Guid CategoryId);

    private sealed record UpdateReportingTagOptionRequest(string Name, Guid CategoryId, bool IsActive);

    private sealed record CreateCustomFieldDefinitionRequest(
        string Name, CustomFieldType Type, IReadOnlyList<DocumentType> ApplicableDocumentTypes, IReadOnlyList<string> ChoiceOptions);

    private sealed record UpdateCustomFieldDefinitionRequest(
        string Name,
        CustomFieldType Type,
        IReadOnlyList<DocumentType> ApplicableDocumentTypes,
        bool IsActive,
        IReadOnlyList<string> ChoiceOptions);

    private sealed record CreateTdsTypeRequest(string Code, string Name, decimal RatePct);

    private sealed record UpdateTdsTypeRequest(string Code, string Name, decimal RatePct, bool IsActive);

    private sealed record CreateTaskTypeRequest(string Name, string Color);

    private sealed record UpdateTaskTypeRequest(string Name, string Color, bool IsActive);

    private sealed record CreateLeadSourceRequest(string Name);

    private sealed record UpdateLeadSourceRequest(string Name, bool IsActive);

    private sealed record CreateDealStageRequest(string Name, int SortOrder, string? Color);

    private sealed record UpdateDealStageRequest(string Name, int SortOrder, string? Color, bool IsActive);

    // Reporting tags live outside the /configuration group's own lookup-CRUD shape -- they attach
    // to a transaction document, not a Configuration lookup row -- but stay in this file since
    // they're still under this same route base and reuse ReportingTagOption. See
    // SetTransactionReportingTagsCommand's doc comment for the granularity/permission reasoning.
    private static void MapTransactionReportingTagEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/reporting-tags/{documentType}/{documentId:guid}", async (
            Guid organizationId, DocumentType documentType, Guid documentId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetTransactionReportingTagsQuery(organizationId, documentType, documentId), ct);
            return Results.Ok(result);
        });

        group.MapPut("/reporting-tags/{documentType}/{documentId:guid}", async (
            Guid organizationId, DocumentType documentType, Guid documentId, SetTransactionReportingTagsRequest request,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(
                new SetTransactionReportingTagsCommand(organizationId, documentType, documentId, request.TagOptionIds), ct);
            return Results.NoContent();
        });
    }

    private sealed record SetTransactionReportingTagsRequest(IReadOnlyList<Guid> TagOptionIds);

    // Custom field values (Phase 20a) -- same "lives outside the lookup-CRUD shape" reasoning as
    // reporting tags above. See SetCustomFieldValuesCommand's doc comment for the
    // granularity/permission reasoning.
    private static void MapCustomFieldValueEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/custom-field-values/{documentType}/{documentId:guid}", async (
            Guid organizationId, DocumentType documentType, Guid documentId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCustomFieldValuesQuery(organizationId, documentType, documentId), ct);
            return Results.Ok(result);
        });

        group.MapPut("/custom-field-values/{documentType}/{documentId:guid}", async (
            Guid organizationId, DocumentType documentType, Guid documentId, SetCustomFieldValuesRequest request,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SetCustomFieldValuesCommand(organizationId, documentType, documentId, request.Values), ct);
            return Results.NoContent();
        });
    }

    private sealed record SetCustomFieldValuesRequest(IReadOnlyList<CustomFieldValueInput> Values);

    // Custom status assignment (Phase 20b) -- write-only: the document's own DTO (Quotation,
    // PurchaseOrder) already carries CustomStatusId, so no GET is needed here, unlike reporting
    // tags/custom field values which have no other read path.
    private static void MapCustomStatusAssignmentEndpoints(RouteGroupBuilder group)
    {
        group.MapPut("/custom-status/{documentType}/{documentId:guid}", async (
            Guid organizationId, DocumentType documentType, Guid documentId, SetCustomStatusRequest request,
            ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new SetCustomStatusCommand(organizationId, documentType, documentId, request.CustomStatusId), ct);
            return Results.NoContent();
        });
    }

    private sealed record SetCustomStatusRequest(Guid? CustomStatusId);
}
