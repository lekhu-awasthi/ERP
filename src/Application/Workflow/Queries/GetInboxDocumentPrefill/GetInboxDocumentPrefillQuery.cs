using ErpApp.Application.Common.DocumentExtraction;
using ErpApp.Application.Common.Exceptions;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Common;
using ErpApp.Domain.Contacts;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Workflow.Queries.GetInboxDocumentPrefill;

/// <summary>
/// The server-computed pre-fill a conversion hands to the target document's ordinary <c>new</c>
/// form (docs/phase-22-status.md, Decision B). Deliberately shaped like Phase 6's
/// <c>GetDebitNoteConversionTemplate</c>: the target page is unchanged apart from consuming one more
/// prefill source.
///
/// <para><b>Its permission key is the target type's own Create key</b>, resolved per request exactly
/// as <c>PrintDocumentQuery</c> resolves a View key. That is not a second, weaker gate in front of
/// the real one -- it is the same gate, moved one step earlier, so a user who could never save a
/// Purchase Bill cannot even obtain the prefill for one. It also means the inbox can never become a
/// side door around <c>AuthorizationBehavior</c>.</para>
///
/// <para><b>Every resolved id is an exact match or null.</b> A party name that does not match a
/// Contact exactly comes back as raw text with a null ContactId, and the screen shows the raw text
/// beside an empty picker rather than choosing a plausible neighbour. Fuzzy matching here would put
/// a wrong supplier on a bill with no visible sign anything was guessed.</para>
/// </summary>
public sealed record GetInboxDocumentPrefillQuery(Guid OrganizationId, Guid DocumentId, DocumentType TargetType)
    : IRequest<InboxPrefillDto>, IRequirePermission, IOrganizationScoped
{
    public string PermissionKey => InboxConversionTargets.CreatePermissionFor(TargetType);
}

/// <summary>
/// Target-agnostic on purpose -- one DTO for all four conversion targets, because they overlap
/// almost entirely at this level. See <c>InboxConversionTargets</c> for what a fifth target costs.
/// </summary>
/// <param name="HasExtraction">
/// False when no extraction ever ran (or it failed, or the tenant declined). The conversion still
/// proceeds: the user gets a blank form with the scan beside it, which is the whole base feature.
/// The screen uses this to decide whether to show the "these values were read by AI -- check them"
/// banner at all.
/// </param>
/// <param name="PartyNameRaw">What the document said, kept even when
/// <paramref name="ContactId"/> resolved, so the user can see what was matched against.</param>
public sealed record InboxPrefillDto(
    Guid DocumentId,
    string FileName,
    string ContentType,
    DocumentType TargetType,
    bool HasExtraction,
    string? ExtractionModelId,
    Guid? ContactId,
    string? PartyNameRaw,
    string? PartyPanRaw,
    DateOnly? Date,
    string? Reference,
    decimal? TotalAmount,
    decimal? VatAmount,
    IReadOnlyList<InboxPrefillLineDto> Lines);

/// <param name="ProductId">Null unless <paramref name="DescriptionRaw"/> matched a Product's Code or
/// Name exactly. A null here is a line the user must complete by hand.</param>
public sealed record InboxPrefillLineDto(
    Guid? ProductId,
    string? DescriptionRaw,
    decimal? Quantity,
    decimal? Rate,
    decimal? Amount);

public sealed class GetInboxDocumentPrefillQueryValidator : AbstractValidator<GetInboxDocumentPrefillQuery>
{
    public GetInboxDocumentPrefillQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.TargetType)
            .Must(InboxConversionTargets.IsSupported)
            .WithMessage("That document type cannot be created from the Document inbox.");
    }
}

public sealed class GetInboxDocumentPrefillQueryHandler(IAppDbContext db)
    : IRequestHandler<GetInboxDocumentPrefillQuery, InboxPrefillDto>
{
    public async Task<InboxPrefillDto> Handle(
        GetInboxDocumentPrefillQuery request, CancellationToken cancellationToken)
    {
        var document = await db.UploadedDocuments
            .Where(x => x.Id == request.DocumentId && x.OrganizationId == request.OrganizationId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Document not found.");

        if (document.IsLinked)
        {
            throw new ConflictException(
                "This document has already been converted into a transaction. Upload the file again if you need a second one.");
        }

        var data = InboxDocumentMapper.Deserialize(document.ExtractedDataJson);

        if (data is null)
        {
            return new InboxPrefillDto(
                document.Id, document.FileName, document.ContentType, request.TargetType,
                HasExtraction: false, ExtractionModelId: null,
                ContactId: null, PartyNameRaw: null, PartyPanRaw: null,
                Date: null, Reference: null, TotalAmount: null, VatAmount: null, Lines: []);
        }

        var contactId = await ResolveContactAsync(request, data, cancellationToken);
        var lines = await ResolveLinesAsync(request.OrganizationId, data, cancellationToken);

        return new InboxPrefillDto(
            document.Id,
            document.FileName,
            document.ContentType,
            request.TargetType,
            HasExtraction: true,
            document.ExtractionModelId,
            contactId,
            data.PartyName,
            data.PartyPan,
            data.DocumentDate,
            data.Reference,
            data.TotalAmount,
            data.VatAmount,
            lines);
    }

    /// <summary>
    /// PAN first, then exact name. PAN is the stronger signal -- it is unique by construction in
    /// Nepal, whereas two Contacts can share a trading name -- and an exact-name match that is
    /// ambiguous resolves to nothing rather than to the first row, which would silently pick a
    /// supplier by insertion order.
    ///
    /// <para>The candidate set is narrowed by <see cref="ContactType"/> to match the target
    /// document: a sales Invoice looks only at Customers, a purchase document only at Suppliers.
    /// A Payment looks at both, because Quick Payment/Quick Receipt is one screen serving both
    /// directions. Nothing here ever creates a Contact -- an unmatched party is shown as raw text
    /// so a human decides.</para>
    /// </summary>
    private async Task<Guid?> ResolveContactAsync(
        GetInboxDocumentPrefillQuery request, ExtractedDocumentData data, CancellationToken cancellationToken)
    {
        var pan = Trimmed(data.PartyPan);
        var name = Trimmed(data.PartyName);

        if (pan is null && name is null)
        {
            return null;
        }

        var candidates = db.Contacts.Where(x => x.OrganizationId == request.OrganizationId && x.IsActive);

        candidates = request.TargetType switch
        {
            DocumentType.Invoice => candidates.Where(x => x.Type == ContactType.Customer),
            DocumentType.PurchaseBill or DocumentType.Expense => candidates.Where(x => x.Type == ContactType.Supplier),
            // Quick Payment/Quick Receipt is one screen for both directions -- see QuickPaymentPage.
            DocumentType.Payment => candidates.Where(
                x => x.Type == ContactType.Customer || x.Type == ContactType.Supplier),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request), request.TargetType, "Not a supported Document inbox conversion target."),
        };

        if (pan is not null)
        {
            var byPan = await candidates.Where(x => x.Pan == pan).Select(x => (Guid?)x.Id).Take(2).ToListAsync(cancellationToken);
            if (byPan.Count == 1)
            {
                return byPan[0];
            }
        }

        if (name is not null)
        {
            var byName = await candidates.Where(x => x.Name == name).Select(x => (Guid?)x.Id).Take(2).ToListAsync(cancellationToken);
            if (byName.Count == 1)
            {
                return byName[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Matches each extracted line description against an active Product's Code or Name, exactly.
    /// Loads the (typically small) candidate set once and matches in memory rather than issuing a
    /// query per line -- a scanned bill has a handful of lines, and the alternative is N round trips.
    /// An ambiguous description resolves to null, for the same reason an ambiguous party does.
    /// </summary>
    private async Task<IReadOnlyList<InboxPrefillLineDto>> ResolveLinesAsync(
        Guid organizationId, ExtractedDocumentData data, CancellationToken cancellationToken)
    {
        if (data.Lines.Count == 0)
        {
            return [];
        }

        var descriptions = data.Lines
            .Select(x => Trimmed(x.Description))
            .Where(x => x is not null)
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var matches = descriptions.Count == 0
            ? []
            : await db.Products
                .Where(x => x.OrganizationId == organizationId
                    && x.IsActive
                    && (descriptions.Contains(x.Code) || descriptions.Contains(x.Name)))
                .Select(x => new { x.Id, x.Code, x.Name })
                .ToListAsync(cancellationToken);

        return
        [
            .. data.Lines.Select(line =>
            {
                var description = Trimmed(line.Description);
                Guid? productId = null;

                if (description is not null)
                {
                    var candidates = matches
                        .Where(m => string.Equals(m.Code, description, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(m.Name, description, StringComparison.OrdinalIgnoreCase))
                        .Select(m => m.Id)
                        .Distinct()
                        .Take(2)
                        .ToList();

                    if (candidates.Count == 1)
                    {
                        productId = candidates[0];
                    }
                }

                return new InboxPrefillLineDto(productId, description, line.Quantity, line.Rate, line.Amount);
            }),
        ];
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
