using ErpApp.Api.Reports;
using ErpApp.Application.Accounting.Commands.ApproveCashTransfer;
using ErpApp.Application.Accounting.Commands.ApproveJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateAccount;
using ErpApp.Application.Accounting.Commands.CreateAccountGroup;
using ErpApp.Application.Accounting.Commands.CreateCashTransfer;
using ErpApp.Application.Accounting.Commands.CreateJournalVoucher;
using ErpApp.Application.Accounting.Commands.CreateOrUpdateOpeningBalanceLine;
using ErpApp.Application.Accounting.Commands.UpdateAccount;
using ErpApp.Application.Accounting.Commands.UpdateAccountGroup;
using ErpApp.Application.Accounting.Commands.UpdateCashTransfer;
using ErpApp.Application.Accounting.Commands.UpdateJournalVoucher;
using ErpApp.Application.Accounting.Commands.VoidCashTransfer;
using ErpApp.Application.Accounting.Commands.VoidJournalVoucher;
using ErpApp.Application.Accounting.Queries.BalanceSheet;
using ErpApp.Application.Accounting.Queries.CashFlowSummary;
using ErpApp.Application.Accounting.Queries.DetailGeneralLedger;
using ErpApp.Application.Accounting.Queries.GeneralLedgerMaster;
using ErpApp.Application.Accounting.Queries.GeneralLedgerSummary;
using ErpApp.Application.Accounting.Queries.GetAccount;
using ErpApp.Application.Accounting.Queries.GetCashTransfer;
using ErpApp.Application.Accounting.Queries.GetJournalVoucher;
using ErpApp.Application.Accounting.Queries.IncomeStatement;
using ErpApp.Application.Accounting.Queries.JournalReport;
using ErpApp.Application.Accounting.Queries.ListAccounts;
using ErpApp.Application.Accounting.Queries.ListBankAccounts;
using ErpApp.Application.Accounting.Queries.ListCashTransfers;
using ErpApp.Application.Accounting.Queries.ListJournalVouchers;
using ErpApp.Application.Accounting.Queries.ListOpeningBalanceLines;
using ErpApp.Application.Accounting.Queries.PreviewGlPosting;
using ErpApp.Application.Accounting.Queries.RatioAnalysis;
using ErpApp.Application.Accounting.Queries.TrialBalance;
using ErpApp.Application.Accounting.Queries.VatSummaryReport;
using ErpApp.Application.Accounting;
using ErpApp.Application.Common.Pagination;
using ErpApp.Application.Configuration.Commands.DeleteLookup;
using ErpApp.Application.Configuration.Queries.ListLookups;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Api.Endpoints;

public static class AccountingEndpoints
{
    public static void MapAccountingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations/{organizationId:guid}")
            .WithTags("Accounting")
            .RequireAuthorization();

        MapAccountGroupEndpoints(group);
        MapAccountEndpoints(group);
        MapBankAccountEndpoints(group);
        MapJournalVoucherEndpoints(group);
        MapCashTransferEndpoints(group);
        MapOpeningBalanceEndpoints(group);
        MapReportEndpoints(group);
    }

    private static void MapAccountGroupEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/account-groups", async (Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListLookupsQuery<AccountGroup>(organizationId, page ?? 1, pageSize ?? PagingDefaults.MaxPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPost("/account-groups", async (
            Guid organizationId, CreateAccountGroupRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateAccountGroupCommand(organizationId, request.Name, request.RootType, request.ParentGroupId), ct);
            return Results.Created($"/api/organizations/{organizationId}/account-groups/{result.Id}", result);
        });

        group.MapPut("/account-groups/{id:guid}", async (
            Guid organizationId, Guid id, UpdateAccountGroupRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateAccountGroupCommand(organizationId, id, request.Name, request.ParentGroupId, request.IsActive), ct);
            return Results.Ok(result);
        });

        group.MapDelete("/account-groups/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            await sender.Send(new DeleteLookupCommand<AccountGroup>(organizationId, id), ct);
            return Results.NoContent();
        });
    }

    private static void MapAccountEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/accounts", async (
            Guid organizationId, AccountRootType? rootType, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListAccountsQuery(organizationId, rootType, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/accounts/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAccountQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/accounts", async (
            Guid organizationId, CreateAccountRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateAccountCommand(
                    organizationId, request.Name, request.GroupId, request.Kind, request.BankId, request.AccountNumber),
                ct);
            return Results.Created($"/api/organizations/{organizationId}/accounts/{result.Id}", result);
        });

        group.MapPut("/accounts/{id:guid}", async (
            Guid organizationId, Guid id, UpdateAccountRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateAccountCommand(
                    organizationId, id, request.Name, request.GroupId, request.IsActive,
                    request.Kind, request.BankId, request.AccountNumber),
                ct);
            return Results.Ok(result);
        });
    }

    private static void MapBankAccountEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/bank-accounts", async (
            Guid organizationId, bool? isActive, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListBankAccountsQuery(
                    organizationId, isActive ?? true, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });
    }

    private static void MapJournalVoucherEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/journal-vouchers", async (
            Guid organizationId, JournalVoucherStatus? status, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListJournalVouchersQuery(organizationId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/journal-vouchers/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetJournalVoucherQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/journal-vouchers", async (
            Guid organizationId, JournalVoucherRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateJournalVoucherCommand(organizationId, request.Date, request.Reference, request.Lines) { CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate }, ct);
            return Results.Created($"/api/organizations/{organizationId}/journal-vouchers/{result.Id}", result);
        });

        group.MapPut("/journal-vouchers/{id:guid}", async (
            Guid organizationId, Guid id, JournalVoucherRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateJournalVoucherCommand(organizationId, id, request.Date, request.Reference, request.Lines) { CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate }, ct);
            return Results.Ok(result);
        });

        group.MapPost("/journal-vouchers/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveJournalVoucherCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/journal-vouchers/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidJournalVoucherCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/journal-vouchers/preview-gl-posting", async (
            Guid organizationId, PreviewGlPostingRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new PreviewGlPostingQuery(organizationId, request.Date, request.Reference, request.Lines), ct);
            return Results.Ok(result);
        });
    }

    private static void MapCashTransferEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/cash-transfers", async (
            Guid organizationId, CashTransferStatus? status, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListCashTransfersQuery(organizationId, status, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapGet("/cash-transfers/{id:guid}", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetCashTransferQuery(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/cash-transfers", async (
            Guid organizationId, CashTransferRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateCashTransferCommand(organizationId, request.Date, request.Reference, request.FromAccountId, request.Lines) { CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate },
                ct);
            return Results.Created($"/api/organizations/{organizationId}/cash-transfers/{result.Id}", result);
        });

        group.MapPut("/cash-transfers/{id:guid}", async (
            Guid organizationId, Guid id, CashTransferRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateCashTransferCommand(organizationId, id, request.Date, request.Reference, request.FromAccountId, request.Lines) { CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate },
                ct);
            return Results.Ok(result);
        });

        group.MapPost("/cash-transfers/{id:guid}/approve", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ApproveCashTransferCommand(organizationId, id), ct);
            return Results.Ok(result);
        });

        group.MapPost("/cash-transfers/{id:guid}/void", async (
            Guid organizationId, Guid id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VoidCashTransferCommand(organizationId, id), ct);
            return Results.Ok(result);
        });
    }

    private static void MapOpeningBalanceEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/opening-balances/accounts", async (
            Guid organizationId, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new ListAccountOpeningBalancesQuery(organizationId, page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize), ct);
            return Results.Ok(result);
        });

        group.MapPut("/opening-balances/accounts/{accountId:guid}", async (
            Guid organizationId, Guid accountId, OpeningBalanceLineRequest request, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateOrUpdateOpeningBalanceLineCommand(organizationId, accountId, request.Debit, request.Credit) { CurrencyCode = request.CurrencyCode, ExchangeRate = request.ExchangeRate }, ct);
            return Results.Ok(result);
        });
    }

    private static void MapReportEndpoints(RouteGroupBuilder group)
    {
        // compare (Phase 26a, FR-9.1) is an optional bool -- absent means the exact Phase 8a
        // response, so every pre-existing caller is unaffected. The comparison window itself is
        // never a request parameter: it is derived server-side by ComparePeriod and echoed back on
        // the response, so the screen and the .xlsx label the extra columns with real dates.
        group.MapGet("/reports/trial-balance", async (
            Guid organizationId, DateOnly asOfDate, bool? compare, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new TrialBalanceQuery(organizationId, asOfDate, compare ?? false), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/trial-balance/export", async (
            Guid organizationId, DateOnly asOfDate, bool? compare, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new TrialBalanceQuery(organizationId, asOfDate, compare ?? false), ct);
            return ReportSpreadsheetExporter.ExportTrialBalance(result);
        });

        group.MapGet("/reports/balance-sheet", async (
            Guid organizationId, DateOnly asOfDate, bool? compare, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new BalanceSheetQuery(organizationId, asOfDate, compare ?? false), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/balance-sheet/export", async (
            Guid organizationId, DateOnly asOfDate, bool? compare, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new BalanceSheetQuery(organizationId, asOfDate, compare ?? false), ct);
            return ReportSpreadsheetExporter.ExportBalanceSheet(result);
        });

        group.MapGet("/reports/income-statement", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, bool? compare, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new IncomeStatementQuery(organizationId, fromDate, toDate, compare ?? false), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/income-statement/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, bool? compare, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new IncomeStatementQuery(organizationId, fromDate, toDate, compare ?? false), ct);
            return ReportSpreadsheetExporter.ExportIncomeStatement(result);
        });

        // Phase 26a -- the four line-level/rollup GL reports the catalog was missing. Every one is
        // Period-filtered and paged; each has a matching /export route taking full=true|false, the
        // Current-View-vs-Full-List split phase-16c established.
        group.MapGet("/reports/journal-report", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, DocumentType? documentType,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new JournalReportQuery(
                    organizationId, fromDate, toDate, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/journal-report/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, DocumentType? documentType,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new JournalReportQuery(
                    organizationId, fromDate, toDate, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportJournalReport(result, fromDate, toDate);
        });

        group.MapGet("/reports/general-ledger-summary", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? groupId, Guid? accountId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GeneralLedgerSummaryQuery(
                    organizationId, fromDate, toDate, groupId, accountId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/general-ledger-summary/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? groupId, Guid? accountId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GeneralLedgerSummaryQuery(
                    organizationId, fromDate, toDate, groupId, accountId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportGeneralLedgerSummary(result, fromDate, toDate);
        });

        group.MapGet("/reports/detail-general-ledger", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? accountId,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new DetailGeneralLedgerQuery(
                    organizationId, fromDate, toDate, accountId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/detail-general-ledger/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? accountId,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new DetailGeneralLedgerQuery(
                    organizationId, fromDate, toDate, accountId,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportDetailGeneralLedger(result, fromDate, toDate);
        });

        group.MapGet("/reports/general-ledger-master", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, DocumentType? documentType,
            int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GeneralLedgerMasterQuery(
                    organizationId, fromDate, toDate, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize),
                ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/general-ledger-master/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, DocumentType? documentType,
            bool full, int? page, int? pageSize, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new GeneralLedgerMasterQuery(
                    organizationId, fromDate, toDate, documentType,
                    page ?? 1, pageSize ?? PagingDefaults.DefaultPageSize, ExportAll: full),
                ct);
            return ReportSpreadsheetExporter.ExportGeneralLedgerMaster(result, fromDate, toDate);
        });

        group.MapGet("/reports/cash-flow-summary", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? bankAccountId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CashFlowSummaryQuery(organizationId, fromDate, toDate, bankAccountId), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/cash-flow-summary/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, Guid? bankAccountId, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CashFlowSummaryQuery(organizationId, fromDate, toDate, bankAccountId), ct);
            return ReportSpreadsheetExporter.ExportCashFlowSummary(result);
        });

        group.MapGet("/reports/ratio-analysis", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RatioAnalysisQuery(organizationId, fromDate, toDate), ct);
            return Results.Ok(result);
        });

        group.MapGet("/reports/ratio-analysis/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new RatioAnalysisQuery(organizationId, fromDate, toDate), ct);
            return ReportSpreadsheetExporter.ExportRatioAnalysis(result);
        });

        group.MapGet("/reports/vat-summary", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VatSummaryReportQuery(organizationId, fromDate, toDate), ct);
            return Results.Ok(result);
        });

        // No "current view" vs "full dataset" distinction -- VatSummaryReportQuery always returns
        // every bucket (fixed 2x3 cardinality, never paginated, see VatSummaryReportQuery's own
        // doc comment), so both export variants would be identical; one export route is enough.
        group.MapGet("/reports/vat-summary/export", async (
            Guid organizationId, DateOnly fromDate, DateOnly toDate, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new VatSummaryReportQuery(organizationId, fromDate, toDate), ct);
            return ReportSpreadsheetExporter.ExportVatSummaryReport(result);
        });
    }

    private sealed record CreateAccountGroupRequest(string Name, AccountRootType RootType, Guid? ParentGroupId);

    private sealed record UpdateAccountGroupRequest(string Name, Guid? ParentGroupId, bool IsActive);

    private sealed record CreateAccountRequest(
        string Name, Guid GroupId, AccountKind Kind = AccountKind.Other, Guid? BankId = null, string? AccountNumber = null);

    private sealed record UpdateAccountRequest(
        string Name, Guid GroupId, bool IsActive, AccountKind Kind = AccountKind.Other, Guid? BankId = null,
        string? AccountNumber = null);

    private sealed record JournalVoucherRequest(DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines,
        // Phase 28 (FR-2.5) -- the Currency + "Exchange Rate To NPR" pair. Optional and trailing so
        // every existing caller is unchanged; null/null means the base currency at rate 1. These must
        // be carried on the request record itself, not only on the command: a trailing optional
        // parameter added to a command alone binds to null forever and every test still passes
        // (phase-27b's Terms).
        string? CurrencyCode = null, decimal? ExchangeRate = null);

    private sealed record PreviewGlPostingRequest(DateOnly Date, string? Reference, IReadOnlyList<JournalVoucherLineInput> Lines);

    private sealed record CashTransferRequest(
        DateOnly Date, string? Reference, Guid FromAccountId, IReadOnlyList<CashTransferLineInput> Lines,
        // Phase 28 (FR-2.5) -- the Currency + "Exchange Rate To NPR" pair. Optional and trailing so
        // every existing caller is unchanged; null/null means the base currency at rate 1. These must
        // be carried on the request record itself, not only on the command: a trailing optional
        // parameter added to a command alone binds to null forever and every test still passes
        // (phase-27b's Terms).
        string? CurrencyCode = null, decimal? ExchangeRate = null);

    private sealed record OpeningBalanceLineRequest(decimal Debit, decimal Credit,
        // Phase 28 (FR-2.5) -- the Currency + "Exchange Rate To NPR" pair. Optional and trailing so
        // every existing caller is unchanged; null/null means the base currency at rate 1. These must
        // be carried on the request record itself, not only on the command: a trailing optional
        // parameter added to a command alone binds to null forever and every test still passes
        // (phase-27b's Terms).
        string? CurrencyCode = null, decimal? ExchangeRate = null);
}
