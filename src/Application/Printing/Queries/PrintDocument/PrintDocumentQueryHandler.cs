using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Formatting;
using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Configuration;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ErpApp.Application.Common.Storage;

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
///
/// <para><b>Except on a tax document</b> (Phase 71, <see cref="TaxDocuments"/>): every date on an invoice, credit
/// note or debit note goes through <see cref="BillDate"/>, BS with AD beside it, whatever the request asked.
/// <see cref="FormatDate"/> is the one place that choice is made, so no builder can format a date the other way.</para>
/// </summary>
public sealed class PrintDocumentQueryHandler(IAppDbContext db, IFileStorage storage)
    : IRequestHandler<PrintDocumentQuery, PrintableDocumentDto>
{
    public async Task<PrintableDocumentDto> Handle(PrintDocumentQuery request, CancellationToken cancellationToken)
    {
        // Phase 67 (Decision C) -- an invoice's or credit note's copies are counted, so it renders only
        // as a counted print. A GET that wrote the row would be a GET that writes; one that did not
        // would hand out an unmarked, uncounted copy.
        if (CountedPrints.Applies(request.DocumentType) && request.Issue is null)
        {
            throw new ConflictException(
                $"Every copy of this {DocumentLabel(request.DocumentType).ToLowerInvariant()} is counted, so it is printed " +
                $"with POST /print/{request.DocumentType}/{{id}}, which records the print.");
        }

        var organization = await db.Organizations.SingleAsync(x => x.Id == request.OrganizationId, cancellationToken);

        // Phase 75 -- the document's location chooses its template (falling back to the type's default) and prints
        // its own line under the organization's address. A missing document reads as no location here; its builder
        // 404s below as it always did.
        var (_, locationId) = await DocumentLocationReader.ReadAsync(db, request.DocumentType, request.DocumentId, cancellationToken);

        // Phase 73 -- the template, or the settings every document printed with before there were any. Read into a
        // field because the builders consult it below the frame (a product's label drops its code, the logo's blob is
        // not read when hidden); everything else it does is PrintTemplateApplier's, once, below.
        var chosenTemplate = await PrintingTemplateResolver.ResolveAsync(
            db, request.OrganizationId, request.DocumentType, locationId, cancellationToken);
        template = chosenTemplate?.Settings ?? PrintingTemplateSettings.Default;

        var dto = request.DocumentType switch
        {
            DocumentType.Quotation => await BuildQuotationAsync(request, organization, cancellationToken),
            DocumentType.SalesOrder => await BuildSalesOrderAsync(request, organization, cancellationToken),
            DocumentType.Invoice => await BuildInvoiceAsync(request, organization, cancellationToken),
            DocumentType.CreditNote => await BuildCreditNoteAsync(request, organization, cancellationToken),
            DocumentType.Payment => await BuildPaymentAsync(request, organization, cancellationToken),
            DocumentType.PurchaseOrder => await BuildPurchaseOrderAsync(request, organization, cancellationToken),
            DocumentType.PurchaseBill => await BuildPurchaseBillAsync(request, organization, cancellationToken),
            DocumentType.Expense => await BuildExpenseAsync(request, organization, cancellationToken),
            DocumentType.DebitNote => await BuildDebitNoteAsync(request, organization, cancellationToken),
            DocumentType.JournalVoucher => await BuildJournalVoucherAsync(request, organization, cancellationToken),
            DocumentType.CashTransfer => await BuildCashTransferAsync(request, organization, cancellationToken),
            DocumentType.WarehouseTransfer => await BuildWarehouseTransferAsync(request, organization, cancellationToken),
            DocumentType.InventoryAdjustment => await BuildInventoryAdjustmentAsync(request, organization, cancellationToken),
            DocumentType.ProductionOrder => await BuildProductionOrderAsync(request, organization, cancellationToken),
            DocumentType.ProductionJournal => await BuildProductionJournalAsync(request, organization, cancellationToken),
            DocumentType.DeliveryNote => await BuildDeliveryNoteAsync(request, organization, cancellationToken),
            DocumentType.GoodsReceivedNote => await BuildGoodsReceivedNoteAsync(request, organization, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request.DocumentType), request.DocumentType, "This document type has no printable record."),
        };

        dto = dto with { CustomFields = await CustomFieldsAsync(request, cancellationToken) };

        if (await PrintedLocationAsync(request.OrganizationId, locationId, cancellationToken) is { } location)
        {
            dto = dto with { LocationName = location.Name, LocationAddress = location.Address };
        }

        return PrintTemplateApplier.Apply(dto, template);
    }

    /// <summary>Phase 73 -- the request's template's settings; set first thing in <see cref="Handle"/>.</summary>
    private PrintingTemplateSettings template = PrintingTemplateSettings.Default;

    /// <summary>
    /// Phase 75 (Decision A) -- the location a document prints under the organization's address: its own, and only
    /// when the organization has more than one, so a one-branch tenant's bills never say "HeadOffice". Counted over
    /// every location, active or not, because a branch since closed still issued the bills being reprinted.
    /// </summary>
    private async Task<(string Name, string? Address)?> PrintedLocationAsync(
        Guid organizationId, Guid? locationId, CancellationToken ct)
    {
        if (locationId is not { } id
            || await db.BillingLocations.CountAsync(x => x.OrganizationId == organizationId, ct) < 2)
        {
            return null;
        }

        var location = await db.BillingLocations
            .Where(x => x.Id == id && x.OrganizationId == organizationId)
            .Select(x => new { x.Name, x.Address })
            .SingleOrDefaultAsync(ct);

        return location is null ? null : (location.Name, location.Address);
    }

    // ---- Sales -------------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildQuotationAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.Quotations.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Quotation not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId))
            .ToList();

        var header = new List<PrintableFieldDto>();
        if (document.ExpiryDate is { } expiry)
        {
            header.Add(new PrintableFieldDto("Expiry Date", FormatDate(request, expiry)));
        }

        return await BuildProductDocumentAsync(
            request, organization, "Quotation", document.Code, document.Date, document.Reference,
            document.ContactId, "Quotation For", header, lines, document.DiscountPct, document.Terms, ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate);
    }

    private async Task<PrintableDocumentDto> BuildSalesOrderAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.SalesOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Sales order not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, "Sales Order", document.Code, document.Date, document.Reference,
            document.ContactId, "Order For", [], lines, document.DiscountPct, document.Terms, ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate);
    }

    private async Task<PrintableDocumentDto> BuildInvoiceAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.Invoices.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Invoice not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId)
            {
                ServiceChargeAmount = l.ServiceChargeAmount,
                PackagingFeeAmount = l.PackagingFeeAmount,
                DiscountPerUnit = l.DiscountPerUnit,
                PartDiscountAmount = l.PartDiscountAmount,
                PartServiceChargeWaived = l.PartServiceChargeWaived,
            })
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

        // Phase 67 (Decision A) -- the heading the bill is, by the same rule as the till's receipt.
        var heading = InvoiceHeadings.For(organization.IsVatRegistered, document.IsAbbreviatedTaxInvoice);

        var dto = await BuildProductDocumentAsync(
            request, organization, InvoiceHeadings.English(heading), document.Code, document.Date, document.Reference,
            document.ContactId, "Bill To", header, lines, document.DiscountPct, document.Terms, ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate, roundOff: document.RoundOff,
            discountAmount: document.DiscountAmount);

        return WithStatutoryDates(dto, document.ApprovedAt, document.IsAbbreviatedTaxInvoice) with
        {
            TitleNepali = InvoiceHeadings.Nepali(heading),
            PrintedCopy = await PrintedCopyAsync(request, ct),
            PartyPan = await PartyPanAsync(document.ContactId, ct),
        };
    }

    private async Task<PrintableDocumentDto> BuildCreditNoteAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.CreditNotes.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Credit note not found.");

        // Phase 63 -- a till refund's service charge and round-off print as the sale's do (both zero on
        // an ERP note).
        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId)
            {
                ServiceChargeAmount = l.ServiceChargeAmount,
                PackagingFeeAmount = l.PackagingFeeAmount,
                DiscountPerUnit = l.DiscountPerUnit,
            })
            .ToList();

        // Phase 67 (Decision F) -- VAT Rule 20(1) asks a credit note for the number and date of the tax
        // invoice it relates to. Phase 69 -- however the note names it: the invoice it returns, the one a
        // price adjustment names, or one issued before the system and typed. A note naming none (only an
        // older draft or a non-VAT tenant's) prints its Reference as before rather than an invented link.
        // Phase 71 -- the number and the date as two fields: a bill's date is "BS (AD)" and, after a real
        // invoice number, one field wrapped inside the parenthesis in the half-width header column.
        var header = new List<PrintableFieldDto>();
        if (document.RelatedInvoiceId is { } invoiceId)
        {
            var invoice = await db.Invoices
                .Where(x => x.Id == invoiceId && x.OrganizationId == request.OrganizationId)
                .Select(x => new { x.Code, x.Date })
                .SingleOrDefaultAsync(ct);
            if (invoice is not null)
            {
                header.Add(new PrintableFieldDto("Against Invoice", invoice.Code));
                header.Add(new PrintableFieldDto("Invoice Date", FormatDate(request, invoice.Date)));
            }
        }
        else if (document.AgainstInvoiceNumber is { } typedNumber && document.AgainstInvoiceDate is { } typedDate)
        {
            header.Add(new PrintableFieldDto("Against Invoice", typedNumber));
            header.Add(new PrintableFieldDto("Invoice Date", FormatDate(request, typedDate)));
        }

        // Phase 69 (Decision D) -- Rule 20(1)(f)'s "details of ... the credit": the reason, when there is
        // one. Required on a till refund, optional on an ERP note; neither printed it until now.
        if (document.Reason is { } reason)
        {
            header.Add(new PrintableFieldDto("Reason", reason));
        }

        var dto = await BuildProductDocumentAsync(
            request, organization, "Credit Note", document.Code, document.Date, document.Reference,
            document.ContactId, "Credit To", header, lines, document.DiscountPct, document.Terms, ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate, roundOff: document.RoundOff,
            discountAmount: document.DiscountAmount);

        return WithStatutoryDates(dto, document.ApprovedAt) with
        {
            TitleNepali = InvoiceHeadings.CreditNoteNepali,
            PrintedCopy = await PrintedCopyAsync(request, ct),
            PartyPan = await PartyPanAsync(document.ContactId, ct),
        };
    }

    /// <summary>The buyer's PAN for the two statutory documents, or null when the contact has none -- PAN
    /// stays optional on a contact, so a retail customer prints exactly as before (the till's rule, phase 62).
    /// Whether IRD expects one on a high-value sale to an unregistered buyer is a tax-advisor question, and
    /// would be a warning on approve, never a block here.</summary>
    private async Task<string?> PartyPanAsync(Guid contactId, CancellationToken ct)
    {
        var pan = await db.Contacts.Where(x => x.Id == contactId).Select(x => x.Pan).SingleOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(pan) ? null : pan.Trim();
    }

    /// <summary>Phase 67 -- the counted print's number, printer and Nepal wall-clock time, or null for an
    /// uncounted type. The time is Nepal's whatever the server's zone, as every tenant-facing time is.</summary>
    private async Task<PrintedCopyDto?> PrintedCopyAsync(PrintDocumentQuery request, CancellationToken ct)
    {
        if (request.Issue is not { } issue)
        {
            return null;
        }

        var printedBy = await db.Users
            .Where(x => x.Id == issue.PrintedByUserId)
            .Select(x => x.FullName)
            .SingleOrDefaultAsync(ct) ?? "";

        var at = $"{FormatDate(request, NepalTime.LocalDate(issue.PrintedAt))} {NepalTime.LocalTimeOfDay(issue.PrintedAt).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}";
        return new PrintedCopyDto(issue.PrintNumber, printedBy, at);
    }

    /// <summary>Phase 71 -- every date this handler prints: a tax document's in BS with AD beside it, any other
    /// type's in the request's calendar. Phase 84 -- and the money receipt's (<see cref="BillDatedDocuments"/>).</summary>
    private static string FormatDate(PrintDocumentQuery request, DateOnly date) =>
        BillDatedDocuments.Applies(request.DocumentType) ? BillDate.Format(date) : RequestCalendar.Format(date);

    /// <summary>Phase 71 -- the frame's date relabelled as the first statutory date, and the second beside it. The
    /// issue date is the Nepal day of approval, when the bill got its number; a draft has none and prints none. An
    /// abbreviated tax invoice keeps its form's one "Date" (Schedule 6), as its till receipt does.</summary>
    private static PrintableDocumentDto WithStatutoryDates(
        PrintableDocumentDto dto, DateTimeOffset? approvedAt, bool isAbbreviatedTaxInvoice = false) =>
        isAbbreviatedTaxInvoice
            ? dto
            : dto with
            {
                DateLabel = "Transaction Date",
                IssueDateText = approvedAt is { } at ? BillDate.Format(NepalTime.LocalDate(at)) : null,
            };

    /// <summary>Two sections, matching the reference product's Customer Receipt layout read live:
    /// "Payment Details" (the account the money moved through) and "Payment For" (what it was
    /// allocated against). An unallocated payment prints the second section empty rather than
    /// omitting it -- a receipt that silently drops the section reads as if nothing is outstanding.</summary>
    private async Task<PrintableDocumentDto> BuildPaymentAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
                date is null ? "-" : FormatDate(request, date.Value),
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
            request, organization,
            received ? "Customer Receipt" : "Supplier Payment",
            document.Code, document.Date, document.Reference,
            document.ContactId, received ? "Received From" : "Paid To",
            header, sections, summary, notes: null, terms: null, ct,
            document.CurrencyCode, document.ExchangeRate, amountInWordsTotal: document.Amount);
    }

    // ---- Purchasing --------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildPurchaseOrderAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Purchase order not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, "Purchase Order", document.Code, document.Date, document.Reference,
            document.ContactId, "Supplier", [], lines, document.DiscountPct, document.Terms, ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate);
    }

    private async Task<PrintableDocumentDto> BuildPurchaseBillAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.PurchaseBills.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Purchase bill not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, "Purchase Bill", document.Code, document.Date, document.Reference,
            document.ContactId, "Supplier", [], lines, document.DiscountPct, terms: null, tdsAmount: document.TdsAmount, ct: ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate);
    }

    /// <summary>Expense lines carry an <b>account</b>, not a product -- the one line-item document
    /// whose table is not a product table.</summary>
    private async Task<PrintableDocumentDto> BuildExpenseAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
            header.Add(new PrintableFieldDto("Due Date", FormatDate(request, dueDate)));
        }

        if (!string.IsNullOrWhiteSpace(document.SupplierInvoiceReference))
        {
            header.Add(new PrintableFieldDto("Supplier Invoice Ref", document.SupplierInvoiceReference));
        }

        return await BuildDocumentAsync(
            request, organization, "Expense", document.Code, document.Date,
            reference: null, document.ContactId, "Supplier",
            header, sections, summary, document.Notes, terms: null, ct,
            document.CurrencyCode, document.ExchangeRate, amountInWordsTotal: subTotal + vat - document.TdsAmount);
    }

    private async Task<PrintableDocumentDto> BuildDebitNoteAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.DebitNotes.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Debit note not found.");

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId))
            .ToList();

        var dto = await BuildProductDocumentAsync(
            request, organization, "Debit Note", document.Code, document.Date, document.Reference,
            document.ContactId, "Debit To", [], lines, document.DiscountPct, terms: null, tdsAmount: document.TdsAmount, ct: ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate);

        return WithStatutoryDates(dto, document.ApprovedAt);
    }

    // ---- Accounting --------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildJournalVoucherAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
            request, organization, "Journal Voucher", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, [], sections,
            [new PrintableFieldDto("Total", Money(totalDebit), Emphasise: true)], notes: null, terms: null, ct,
            document.CurrencyCode, document.ExchangeRate);
    }

    /// <summary>Two sections named exactly as the reference product prints them -- "Transferred
    /// From" carries the single source account, "Transferred To" the destination lines.</summary>
    private async Task<PrintableDocumentDto> BuildCashTransferAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
            request, organization, "Transfer", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, [], sections,
            [new PrintableFieldDto("Total Transfer", Money(total), Emphasise: true)], notes: null, terms: null, ct,
            document.CurrencyCode, document.ExchangeRate);
    }

    // ---- Physical movement (phase 58) ----------------------------------------------------------

    /// <summary>The Sales Order print plus the warehouse and delivery facts. The live Printing
    /// Templates page lists a Delivery Note section, and its money prints as its own form shows it:
    /// the lines, discount, VAT and total the document carries, none of which is posted.</summary>
    private async Task<PrintableDocumentDto> BuildDeliveryNoteAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.DeliveryNotes.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Delivery note not found.");

        var warehouseName = await db.Warehouses
            .Where(x => x.Id == document.WarehouseId).Select(x => x.Name).SingleOrDefaultAsync(ct);

        var header = new List<PrintableFieldDto>
        {
            new("Expected Delivery", FormatDate(request, document.ExpectedDeliveryDate)),
            new("Warehouse", warehouseName ?? "-"),
        };
        if (!string.IsNullOrWhiteSpace(document.TrackingNo))
        {
            header.Add(new PrintableFieldDto("Tracking No", document.TrackingNo));
        }

        if (!string.IsNullOrWhiteSpace(document.ShippingAddress))
        {
            header.Add(new PrintableFieldDto("Ship To", document.ShippingAddress));
        }

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, "Delivery Note", document.Code, document.Date, document.Reference,
            document.ContactId, "Customer", header, lines, document.DiscountPct, document.Terms, ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate);
    }

    /// <summary>The Purchase Order print plus the warehouse and tracking number.</summary>
    private async Task<PrintableDocumentDto> BuildGoodsReceivedNoteAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
    {
        var document = await db.GoodsReceivedNotes.Include(x => x.Lines).SingleOrDefaultAsync(
            x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, ct)
            ?? throw new NotFoundException("Goods received note not found.");

        var warehouseName = await db.Warehouses
            .Where(x => x.Id == document.WarehouseId).Select(x => x.Name).SingleOrDefaultAsync(ct);

        var header = new List<PrintableFieldDto> { new("Warehouse", warehouseName ?? "-") };
        if (!string.IsNullOrWhiteSpace(document.TrackingNo))
        {
            header.Add(new PrintableFieldDto("Tracking No", document.TrackingNo));
        }

        var lines = document.Lines
            .Select(l => new ProductLine(l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount, l.UnitId))
            .ToList();

        return await BuildProductDocumentAsync(
            request, organization, "Goods Received Note", document.Code, document.Date, document.Reference,
            document.ContactId, "Supplier", header, lines, document.DiscountPct, terms: null, ct,
            currencyCode: document.CurrencyCode, exchangeRate: document.ExchangeRate);
    }

    // ---- Inventory ---------------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildWarehouseTransferAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
            request, organization, "Warehouse Transfer", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, header, sections,
            [new PrintableFieldDto("Total Quantity", Quantity(document.Lines.Sum(l => l.Quantity)), Emphasise: true)],
            notes: null, terms: null, ct);
    }

    private async Task<PrintableDocumentDto> BuildInventoryAdjustmentAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
            request, organization, "Inventory Adjustment", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null,
            [new PrintableFieldDto("Warehouse", warehouseName ?? "-")], sections,
            [new PrintableFieldDto("Total Value", Money(document.Lines.Sum(l => l.Quantity * l.UnitCost)), Emphasise: true)],
            notes: null, terms: null, ct);
    }

    // ---- Manufacturing -----------------------------------------------------------------------

    private async Task<PrintableDocumentDto> BuildProductionOrderAsync(
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
            request, organization, "Production Order", document.Code, document.Date, document.Reference,
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
        PrintDocumentQuery request, Organization organization, CancellationToken ct)
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
            request, organization, "Production Journal", document.Code, document.Date, document.Reference,
            contactId: null, partyHeading: null, header, sections, summary, document.Notes, terms: null, ct);
    }

    // ---- Shared assembly ---------------------------------------------------------------------

    /// <summary>One printed line. <see cref="ServiceChargeAmount"/> is a till line's (phase 61), as is
    /// <see cref="PackagingFeeAmount"/> (phase 70), and both are zero on every other line of every other
    /// document, which is why they are init properties rather than positional ones every one of the seven
    /// builders would have to pass.</summary>
    private readonly record struct ProductLine(
        Guid ProductId, decimal Quantity, decimal Rate, decimal DiscountPct, decimal Amount, decimal VatAmount, Guid? UnitId)
    {
        public decimal ServiceChargeAmount { get; init; }

        public decimal PackagingFeeAmount { get; init; }

        /// <summary>Phase 74 -- a till line's Amount discount, rupees off each unit.</summary>
        public decimal DiscountPerUnit { get; init; }

        /// <summary>Phase 91 -- of a restaurant bill's Amount discount, one guest's own (phase 83), and the service
        /// charge that guest declined; zero on every other line.</summary>
        public decimal PartDiscountAmount { get; init; }

        public decimal PartServiceChargeWaived { get; init; }

        public decimal LineTotal => Amount + ServiceChargeAmount + PackagingFeeAmount + VatAmount;
    }

    /// <summary>The seven product-line documents (Quotation, Sales Order, Invoice, Credit Note,
    /// Purchase Order, Purchase Bill, Debit Note) share one table and one totals block.</summary>
    private async Task<PrintableDocumentDto> BuildProductDocumentAsync(
        PrintDocumentQuery request,
        Organization organization,
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
        decimal tdsAmount = 0,
        string? currencyCode = null,
        decimal? exchangeRate = null,
        decimal roundOff = 0,
        decimal discountAmount = 0)
    {
        var products = await ProductDetailsAsync(lines.Select(l => l.ProductId), ct);

        // Phase 73 -- the unit a line was entered in (phase 52: the line stores it, or the product's primary unit when
        // it names none), and the product's HS code. Each is a template element; an HS code column with nothing in it
        // is left out rather than printed empty, since most products carry none.
        Guid? UnitOf(ProductLine line) => line.UnitId ?? products.GetValueOrDefault(line.ProductId)?.PrimaryUnitId;
        var units = await UnitNamesAsync(lines.Select(UnitOf), ct);
        string UnitName(ProductLine line) => UnitOf(line) is { } unitId ? units.GetValueOrDefault(unitId, string.Empty) : string.Empty;
        var anyHsCode = lines.Any(l => !string.IsNullOrWhiteSpace(products.GetValueOrDefault(l.ProductId)?.HsCode));
        var anyPerUnit = lines.Any(l => l.DiscountPerUnit != 0m);

        List<string> Cells(string product, string hsCode, string quantity, string unit, string rate, string discount, string vatAmount, string amount)
        {
            var cells = new List<string> { product };
            if (anyHsCode)
            {
                cells.Add(hsCode);
            }

            cells.AddRange([quantity, unit, rate, discount, vatAmount, amount]);
            return cells;
        }

        // Phase 73 -- widths sized so a six-figure amount ("1,24,300.00" is no longer than "124,300.00") fits a ruled
        // cell on one line: money gets more room than the short columns (quantity, unit, percent) beside it.
        var columns = new List<PrintableColumnDto> { new("Product", 3.4f) };
        if (anyHsCode)
        {
            columns.Add(new PrintableColumnDto("HS Code", 1, Element: PrintElement.HsCode));
        }

        columns.AddRange([
            new PrintableColumnDto("Qty", 0.8f, AlignRight: true),
            new PrintableColumnDto("Unit", 0.8f, Element: PrintElement.Unit),
            new PrintableColumnDto("Rate", 1.4f, AlignRight: true),
            // Phase 74 -- a till line's discount can be rupees off each unit, so the column says which each cell is.
            new PrintableColumnDto(anyPerUnit ? "Disc" : "Disc %", anyPerUnit ? 1.2f : 0.8f, AlignRight: true, Element: PrintElement.DiscountColumn),
            new PrintableColumnDto("VAT", 1.2f, AlignRight: true, Element: PrintElement.VatColumn),
            new PrintableColumnDto("Amount", 1.5f, AlignRight: true),
        ]);

        // A quantity total means something only in one unit; across a box and a piece it adds unlike things, so the
        // total row leaves it blank (now that the unit is printed beside every quantity, the sum would be visibly wrong).
        var oneUnit = lines.Select(UnitOf).Distinct().Count() <= 1;

        string Discount(ProductLine line) => !anyPerUnit
            ? Percent(line.DiscountPct)
            : line.DiscountPerUnit != 0m ? $"{Money(line.DiscountPerUnit)}/unit" : $"{Percent(line.DiscountPct)}%";

        var sections = new List<PrintableSectionDto>
        {
            new(
                "Items",
                columns,
                lines.Select(l => new PrintableRowDto(Cells(
                    products.GetValueOrDefault(l.ProductId)?.Label ?? "(unknown product)",
                    products.GetValueOrDefault(l.ProductId)?.HsCode ?? string.Empty,
                    Quantity(l.Quantity),
                    UnitName(l),
                    Money(l.Rate),
                    Discount(l),
                    Money(l.VatAmount),
                    Money(l.LineTotal)))).ToList(),
                new PrintableRowDto(Cells(
                    "Total", string.Empty, oneUnit ? Quantity(lines.Sum(l => l.Quantity)) : string.Empty,
                    oneUnit && lines.Count > 0 ? UnitName(lines[0]) : string.Empty, string.Empty, string.Empty,
                    Money(lines.Sum(l => l.VatAmount)), Money(lines.Sum(l => l.LineTotal))))),
        };

        var subTotal = lines.Sum(l => l.Amount);
        var serviceCharge = lines.Sum(l => l.ServiceChargeAmount);
        var packagingFee = lines.Sum(l => l.PackagingFeeAmount);
        var vat = lines.Sum(l => l.VatAmount);

        // Phase 74 -- a till bill's Amount discount is rupees already taken off its lines, so it prints above the Sub
        // Total it was taken from, the arithmetic reading down the page: what the lines came to, less the discount, is
        // the Sub Total. Printed under it (as the rate is), a figure reads as a second subtraction -- it did, once.
        //
        // Phase 83 -- a restaurant bill can carry both: the order's percentage, already in every line's Amount, and one
        // guest's own discount in rupees. Two rows both called "Discount Applied" would read as one discount printed
        // twice, so the guest's is named for what it is, the bill's own; and the percentage prints first, because the
        // figure the bill's discount comes off is already after it. Printed under the Sub Total, as it is alone, it
        // read as a second 10% off a total it is already inside (phase 74's lesson, seen on the rendered page).
        //
        // Phase 91 -- and a bill can carry two Amount discounts with no percentage: the order's rupees and a guest's own.
        // Phase 83 printed them as one "Discount Applied" (its § 5); they print apart, the order's first, the guest's
        // named as phase 83 names it, so the two rows still read down to the Sub Total.
        var summary = new List<PrintableFieldDto>();
        var both = discountAmount > 0 && discountPct > 0;
        var guestsDiscount = lines.Sum(l => l.PartDiscountAmount);
        var twoAmounts = !both && guestsDiscount > 0 && discountAmount > guestsDiscount;
        if (both)
        {
            summary.Add(new PrintableFieldDto("Discount Applied", $"{Percent(discountPct)}%"));
        }

        if (discountAmount > 0)
        {
            summary.Add(new PrintableFieldDto(both ? "Before Bill Discount" : "Before Discount", Money(subTotal + discountAmount)));
            if (twoAmounts)
            {
                summary.Add(new PrintableFieldDto("Discount Applied", $"({Money(discountAmount - guestsDiscount)})"));
                summary.Add(new PrintableFieldDto("Bill Discount", $"({Money(guestsDiscount)})"));
            }
            else
            {
                summary.Add(new PrintableFieldDto(both ? "Bill Discount" : "Discount Applied", $"({Money(discountAmount)})"));
            }
        }

        summary.Add(new PrintableFieldDto("Sub Total", Money(subTotal)));
        if (discountPct > 0 && !both)
        {
            // The header discount is already folded into every line's Amount (phase-16b), so this
            // is disclosure of the rate applied, not a figure to subtract again.
            summary.Add(new PrintableFieldDto("Discount Applied", $"{Percent(discountPct)}%"));
        }

        // Phase 61 -- a till sale prints its service charge and round-off as lines of their own (the
        // vendor's receipt folds both away, phase 59 defect 3), and phase 70 its packaging fee. None
        // appears on any other document.
        if (serviceCharge != 0)
        {
            summary.Add(new PrintableFieldDto("Service Charge", Money(serviceCharge)));
        }
        else if (lines.Sum(l => l.PartServiceChargeWaived) > 0)
        {
            // Phase 91 -- a guest declined it on this bill. Said, not priced: a figure here would read as charged, and
            // one under the total as a second subtraction (phase 74's lesson).
            summary.Add(new PrintableFieldDto("Service Charge", "Waived"));
        }

        if (packagingFee != 0)
        {
            summary.Add(new PrintableFieldDto("Packaging", Money(packagingFee)));
        }

        summary.Add(new PrintableFieldDto("VAT", Money(vat)));
        if (tdsAmount != 0)
        {
            summary.Add(new PrintableFieldDto("TDS", Money(tdsAmount)));
        }

        if (roundOff != 0)
        {
            summary.Add(new PrintableFieldDto("Round Off", Money(roundOff)));
        }

        var grandTotal = subTotal + serviceCharge + packagingFee + vat - tdsAmount + roundOff;
        summary.Add(new PrintableFieldDto("Grand Total", Money(grandTotal), Emphasise: true));

        return await BuildDocumentAsync(
            request, organization, title, code, date, reference, contactId, partyHeading,
            headerFields, sections, summary, notes: null, terms, ct, currencyCode, exchangeRate, amountInWordsTotal: grandTotal);
    }

    /// <summary>The frame every one of the fifteen shares: organization block, optional party block,
    /// header fields, sections, summary, notes, terms.</summary>
    private async Task<PrintableDocumentDto> BuildDocumentAsync(
        PrintDocumentQuery request,
        Organization organization,
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
        CancellationToken ct,
        string? currencyCode = null,
        decimal? exchangeRate = null,
        decimal? amountInWordsTotal = null)
    {
        Contact? contact = null;
        if (contactId is { } id)
        {
            contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == id, ct);
        }

        // Phase 28 (FR-2.5) -- what a foreign-currency document prints. Confirmed live 2026-09-04
        // by reading the reference product's own print output: line amounts are bare, the Net Total
        // carries the currency CODE ("NPR 3,06,500.00"), the amount-in-words line names the
        // currency, and there is no base-currency column anywhere in the layout. A frame with no
        // column for the NPR equivalent cannot print one, so the decision is settled by the layout
        // rather than by preference: the printed figure is the transaction currency, full stop.
        // The rate is disclosed as a header field instead, so a reader can reconcile the document
        // against a Trial Balance without a second money column crowding the page.
        var code_ = currencyCode ?? CurrencyCatalog.BaseCode;
        var rate = exchangeRate ?? ExchangeRates.BaseRate;

        if (!CurrencyCatalog.IsBase(code_))
        {
            headerFields = headerFields
                .Append(new PrintableFieldDto($"Exchange Rate To {CurrencyCatalog.BaseCode}", rate.ToString("0.######")))
                .ToList();
        }

        var logo = template.Shows(PrintElement.OrganizationLogo) ? await ReadLogoAsync(storage, organization, ct) : null;

        return new PrintableDocumentDto(
            request.DocumentType,
            title,
            code,
            FormatDate(request, date),
            reference,
            organization.Name,
            organization.Address,
            organization.Phone,
            organization.Email,
            organization.PanNumber,
            organization.Website,
            logo,
            contact is null ? null : partyHeading,
            contact is null ? null : $"{contact.Code} — {contact.Name}",
            contact?.Address,
            template.Name,
            headerFields,
            sections,
            summary,
            notes,
            terms,
            // A bill labels each of its dates "BS" and "AD" inline, so the footer's disclosure would only
            // repeat it, and in AD it says nothing anyway.
            BillDatedDocuments.Applies(request.DocumentType) ? null : RequestCalendar.DisclosureLine,
            code_)
        {
            // Phase 73 -- the words name the currency; phase 79 -- in the document's own currency, not only rupees.
            AmountInWords = amountInWordsTotal is { } total ? Domain.Common.AmountInWords.For(total, code_) : null,
        };
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
        var showCode = template.Shows(PrintElement.ItemCode);
        var rows = await db.Products
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.Id, x => showCode ? $"{x.Code} — {x.Name}" : x.Name);
    }

    /// <summary>Phase 73 -- a product-table line's label (with its code unless the template hides it), HS code and
    /// primary unit.</summary>
    private sealed record ProductDetails(string Label, string? HsCode, Guid PrimaryUnitId);

    private async Task<Dictionary<Guid, ProductDetails>> ProductDetailsAsync(IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var showCode = template.Shows(PrintElement.ItemCode);
        var rows = await db.Products
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.HsCode, x.PrimaryUnitId })
            .ToListAsync(ct);

        return rows.ToDictionary(
            x => x.Id,
            x => new ProductDetails(showCode ? $"{x.Code} — {x.Name}" : x.Name, x.HsCode, x.PrimaryUnitId));
    }

    /// <summary>Phase 73 -- each unit's short name ("Pcs", "Box"), as the till's receipt prints it.</summary>
    private async Task<Dictionary<Guid, string>> UnitNamesAsync(IEnumerable<Guid?> unitIds, CancellationToken ct)
    {
        var ids = unitIds.OfType<Guid>().Distinct().ToList();
        return await db.UnitsOfMeasurement
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ShortName, ct);
    }

    /// <summary>
    /// Phase 73 -- the custom fields the template prints, in its order, each with this document's value. A field
    /// switched off or no longer applying to the type since the template was saved prints nothing, and so does one
    /// left blank on this document: an empty label is noise on a bill.
    /// </summary>
    private async Task<IReadOnlyList<PrintableFieldDto>> CustomFieldsAsync(PrintDocumentQuery request, CancellationToken ct)
    {
        var ids = template.PrintedCustomFieldIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var fields = await db.CustomFieldDefinitions
            .Where(x => x.OrganizationId == request.OrganizationId && x.IsActive && ids.Contains(x.Id))
            .ToListAsync(ct);
        var values = await db.CustomFieldValues
            .Where(x => x.OrganizationId == request.OrganizationId
                        && x.DocumentType == request.DocumentType
                        && x.DocumentId == request.DocumentId
                        && ids.Contains(x.FieldDefinitionId))
            .ToListAsync(ct);

        var printed = new List<PrintableFieldDto>();
        foreach (var id in ids)
        {
            var field = fields.SingleOrDefault(x => x.Id == id);
            var value = values.FirstOrDefault(x => x.FieldDefinitionId == id)?.Value;
            if (field is not null && field.ApplicableDocumentTypes.Contains(request.DocumentType)
                && !string.IsNullOrWhiteSpace(value))
            {
                printed.Add(new PrintableFieldDto(field.Name, value));
            }
        }

        return printed;
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

    /// <summary>
    /// Phase 39 -- the organization's logo for the printed header, or null.
    ///
    /// <para><b>A missing blob is not an error here.</b> A storage key pointing at a file that is
    /// gone means somebody's logo went missing; it does not mean their invoice should fail to print.
    /// The header simply renders as it did before this phase, which is a shape the layout already
    /// supports because it is the shape every tenant without a logo gets.</para>
    ///
    /// <para>Phase 73 -- static and internal so the template preview prints the same header.</para>
    /// </summary>
    internal static async Task<byte[]?> ReadLogoAsync(IFileStorage storage, Organization organization, CancellationToken ct)
    {
        if (organization.LogoStorageKey is null)
        {
            return null;
        }

        try
        {
            await using var content = await storage.OpenReadAsync(organization.LogoStorageKey, ct);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, ct);

            return buffer.ToArray();
        }
        catch (IOException)
        {
            return null;
        }
    }

}
