using ErpApp.Application.Accounting.Posting;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Payments.Posting;
using MediatR;

namespace ErpApp.Application.Payments.Queries.PreviewPaymentGlPosting;

public sealed class PreviewPaymentGlPostingQueryHandler(IAppDbContext db, IGlPostingRule<PaymentPostingInput> postingRule)
    : IRequestHandler<PreviewPaymentGlPostingQuery, IReadOnlyList<GlLinePreviewDto>>
{
    public async Task<IReadOnlyList<GlLinePreviewDto>> Handle(PreviewPaymentGlPostingQuery request, CancellationToken cancellationToken)
    {
        var postingInput = await PaymentAccountResolver.ResolveAsync(
            db, request.OrganizationId, request.AccountId, request.Amount, cancellationToken);

        return postingRule.BuildLines(postingInput)
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();
    }
}
