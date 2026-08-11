using ErpApp.Application.Accounting.Posting;
using ErpApp.Domain.Accounting;
using MediatR;

namespace ErpApp.Application.Accounting.Queries.PreviewGlPosting;

public sealed class PreviewGlPostingQueryHandler(IGlPostingRule<JournalVoucher> postingRule)
    : IRequestHandler<PreviewGlPostingQuery, IReadOnlyList<GlLinePreviewDto>>
{
    public Task<IReadOnlyList<GlLinePreviewDto>> Handle(PreviewGlPostingQuery request, CancellationToken cancellationToken)
    {
        var journalVoucher = JournalVoucher.Create(request.OrganizationId, request.Date, request.Reference);
        foreach (var line in request.Lines)
        {
            journalVoucher.AddLine(line.AccountId, line.Debit, line.Credit);
        }

        IReadOnlyList<GlLinePreviewDto> glLines = postingRule.BuildLines(journalVoucher)
            .Select(x => new GlLinePreviewDto(x.AccountId, x.Debit, x.Credit))
            .ToList();

        return Task.FromResult(glLines);
    }
}
