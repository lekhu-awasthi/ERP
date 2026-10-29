using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Purchasing.Posting;
using ErpApp.Domain.Catalog;
using ErpApp.Domain.Purchasing;
using MediatR;

namespace ErpApp.Application.Purchasing.Queries.PreviewExpenseGlPosting;

public sealed class PreviewExpenseGlPostingQueryHandler(IAppDbContext db, IGlPostingRule<ExpensePostingInput> postingRule)
    : IRequestHandler<PreviewExpenseGlPostingQuery, IReadOnlyList<GlLinePreviewDto>>
{
    public async Task<IReadOnlyList<GlLinePreviewDto>> Handle(PreviewExpenseGlPostingQuery request, CancellationToken cancellationToken)
    {
        // Phase 99 -- the VAT that is not credit goes where Approve sends it.
        var lines = request.Lines.Select(x =>
        {
            var vatAmount = x.Amount * x.VatRate.ToPercent();
            return (
                x.AccountId,
                Amount: x.Amount,
                VatAmount: vatAmount,
                DisallowedVat: VatCredit.DisallowedAtPosting(vatAmount, x.CreditUse, x.CreditCategory));
        }).ToList();

        var tdsBaseAmount = request.Lines.Sum(x => x.Amount);
        var tdsAmount = request.TdsApplicable
            ? await PurchasingValidation.ResolveTdsAmountAsync(db, request.OrganizationId, request.TdsTypeId, tdsBaseAmount, cancellationToken)
            : 0;

        var postingInput = await ExpenseAccountResolver.ResolveAsync(db, request.OrganizationId, lines, tdsAmount, cancellationToken);

        return postingRule.BuildLines(postingInput)
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();
    }
}
