using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Payments.Posting;
using ErpApp.Domain.Common;
using MediatR;

namespace ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;

public sealed class PreviewPaymentGlPostingQueryHandler(IAppDbContext db, IGlPostingRule<PaymentPostingInput> postingRule)
    : IRequestHandler<PreviewPaymentGlPostingQuery, IReadOnlyList<GlLinePreviewDto>>
{
    public async Task<IReadOnlyList<GlLinePreviewDto>> Handle(PreviewPaymentGlPostingQuery request, CancellationToken cancellationToken)
    {
        var (currencyCode, exchangeRate) = ExchangeRates.Validate(request.CurrencyCode, request.ExchangeRate);
        var draft = new PaymentGlDraft(
            request.AccountId, request.Amount, request.Direction, currencyCode, exchangeRate,
            [.. request.Allocations.Select(x => new PaymentGlAllocation(x.TargetDocumentType, x.TargetDocumentId, x.Amount))]);

        var plan = await PaymentGlLines.BuildAsync(db, postingRule, request.OrganizationId, draft, cancellationToken);

        return plan.Lines
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();
    }
}
