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
            var amount = x.Quantity * x.Rate;
            return (x.ProductId, Amount: amount, VatAmount: amount * x.VatRate.ToPercent());
        });

        var postingInput = await InvoiceAccountResolver.ResolveAsync(db, request.OrganizationId, lines, cancellationToken);

        return postingRule.BuildLines(postingInput)
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();
    }
}
