using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Domain.Common;
using ErpApp.Domain.Purchasing;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Purchasing.Queries.GetPurchaseBill;

public sealed class GetPurchaseBillQueryHandler(IAppDbContext db) : IRequestHandler<GetPurchaseBillQuery, PurchaseBillDetailDto>
{
    public async Task<PurchaseBillDetailDto> Handle(GetPurchaseBillQuery request, CancellationToken cancellationToken)
    {
        var purchaseBill = await db.PurchaseBills
            .Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == request.Id && x.OrganizationId == request.OrganizationId, cancellationToken)
            ?? throw new NotFoundException("Purchase bill not found.");

        IReadOnlyList<PostedGlLineDto>? glLines = null;

        if (purchaseBill.Status == PurchaseBillStatus.Approved)
        {
            var glEntry = await db.GlJournalEntries
                .Include(x => x.Lines)
                .SingleOrDefaultAsync(
                    x => x.SourceDocumentType == DocumentType.PurchaseBill && x.SourceDocumentId == purchaseBill.Id, cancellationToken);

            glLines = glEntry?.Lines.Select(x => new PostedGlLineDto(x.Id, x.AccountId, x.Debit, x.Credit)).ToList();
        }

        return new PurchaseBillDetailDto(
            purchaseBill.Id,
            purchaseBill.OrganizationId,
            purchaseBill.ContactId,
            purchaseBill.WarehouseId,
            purchaseBill.Code,
            purchaseBill.Date,
            purchaseBill.Reference,
            purchaseBill.SupplierInvoiceReference,
            purchaseBill.IsImport,
            purchaseBill.ImportCountry,
            purchaseBill.ImportDate,
            purchaseBill.ImportDocumentNo,
            purchaseBill.TdsTypeId,
            purchaseBill.TdsAmount,
            purchaseBill.Status,
            purchaseBill.ApprovedByUserId,
            purchaseBill.ApprovedAt,
            purchaseBill.CreatedAt,
            purchaseBill.ReferrerType,
            purchaseBill.ReferrerId,
            purchaseBill.DiscountPct,
            purchaseBill.GrandTotal,
            purchaseBill.Lines.Select(x => new PurchaseBillLineDto(
                x.Id, x.ProductId, x.Quantity, x.Rate, x.VatRate, x.DiscountPct, x.Amount, x.VatAmount, x.ExpenditureClassification)).ToList(),
            glLines);
    }
}
