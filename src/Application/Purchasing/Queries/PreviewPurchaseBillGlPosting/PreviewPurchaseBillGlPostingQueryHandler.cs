using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PreviewPurchaseBillGlPosting;

public sealed class PreviewPurchaseBillGlPostingQueryHandler(
    IAppDbContext db, IGlPostingRule<PurchaseBillPostingInput> postingRule)
    : IRequestHandler<PreviewPurchaseBillGlPostingQuery, IReadOnlyList<GlLinePreviewDto>>
{
    public async Task<IReadOnlyList<GlLinePreviewDto>> Handle(
        PreviewPurchaseBillGlPostingQuery request, CancellationToken cancellationToken)
    {
        var lines = request.Lines.Select(x =>
        {
            var amount = x.Quantity * x.Rate;
            return (x.ProductId, Amount: amount, VatAmount: amount * x.VatRate.ToPercent());
        }).ToList();

        var tdsBaseAmount = request.Lines.Sum(x => x.Quantity * x.Rate);
        var tdsAmount = await PurchasingValidation.ResolveTdsAmountAsync(
            db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken);

        var postingInput = await PurchaseBillAccountResolver.ResolveAsync(db, request.OrganizationId, lines, tdsAmount, cancellationToken);

        return postingRule.BuildLines(postingInput)
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();
    }
}
