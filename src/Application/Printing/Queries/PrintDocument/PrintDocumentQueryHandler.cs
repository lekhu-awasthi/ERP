using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Printing.Queries.PrintDocument;

/// <summary>
/// Builds the print DTO for any of the fifteen transactional document types (Phase 20d wired six,
/// Phase 27b the remaining nine).
///
/// <para><b>Shape of this file.</b> One <see cref="BuildAsync"/> per document type, each returning
/// a list of <see cref="PrintableSectionDto"/>. There is deliberately no generic
/// "line-item document" helper of the kind phase-20d had: the nine types added here differ in what
/// their tables <i>are</i> (a Warehouse Transfer has quantities and no money at all; a Production
/// Journal has three tables and a six-line cost summary), so a shared projection would have needed
/// a parameter per difference. What is shared is the frame -- <see cref="BuildDocument"/> assembles
/// the organization block, party block and header fields once for all fifteen.</para>
///
/// <para><b>Every business date goes through <see cref="RequestCalendar"/></b> (Phase 27b), so a
/// client that asked for Bikram Sambat gets BS in the PDF, not just on screen. Nothing here reads
/// the header itself -- the middleware parked it before this handler ran.</para>
/// </summary>
public sealed class PrintDocumentQueryHandler(IAppDbContext db) : IRequestHandler<PrintDocumentQuery, PrintableDocumentDto>
{
    public async Task<PrintableDocumentDto> Handle(PrintDocumentQuery request, CancellationToken cancellationToken)
    {
        var organization = await db.Organizations.SingleAsync(x => x.Id == request.OrganizationId, cancellationToken);

        var templateName = await db.PrintingTemplates
            .Where(x => x.OrganizationId == request.OrganizationId && x.DocumentType == request.DocumentType && x.IsDefault)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken) ?? "Default";

        return request.DocumentType switch
        {
            DocumentType.Quotation => await BuildQuotationAsync(request, organization, templateName, cancellationToken),
            DocumentType.SalesOrder => await BuildSalesOrderAsync(request, organization, templateName, cancellationToken),
            DocumentType.Invoice => await BuildInvoiceAsync(request, organization, templateName, cancellationToken),
            DocumentType.CreditNote => await BuildCreditNoteAsync(request, organization, templateName, cancellationToken),
            DocumentType.Payment => await BuildPaymentAsync(request, organization, templateName, cancellationToken),
            DocumentType.PurchaseOrder => await BuildPurchaseOrderAsync(request, organization, templateName, cancellationToken),
            DocumentType.PurchaseBill => await BuildPurchaseBillAsync(request, organization, templateName, cancellationToken),
            DocumentType.Expense => await BuildExpenseAsync(request, organization, templateName, cancellationToken),
            DocumentType.DebitNote => await BuildDebitNoteAsync(request, organization, templateName, cancellationToken),
            DocumentType.JournalVoucher => await BuildJournalVoucherAsync(request, organization, templateName, cancellationToken),
            DocumentType.CashTransfer => await BuildCashTransferAsync(request, organization, templateName, cancellationToken),
            DocumentType.WarehouseTransfer => await BuildWarehouseTransferAsync(request, organization, templateName, cancellationToken),
            DocumentType.InventoryAdjustment => await BuildInventoryAdjustmentAsync(request, organization, templateName, cancellationToken),
            DocumentType.ProductionOrder => await BuildProductionOrderAsync(request, organization, templateName, cancellationToken),
            DocumentType.ProductionJournal => await BuildProductionJournalAsync(request, organization, templateName, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request.DocumentType), request.DocumentType, "This document type has no printable record."),
        };
    }

    // ---- Sales -------------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildQuotationAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.Quotations.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Quotation not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))
            .ToList();

        var header = new List<PrintableFieldDto>();
        if (document.ExpiryDate is { } expiry)
        {
            header.Add(new PrintableFieldDto("Expiry Date", RequestCalendar.Format(expiry)));
        }

        return await BuildProductDocumentAsync(
            request, organization, templateName, "Quotation", document.Code, document.Date, document.Reference,
            document.ContactId, "Quotation For", header, lines, document.DiscountPct, document.Terms, ct);
    }

    private async Task<PrintableDocumentDto> BuildSalesOrderAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.SalesOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Sales order not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, templateName, "Sales Order", document.Code, document.Date, document.Reference,
            document.ContactId, "Order For", [], lines, document.DiscountPct, document.Terms, ct);
    }

    private async Task<PrintableDocumentDto> BuildInvoiceAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.Invoices.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Invoice not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))
            .ToList();

        var header = new List<PrintableFieldDto>();
        if (document.IsExport)
        {
            header.Add(new PrintableFieldDto("Export Sales", "Yes"));
            if (!string.IsNullOrWhiteSpace(document.ExportCountry))
            {
                header.Add(new PrintableFieldDto("Export Country", document.ExportCountry));
            }
        }

        return await BuildProductDocumentAsync(
            request, organization, templateName, "Invoice", document.Code, document.Date, document.Reference,
            document.ContactId, "Bill To", header, lines, document.DiscountPct, document.Terms, ct);
    }

    private async Task<PrintableDocumentDto> BuildCreditNoteAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.CreditNotes.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Credit note not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, templateName, "Credit Note", document.Code, document.Date, document.Reference,
            document.ContactId, "Credit To", [], lines, document.DiscountPct, document.Terms, ct);
    }

    /// <summary>Two sections, matching the reference product's Customer Receipt layout read live:
    /// "Payment Details" (the account the money moved through) and "Payment For" (what it was
    /// allocated against). An unallocated payment prints the second section empty rather than
    /// omitting it -- a receipt that silently drops the section reads as if nothing is outstanding.</summary>
    private async Task<PrintableDocumentDto> BuildPaymentAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.Payments.SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Payment not found.");

        var received = document.Direction == Domain.Payments.PaymentDirection.Received;

        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Id == document.AccountId, ct);
        var paymentModeName = document.PaymentModeId is { } modeId
            ? await db.PaymentModes.Where(x => x.Id == modeId).Select(x => x.Name).SingleOrDefaultAsync(ct)
            : null;

        var allocations = await db.PaymentAllocations
            .Where(x => x.SourceType == DocumentType.Payment && x.SourceId == document.Id)
            .ToListAsync(ct);

        var allocationRows = new List<PrintableRowDto>();
        foreach (var allocation in allocations.OrderBy(x => x.TargetDocumentType).ThenBy(x => x.Amount))
        {
            var (code, date) = await ResolveAllocationTargetAsync(allocation.TargetDocumentType, allocation.TargetDocumentId, ct);
            allocationRows.Add(new PrintableRowDto([
                $"{DocumentLabel(allocation.TargetDocumentType)} {code}",
                date is null ? "-" : RequestCalendar.Format(date.Value),
                Money(allocation.Amount),
            ]));
        }

        var header = new List<PrintableFieldDto>
        {
            new(received ? "Receipt No" : "Payment No", document.Code),
        };
        if (paymentModeName is not null)
        {
            header.Add(new PrintableFieldDto("Payment Mode", paymentModeName));
        }

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Payment Details",
                [new PrintableColumnDto("Account", 4), new PrintableColumnDto("Amount", 1, AlignRight: true)],
                [new PrintableRowDto([AccountLabel(account), Money(document.Amount)])],
                new PrintableRowDto([received ? "Net Debit" : "Net Credit", Money(document.Amount)])),
            new(
                "Payment For",
                [
                    new PrintableColumnDto("Document", 3),
                    new PrintableColumnDto("Date", 1, AlignRight: true),
                    new PrintableColumnDto("Amount", 1, AlignRight: true),
                ],
                allocationRows,
                allocationRows.Count == 0
                    ? null
                    : new PrintableRowDto(["Allocated", string.Empty, Money(allocations.Sum(x => x.Amount))])),
        };

        var unallocated = document.Amount - allocations.Sum(x => x.Amount);
        var summary = new List<PrintableFieldDto>
        {
            new("Amount", Money(document.Amount)),
            new("Allocated", Money(allocations.Sum(x => x.Amount))),
            new("Unallocated", Money(unallocated), Emphasise: true),
        };

        return await BuildDocumentAsync(
            request, organization, templateName,
            received ? "Customer Receipt" : "Supplier Payment",
            document.Code, document.Date, document.Reference,
            document.ContactId, received ? "Received From" : "Paid To",
            header, sections, summary, notes: null, terms: null, ct);
    }

    // ---- Purchasing --------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildPurchaseOrderAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Purchase order not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, templateName, "Purchase Order", document.Code, document.Date, document.Reference,
            document.ContactId, "Supplier", [], lines, document.DiscountPct, document.Terms, ct);
    }

    private async Task<PrintableDocumentDto> BuildPurchaseBillAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.PurchaseBills.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Purchase bill not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, templateName, "Purchase Bill", document.Code, document.Date, document.Reference,
            document.ContactId, "Supplier", [], lines, document.DiscountPct, terms: null, tdsAmount: document.TdsAmount, ct: ct);
    }

    /// <summary>Expense lines carry an <b>account</b>, not a product -- the one line-item document
    /// whose table is not a product table.</summary>
    private async Task<PrintableDocumentDto> BuildExpenseAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.Expenses.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Expense not found.");

        var accountIds = document.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.Where(x => accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var rows = document.Lines
            .Select(l => new PrintableRowDto([
                accounts.TryGetValue(l.AccountId, out var account) ? AccountLabel(account) : "(unknown account)",
                Money(l.VatAmount),
                Money(l.Amount + l.VatAmount),
            ]))
            .ToList();

        var subTotal = document.Lines.Sum(l => l.Amount);
        var vat = document.Lines.Sum(l => l.VatAmount);

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Expenses",
                [
                    new PrintableColumnDto("Account", 4),
                    new PrintableColumnDto("VAT", 1, AlignRight: true),
                    new PrintableColumnDto("Amount", 1, AlignRight: true),
                ],
                rows,
                new PrintableRowDto(["Total", Money(vat), Money(subTotal + vat)])),
        };

        var summary = new List<PrintableFieldDto> { new("Sub Total", Money(subTotal)), new("VAT", Money(vat)) };
        if (document.TdsAmount != 0)
        {
            summary.Add(new PrintableFieldDto("TDS", Money(document.TdsAmount)));
        }

        summary.Add(new PrintableFieldDto("Grand Total", Money(subTotal + vat - document.TdsAmount), Emphasise: true));

        var header = new List<PrintableFieldDto>();
        if (document.DueDate is { } dueDate)
        {
            header.Add(new PrintableFieldDto("Due Date", RequestCalendar.Format(dueDate)));
        }

        if (!string.IsNullOrWhiteSpace(document.SupplierInvoiceReference))
        {
            header.Add(new PrintableFieldDto("Supplier Invoice Ref", document.SupplierInvoiceReference));
        }

        return await BuildDocumentAsync(
            request, organization, templateName, "Expense", document.Code, document.Date,
            reference: null, document.ContactId, "Supplier",
            header, sections, summary, document.Notes, terms: null, ct);
    }

    private async Task<PrintableDocumentDto> BuildDebitNoteAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.DebitNotes.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Debit note not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, templateName, "Debit Note", document.Code, document.Date, document.Reference,
            document.ContactId, "Debit To", [], lines, document.DiscountPct, terms: null, tdsAmount: document.TdsAmount, ct: ct);
    }

    // ---- Accounting --------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildJournalVoucherAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.JournalVouchers.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Journal voucher not found.");

        var accountIds = document.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.Where(x => accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var rows = document.Lines
            .Select(l => new PrintableRowDto([
                accounts.TryGetValue(l.AccountId, out var account) ? AccountLabel(account) : "(unknown account)",
                l.Debit > 0 ? Money(l.Debit) : string.Empty,
                l.Credit > 0 ? Money(l.Credit) : string.Empty,
            ]))
            .ToList();

        var totalDebit = document.Lines.Sum(l => l.Debit);

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Entries",
                [
                    new PrintableColumnDto("Account", 4),
                    new PrintableColumnDto("Debit", 1, AlignRight: true),
                    new PrintableColumnDto("Credit", 1, AlignRight: true),
                ],
                rows,
                new PrintableRowDto(["Total", Money(totalDebit), Money(document.Lines.Sum(l => l.Credit))])),
        };

        return await BuildDocumentAsync(
            request, organization, templateName, "Journal Voucher", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, [], sections,
            [new PrintableFieldDto("Total", Money(totalDebit), Emphasise: true)], notes: null, terms: null, ct);
    }

    /// <summary>Two sections named exactly as the reference product prints them -- "Transferred
    /// From" carries the single source account, "Transferred To" the destination lines.</summary>
    private async Task<PrintableDocumentDto> BuildCashTransferAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.CashTransfers.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Cash transfer not found.");

        var accountIds = document.Lines.Select(l => l.ToAccountId).Append(document.FromAccountId).Distinct().ToList();
        var accounts = await db.Accounts.Where(x => accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);

        var total = document.Lines.Sum(l => l.Amount);
        var amountColumns = new[] { new PrintableColumnDto("Account", 4), new PrintableColumnDto("Amount", 1, AlignRight: true) };

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Transferred From",
                amountColumns,
                [new PrintableRowDto([
                    accounts.TryGetValue(document.FromAccountId, out var from) ? AccountLabel(from) : "(unknown account)",
                    Money(total),
                ])]),
            new(
                "Transferred To",
                amountColumns,
                document.Lines.Select(l => new PrintableRowDto([
                    accounts.TryGetValue(l.ToAccountId, out var to) ? AccountLabel(to) : "(unknown account)",
                    Money(l.Amount),
                ])).ToList(),
                new PrintableRowDto(["Total Transfer", Money(total)])),
        };

        return await BuildDocumentAsync(
            request, organization, templateName, "Transfer", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, [], sections,
            [new PrintableFieldDto("Total Transfer", Money(total), Emphasise: true)], notes: null, terms: null, ct);
    }

    // ---- Inventory ---------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildWarehouseTransferAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.WarehouseTransfers.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Warehouse transfer not found.");

        var products = await ProductLabelsAsync(document.Lines.Select(l => l.ProductId), ct);
        var warehouses = await db.Warehouses
            .Where(x => x.Id == document.FromWarehouseId || x.Id == document.ToWarehouseId)
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Items",
                [new PrintableColumnDto("Product", 4), new PrintableColumnDto("Quantity", 1, AlignRight: true)],
                document.Lines.Select(l => new PrintableRowDto([ProductLabel(products, l.ProductId), Quantity(l.Quantity)])).ToList(),
                new PrintableRowDto(["Total", Quantity(document.Lines.Sum(l => l.Quantity))])),
        };

        var header = new List<PrintableFieldDto>
        {
            new("From Warehouse", warehouses.GetValueOrDefault(document.FromWarehouseId, "-")),
            new("To Warehouse", warehouses.GetValueOrDefault(document.ToWarehouseId, "-")),
        };

        return await BuildDocumentAsync(
            request, organization, templateName, "Warehouse Transfer", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, header, sections,
            [new PrintableFieldDto("Total Quantity", Quantity(document.Lines.Sum(l => l.Quantity)), Emphasise: true)],
            notes: null, terms: null, ct);
    }

    private async Task<PrintableDocumentDto> BuildInventoryAdjustmentAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.InventoryAdjustments.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Inventory adjustment not found.");

        var products = await ProductLabelsAsync(document.Lines.Select(l => l.ProductId), ct);
        var warehouseName = await db.Warehouses
            .Where(x => x.Id == document.WarehouseId).Select(x => x.Name).SingleOrDefaultAsync(ct);

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Adjustments",
                [
                    new PrintableColumnDto("Product", 4),
                    new PrintableColumnDto("Direction", 1),
                    new PrintableColumnDto("Quantity", 1, AlignRight: true),
                    new PrintableColumnDto("Unit Cost", 1, AlignRight: true),
                    new PrintableColumnDto("Amount", 1, AlignRight: true),
                ],
                document.Lines.Select(l => new PrintableRowDto([
                    ProductLabel(products, l.ProductId),
                    l.Direction.ToString(),
                    Quantity(l.Quantity),
                    Money(l.UnitCost),
                    Money(l.Quantity * l.UnitCost),
                ])).ToList(),
                new PrintableRowDto([
                    "Total", string.Empty, Quantity(document.Lines.Sum(l => l.Quantity)), string.Empty,
                    Money(document.Lines.Sum(l => l.Quantity * l.UnitCost)),
                ])),
        };

        return await BuildDocumentAsync(
            request, organization, templateName, "Inventory Adjustment", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null,
            [new PrintableFieldDto("Warehouse", warehouseName ?? "-")], sections,
            [new PrintableFieldDto("Total Value", Money(document.Lines.Sum(l => l.Quantity * l.UnitCost)), Emphasise: true)],
            notes: null, terms: null, ct);
    }

    // ---- Manufacturing -----------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildProductionOrderAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.ProductionOrders
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Production order not found.");

        var productIds = document.RawMaterials.Select(l => l.ProductId)
            .Concat(document.ByProducts.Select(l => l.ProductId))
            .Append(document.ProductId);
        var products = await ProductLabelsAsync(productIds, ct);
        var costTerms = await CostTermNamesAsync(document.Expenses.Select(l => l.CostTermId), ct);

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Raw Materials (Input)",
                [new PrintableColumnDto("Product", 4), new PrintableColumnDto("Quantity", 1, AlignRight: true)],
                document.RawMaterials.Select(l => new PrintableRowDto([ProductLabel(products, l.ProductId), Quantity(l.Quantity)])).ToList()),
            new(
                "By Product (Output)",
                [
                    new PrintableColumnDto("Product", 4),
                    new PrintableColumnDto("% of Cost", 1, AlignRight: true),
                    new PrintableColumnDto("Quantity", 1, AlignRight: true),
                ],
                document.ByProducts.Select(l => new PrintableRowDto([
                    ProductLabel(products, l.ProductId), Percent(l.CostAllocationPct), Quantity(l.Quantity),
                ])).ToList()),
            new(
                "Expenses",
                [new PrintableColumnDto("Cost Term", 4), new PrintableColumnDto("Amount", 1, AlignRight: true)],
                document.Expenses.Select(l => new PrintableRowDto([
                    costTerms.GetValueOrDefault(l.CostTermId, "(unknown cost term)"), Money(l.Amount),
                ])).ToList(),
                new PrintableRowDto(["Total Expenses", Money(document.Expenses.Sum(l => l.Amount))])),
        };

        var header = new List<PrintableFieldDto>
        {
            new("Product", ProductLabel(products, document.ProductId)),
            new("Output Quantity", Quantity(document.OutputQuantity)),
        };

        return await BuildDocumentAsync(
            request, organization, templateName, "Production Order", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, header, sections,
            [new PrintableFieldDto("Planned Expenses", Money(document.Expenses.Sum(l => l.Amount)), Emphasise: true)],
            document.Notes, terms: null, ct);
    }

    /// <summary>The richest of the fifteen, and the one that made phase-20d's two fixed layouts
    /// untenable: three tables plus the six-line cost summary the reference product prints
    /// (Raw Material Cost, Production Expenses, Total Cost of Production, Cost Allocated to
    /// By-product, Finished Goods Cost, Cost Per Unit). Those five stored costs are null until the
    /// journal is approved, so a Draft prints the tables with a summary that says so rather than
    /// printing zeroes as if they were computed.</summary>
    private async Task<PrintableDocumentDto> BuildProductionJournalAsync(
        PrintDocumentQuery request, Organization organization, string templateName, CancellationToken ct)
    {
        var document = await db.ProductionJournals
            .Include(x => x.RawMaterials).Include(x => x.ByProducts).Include(x => x.Expenses)
            .SingleOrDefaultAsync(x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Production journal not found.");

        var productIds = document.RawMaterials.Select(l => l.ProductId)
            .Concat(document.ByProducts.Select(l => l.ProductId))
            .Append(document.ProductId);
        var products = await ProductLabelsAsync(productIds, ct);
        var costTerms = await CostTermNamesAsync(document.Expenses.Select(l => l.CostTermId), ct);
        var warehouseName = await db.Warehouses
            .Where(x => x.Id == document.WarehouseId).Select(x => x.Name).SingleOrDefaultAsync(ct);

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Raw Materials (Input)",
                [
                    new PrintableColumnDto("Product", 4),
                    new PrintableColumnDto("Quantity", 1, AlignRight: true),
                    new PrintableColumnDto("Rate", 1, AlignRight: true),
                    new PrintableColumnDto("Amount", 1, AlignRight: true),
                ],
                document.RawMaterials.Select(l => new PrintableRowDto([
                    ProductLabel(products, l.ProductId),
                    Quantity(l.Quantity),
                    OptionalMoney(l.ConsumedUnitCost),
                    OptionalMoney(l.Amount),
                ])).ToList(),
                new PrintableRowDto([
                    "Total", Quantity(document.RawMaterials.Sum(l => l.Quantity)), string.Empty,
                    OptionalMoney(document.RawMaterials.Sum(l => l.Amount ?? 0)),
                ])),
            new(
                "By Product (Output)",
                [
                    new PrintableColumnDto("Product", 4),
                    new PrintableColumnDto("% of Cost", 1, AlignRight: true),
                    new PrintableColumnDto("Quantity", 1, AlignRight: true),
                    new PrintableColumnDto("Rate", 1, AlignRight: true),
                    new PrintableColumnDto("Amount", 1, AlignRight: true),
                ],
                document.ByProducts.Select(l => new PrintableRowDto([
                    ProductLabel(products, l.ProductId),
                    Percent(l.CostAllocationPct),
                    Quantity(l.Quantity),
                    OptionalMoney(l.AllocatedUnitCost),
                    OptionalMoney(l.AllocatedAmount),
                ])).ToList()),
            new(
                "Production Expenses (Input)",
                [new PrintableColumnDto("Cost Term", 4), new PrintableColumnDto("Amount", 1, AlignRight: true)],
                document.Expenses.Select(l => new PrintableRowDto([
                    costTerms.GetValueOrDefault(l.CostTermId, "(unknown cost term)"), Money(l.Amount),
                ])).ToList(),
                new PrintableRowDto(["Total", Money(document.Expenses.Sum(l => l.Amount))])),
        };

        var summary = new List<PrintableFieldDto>
        {
            new("Raw Material Cost", OptionalMoney(document.RawMaterialCost)),
            new("Production Expenses", OptionalMoney(document.ProductionExpenseCost)),
            new("Total Cost of Production", OptionalMoney((document.RawMaterialCost + document.ProductionExpenseCost))),
            new("Cost Allocated to By-product", OptionalMoney(document.CostAllocatedToByProduct)),
            new("Finished Goods Cost", OptionalMoney(document.FinishedGoodsCost)),
            new("Cost Per Unit", OptionalMoney(document.FinishedGoodsUnitCost), Emphasise: true),
        };

        var header = new List<PrintableFieldDto>
        {
            new("Product", ProductLabel(products, document.ProductId)),
            new("Output Quantity", Quantity(document.OutputQuantity)),
            new("Warehouse", warehouseName ?? "-"),
        };

        return await BuildDocumentAsync(
            request, organization, templateName, "Production Journal", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, header, sections, summary, document.Notes, terms: null, ct);
    }

    // ---- Shared assembly ---------------------------------------------------------------------

    private readonly record struct ProductLine(
        Guid ProductId, decimal Quantity, decimal Rate, decimal DiscountPct, decimal Amount, decimal VatAmount);

    /// <summary>The seven product-line documents (Quotation, Sales Order, Invoice, Credit Note,
    /// Purchase Order, Purchase Bill, Debit Note) share one table and one totals block.</summary>
    private async Task<PrintableDocumentDto> BuildProductDocumentAsync(
        PrintDocumentQuery request,
        Organization organization,
        string templateName,
        string title,
        string code,
        DateOnly date,
        string? reference,
        Guid contactId,
        string partyHeading,
        List<PrintableFieldDto> headerFields,
        IReadOnlyList<ProductLine> lines,
        decimal discountPct,
        string? terms,
        CancellationToken ct,
        decimal tdsAmount = 0)
    {
        var products = await ProductLabelsAsync(lines.Select(l => l.ProductId), ct);

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Items",
                [
                    new PrintableColumnDto("Product", 4),
                    new PrintableColumnDto("Qty", 1, AlignRight: true),
                    new PrintableColumnDto("Rate", 1, AlignRight: true),
                    new PrintableColumnDto("Disc %", 1, AlignRight: true),
                    new PrintableColumnDto("VAT", 1, AlignRight: true),
                    new PrintableColumnDto("Amount", 1, AlignRight: true),
                ],
                lines.Select(l => new PrintableRowDto([
                    ProductLabel(products, l.ProductId),
                    Quantity(l.Quantity),
                    Money(l.Rate),
                    Percent(l.DiscountPct),
                    Money(l.VatAmount),
                    Money(l.Amount + l.VatAmount),
                ])).ToList(),
                new PrintableRowDto([
                    "Total", Quantity(lines.Sum(l => l.Quantity)), string.Empty, string.Empty,
                    Money(lines.Sum(l => l.VatAmount)), Money(lines.Sum(l => l.Amount + l.VatAmount)),
                ])),
        };

        var subTotal = lines.Sum(l => l.Amount);
        var vat = lines.Sum(l => l.VatAmount);

        var summary = new List<PrintableFieldDto> { new("Sub Total", Money(subTotal)) };
        if (discountPct > 0)
        {
            // The header discount is already folded into every line's Amount (phase-16b), so this
            // is disclosure of the rate applied, not a figure to subtract again.
            summary.Add(new PrintableFieldDto("Discount Applied", $"{Percent(discountPct)}%"));
        }

        summary.Add(new PrintableFieldDto("VAT", Money(vat)));
        if (tdsAmount != 0)
        {
            summary.Add(new PrintableFieldDto("TDS", Money(tdsAmount)));
        }

        summary.Add(new PrintableFieldDto("Grand Total", Money(subTotal + vat - tdsAmount), Emphasise: true));

        return await BuildDocumentAsync(
            request, organization, templateName, title, code, date, reference, contactId, partyHeading,
            headerFields, sections, summary, notes: null, terms, ct);
    }

    /// <summary>The frame every one of the fifteen shares: organization block, optional party block,
    /// header fields, sections, summary, notes, terms.</summary>
    private async Task<PrintableDocumentDto> BuildDocumentAsync(
        PrintDocumentQuery request,
        Organization organization,
        string templateName,
        string title,
        string code,
        DateOnly date,
        string? reference,
        Guid? contactId,
        string? partyHeading,
        IReadOnlyList<PrintableFieldDto> headerFields,
        IReadOnlyList<PrintableSectionDto> sections,
        IReadOnlyList<PrintableFieldDto> summary,
        string? notes,
        string? terms,
        CancellationToken ct)
    {
        Contact? contact = null;
        if (contactId is { } id)
        {
            contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        }

        return new PrintableDocumentDto(
            request.DocumentType,
            title,
            code,
            RequestCalendar.Format(date),
            reference,
            organization.Name,
            organization.Address,
            organization.Phone,
            organization.Email,
            organization.PanNumber,
            organization.Website,
            contact is null ? null : partyHeading,
            contact is null ? null : $"{contact.Code} — {contact.Name}",
            contact?.Address,
            templateName,
            headerFields,
            sections,
            summary,
            notes,
            terms,
            RequestCalendar.DisclosureLine);
    }

    /// <summary>A payment allocation points at a document by type + id; the receipt prints that
    /// document's own number and date. Only the four allocatable types are reachable here --
    /// anything else prints as a bare id rather than throwing, since a printed receipt failing
    /// outright over one unrecognised allocation row would be the worse outcome.</summary>
    private async Task<(string Code, DateOnly? Date)> ResolveAllocationTargetAsync(
        DocumentType targetType, Guid targetId, CancellationToken ct) => targetType switch
    {
        DocumentType.Invoice => await db.Invoices.Where(x => x.Id == targetId)
            .Select(x => new ValueTuple<string, DateOnly?>(x.Code, x.Date)).SingleOrDefaultAsync(ct),
        DocumentType.CreditNote => await db.CreditNotes.Where(x => x.Id == targetId)
            .Select(x => new ValueTuple<string, DateOnly?>(x.Code, x.Date)).SingleOrDefaultAsync(ct),
        DocumentType.PurchaseBill => await db.PurchaseBills.Where(x => x.Id == targetId)
            .Select(x => new ValueTuple<string, DateOnly?>(x.Code, x.Date)).SingleOrDefaultAsync(ct),
        DocumentType.DebitNote => await db.DebitNotes.Where(x => x.Id == targetId)
            .Select(x => new ValueTuple<string, DateOnly?>(x.Code, x.Date)).SingleOrDefaultAsync(ct),
        _ => (targetId.ToString(), null),
    };

    private async Task<Dictionary<Guid, string>> ProductLabelsAsync(IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        return await db.Products
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => $"{x.Code} — {x.Name}", ct);
    }

    private async Task<Dictionary<Guid, string>> CostTermNamesAsync(IEnumerable<Guid> costTermIds, CancellationToken ct)
    {
        var ids = costTermIds.Distinct().ToList();
        return await db.CostTerms.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
    }

    private static string ProductLabel(IReadOnlyDictionary<Guid, string> products, Guid productId) =>
        products.GetValueOrDefault(productId, "(unknown product)");

    private static string AccountLabel(Domain.Accounting.Account? account) =>
        account is null ? "(unknown account)" : $"{account.Code} — {account.Name}";

    private static string Money(decimal value) => value.ToString("#,##0.00");

    /// <summary>Blank, not "0.00", for a cost that has not been computed yet -- a Draft production
    /// journal has no rates, and printing zeroes would read as "this cost nothing".</summary>
    private static string OptionalMoney(decimal? value) => value is null ? string.Empty : Money(value.Value);

    private static string Quantity(decimal value) => value.ToString("0.####");

    private static string Percent(decimal value) => value.ToString("0.##");

    private static string DocumentLabel(DocumentType documentType) => documentType switch
    {
        DocumentType.PurchaseBill => "Purchase Bill",
        DocumentType.CreditNote => "Credit Note",
        DocumentType.DebitNote => "Debit Note",
        _ => documentType.ToString(),
    };
}
