using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Sales.Posting;
using ErpApp.Domain.Catalog;
using MediatR;

namespace ErpApp.Application.Sales.Queries.PreviewInvoiceGlPosting;

public sealed class PreviewInvoiceGlPostingQueryHandler(IAppDbContext db, IGlPostingRule<InvoicePostingInput> postingRule)
    : IRequestHandler<PreviewInvoiceGlPostingQuery, IReadOnlyList<GlLinePreviewDto>>
{
    public async Task<IReadOnlyList<GlLinePreviewDto>> Handle(PreviewInvoiceGlPostingQuery request, CancellationToken cancellationToken)
    {
        var lines = request.Lines.Select(x =>
        {
            var netAfterLineDiscount = x.Quantity * x.Rate * (1 - x.DiscountPct / 100m);
            var amount = netAfterLineDiscount * (1 - request.DiscountPct / 100m);
            return (x.ProductId, Amount: amount, VatAmount: amount * x.VatRate.ToPercent());
        });

        var postingInput = await InvoiceAccountResolver.ResolveAsync(
            db, request.OrganizationId, lines, resolveInventoryAccounts: false, cancellationToken);

        return postingRule.BuildLines(postingInput)
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();
    }
}
