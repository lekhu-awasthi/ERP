using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using ErpApp.Domain.Tenancy;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Printing.Queries.PrintDocument;

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
            DocumentType.Invoice => await BuildLineItemDocumentAsync(
                request, organization, templateName,
                await db.Invoices.Include(x => x.Lines).SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Invoice not found."),
                x => (x.ContactId, x.Code, x.Date, x.Reference, x.DiscountPct, x.GrandTotal,
                    x.Lines.Select(l => (l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))),
                cancellationToken),

            DocumentType.Quotation => await BuildLineItemDocumentAsync(
                request, organization, templateName,
                await db.Quotations.Include(x => x.Lines).SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Quotation not found."),
                x => (x.ContactId, x.Code, x.Date, x.Reference, x.DiscountPct, x.Lines.Sum(l => l.Amount + l.VatAmount),
                    x.Lines.Select(l => (l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))),
                cancellationToken),

            DocumentType.SalesOrder => await BuildLineItemDocumentAsync(
                request, organization, templateName,
                await db.SalesOrders.Include(x => x.Lines).SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Sales order not found."),
                x => (x.ContactId, x.Code, x.Date, x.Reference, x.DiscountPct, x.Lines.Sum(l => l.Amount + l.VatAmount),
                    x.Lines.Select(l => (l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))),
                cancellationToken),

            DocumentType.PurchaseOrder => await BuildLineItemDocumentAsync(
                request, organization, templateName,
                await db.PurchaseOrders.Include(x => x.Lines).SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Purchase order not found."),
                x => (x.ContactId, x.Code, x.Date, x.Reference, x.DiscountPct, x.Lines.Sum(l => l.Amount + l.VatAmount),
                    x.Lines.Select(l => (l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))),
                cancellationToken),

            DocumentType.PurchaseBill => await BuildLineItemDocumentAsync(
                request, organization, templateName,
                await db.PurchaseBills.Include(x => x.Lines).SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Purchase bill not found."),
                x => (x.ContactId, x.Code, x.Date, x.Reference, x.DiscountPct, x.GrandTotal,
                    x.Lines.Select(l => (l.ProductId, l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount))),
                cancellationToken),

            DocumentType.JournalVoucher => await BuildLedgerDocumentAsync(
                request, organization, templateName,
                await db.JournalVouchers.Include(x => x.Lines).SingleOrDefaultAsync(
                    x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId, cancellationToken)
                    ?? throw new NotFoundException("Journal voucher not found."),
                x => (x.Code, x.Date, x.Reference, x.Lines.Select(l => (l.AccountId, l.Debit, l.Credit))),
                cancellationToken),

            _ => throw new ArgumentOutOfRangeException(
                nameof(request.DocumentType), request.DocumentType, "Printing is not wired up for this document type yet."),
        };
    }

    /// <summary>Shared for every line-item document type (Invoice/Quotation/SalesOrder/
    /// PurchaseOrder/PurchaseBill) -- a Func projecting each aggregate's own header/line fields
    /// into one common tuple shape, so the Contact/Product name-resolution and DTO assembly below
    /// is written exactly once. Not a generic EF query (the CLAUDE.md Func/Where-translation
    /// gotcha) -- the projection runs entirely in memory, against an already-materialized entity.</summary>
    private async Task<PrintableDocumentDto> BuildLineItemDocumentAsync<TDocument>(
        PrintDocumentQuery request,
        Organization organization,
        string templateName,
        TDocument document,
        Func<TDocument, (
            Guid ContactId,
            string Code,
            DateOnly Date,
            string? Reference,
            decimal DiscountPct,
            decimal GrandTotal,
            IEnumerable<(Guid ProductId, decimal Quantity, decimal Rate, decimal DiscountPct, decimal Amount, decimal VatAmount)> Lines)>
            project,
        CancellationToken cancellationToken)
    {
        var header = project(document);

        var contact = await db.Contacts.SingleOrDefaultAsync(x => x.Id == header.ContactId, cancellationToken);
        var productIds = header.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);

        return new PrintableDocumentDto(
            request.DocumentType,
            header.Code,
            header.Date,
            header.Reference,
            organization.Name,
            organization.Address,
            organization.Phone,
            organization.Email,
            organization.PanNumber,
            organization.Website,
            contact is null ? null : $"{contact.Code} — {contact.Name}",
            contact?.Address,
            templateName,
            header.Lines.Select(l => new PrintableLineDto(
                products.TryGetValue(l.ProductId, out var product) ? $"{product.Code} — {product.Name}" : "(unknown product)",
                l.Quantity, l.Rate, l.DiscountPct, l.Amount, l.VatAmount)).ToList(),
            null,
            header.GrandTotal,
            header.DiscountPct);
    }

    /// <summary>Shared for every ledger document type (JournalVoucher today; CashTransfer and the
    /// rest are mechanical follow-up -- see docs/phase-20d-status.md).</summary>
    private async Task<PrintableDocumentDto> BuildLedgerDocumentAsync<TDocument>(
        PrintDocumentQuery request,
        Organization organization,
        string templateName,
        TDocument document,
        Func<TDocument, (string Code, DateOnly Date, string? Reference, IEnumerable<(Guid AccountId, decimal Debit, decimal Credit)> Lines)> project,
        CancellationToken cancellationToken)
    {
        var header = project(document);

        var accountIds = header.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await db.Accounts.Where(x => accountIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);

        return new PrintableDocumentDto(
            request.DocumentType,
            header.Code,
            header.Date,
            header.Reference,
            organization.Name,
            organization.Address,
            organization.Phone,
            organization.Email,
            organization.PanNumber,
            organization.Website,
            null,
            null,
            templateName,
            null,
            header.Lines.Select(l => new PrintableGlLineDto(
                accounts.TryGetValue(l.AccountId, out var account) ? $"{account.Code} — {account.Name}" : "(unknown account)",
                l.Debit, l.Credit)).ToList(),
            header.Lines.Sum(l => l.Debit),
            null);
    }
}
