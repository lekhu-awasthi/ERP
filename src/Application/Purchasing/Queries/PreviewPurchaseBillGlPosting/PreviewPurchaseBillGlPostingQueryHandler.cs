using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PreviewPurchaseBillGlPosting;

public sealed class PreviewPurchaseBillGlPostingQueryHandler(
    IAppDbContext db, IGlPostingRule<PurchaseBillPostingInput> postingRule)
    : IRequestHandler<PreviewPurchaseBillGlPostingQuery, IReadOnlyList<GlLinePreviewDto>>
{
    public async Task<IReadOnlyList<GlLinePreviewDto>> Handle(
        PreviewPurchaseBillGlPostingQuery request, CancellationToken cancellationToken)
    {
        // Phase 99 -- the line's credit terms resolved as Create resolves them, so the preview posts what Approve will.
        var categories = await PurchaseLineVatCredit.CategoriesAsync(
            db, request.OrganizationId, [.. request.Lines.Select(x => (x.ProductId, x.CreditCategory))], cancellationToken);
        var lines = request.Lines.Select((x, i) =>
        {
            var netAfterLineDiscount = x.Quantity * x.Rate * (1 - x.DiscountPct / 100m);
            var amount = netAfterLineDiscount * (1 - request.DiscountPct / 100m);
            var vatAmount = amount * x.VatRate.ToPercent();
            return (
                x.ProductId,
                Amount: amount,
                VatAmount: vatAmount,
                DisallowedVat: VatCredit.DisallowedAtPosting(vatAmount, x.CreditUse ?? VatCreditUse.Taxable, categories[i]));
        }).ToList();

        var tdsBaseAmount = request.Lines.Sum(
            x => x.Quantity * x.Rate * (1 - x.DiscountPct / 100m) * (1 - request.DiscountPct / 100m));
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        var postingInput = await PurchaseBillAccountResolver.ResolveAsync(db, request.OrganizationId, lines, tdsAmount, cancellationToken);

        return postingRule.BuildLines(postingInput)
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();
    }
}
