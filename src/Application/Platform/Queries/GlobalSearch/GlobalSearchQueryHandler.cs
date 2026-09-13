using ErpApp.Application.Common.Locations;
using ErpApp.Application.Common.Persistence;
using ErpApp.Application.Common.Security;
using ErpApp.Domain.Accounting;
using ErpApp.Domain.Common;
using ErpApp.Domain.Inventory;
using ErpApp.Domain.Manufacturing;
using ErpApp.Domain.Payments;
using ErpApp.Domain.Purchasing;
using ErpApp.Domain.Sales;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace ErpApp.Application.Platform.Queries.GlobalSearch;

public sealed class GlobalSearchQueryHandler(IAppDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<GlobalSearchQuery, IReadOnlyList<GlobalSearchHitDto>>
{
    /// <summary>
    /// How many hits any one collection may contribute. A per-collection cap rather than one global
    /// budget consumed in order, because a global budget makes the result depend on which collection
    /// happens to be queried first: a term matching twenty contacts would silently hide the invoice
    /// whose number is exactly that term, which is the single most valuable hit a search can return.
    /// </summary>
    public const int PerCollectionLimit = 5;

    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    /// <summary>
    /// One document hit before it knows what type it is. <paramref name="SubKind"/> is null for all
    /// but Payment -- see <see cref="PaymentsByCodeAsync"/> for the one case that needs it.
    /// </summary>
    private sealed record CodeHit(Guid Id, string Code, string? SubKind = null);

    public async Task<IReadOnlyList<GlobalSearchHitDto>> Handle(
        GlobalSearchQuery request, CancellationToken cancellationToken)
    {
        var term = request.Term.Trim();
        var limit = Math.Clamp(request.Limit ?? DefaultLimit, 1, MaxLimit);

        // The validator already refuses a term this short. Re-checked here because a blank term
        // against `Contains` matches every row in the tenant, and a handler should not depend on a
        // validator for a result that catastrophic.
        if (term.Length < GlobalSearchQueryValidator.MinimumTermLength)
        {
            return [];
        }

        var granted = await GrantedPermissionReader.GrantedKeysAsync(
            db, request.OrganizationId, currentUser.UserId, cancellationToken);

        var hits = new List<GlobalSearchHitDto>();

        // Phase 39. With one collection asked for, the per-collection cap and the total are the same
        // number, so the cap becomes the caller's limit -- that is what makes the results page able
        // to show more than five of anything, which is the whole reason it exists.
        var perCollection = request.Collection is null ? PerCollectionLimit : limit;

        bool Wants(GlobalSearchCollection collection) =>
            request.Collection is null || request.Collection == collection;

        // ---- Master data: matched on name OR code -------------------------------------------
        // These three are the reference product's own `collection` values (confirmed live), and none
        // of them carries a LocationId -- a contact belongs to the tenant, not to a branch -- so the
        // location scope further down applies to documents only.

        if (Wants(GlobalSearchCollection.Contact) && granted.Contains(PermissionKeys.ContactView))
        {
            var rows = await db.Contacts
                .Where(x => x.OrganizationId == request.OrganizationId
                            && x.IsActive
                            && (x.Name.Contains(term) || x.Code.Contains(term)))
                .OrderBy(x => x.Name)
                .Take(perCollection)
                .Select(x => new { x.Id, x.Code, x.Name, Kind = x.Type })
                .ToListAsync(cancellationToken);

            hits.AddRange(rows.Select(x => new GlobalSearchHitDto(
                GlobalSearchCollection.Contact, null, x.Id, x.Code, x.Name, x.Kind.ToString())));
        }

        if (Wants(GlobalSearchCollection.Product) && granted.Contains(PermissionKeys.ProductView))
        {
            var rows = await db.Products
                .Where(x => x.OrganizationId == request.OrganizationId
                            && x.IsActive
                            && (x.Name.Contains(term) || x.Code.Contains(term)))
                .OrderBy(x => x.Name)
                .Take(perCollection)
                .Select(x => new { x.Id, x.Code, x.Name, Kind = x.Type })
                .ToListAsync(cancellationToken);

            hits.AddRange(rows.Select(x => new GlobalSearchHitDto(
                GlobalSearchCollection.Product, null, x.Id, x.Code, x.Name, x.Kind.ToString())));
        }

        if (Wants(GlobalSearchCollection.Account) && granted.Contains(PermissionKeys.AccountView))
        {
            var rows = await db.Accounts
                .Where(x => x.OrganizationId == request.OrganizationId
                            && (x.Name.Contains(term) || x.Code.Contains(term)))
                .OrderBy(x => x.Name)
                .Take(perCollection)
                .Select(x => new { x.Id, x.Code, x.Name, Kind = x.RootType })
                .ToListAsync(cancellationToken);

            hits.AddRange(rows.Select(x => new GlobalSearchHitDto(
                GlobalSearchCollection.Account, null, x.Id, x.Code, x.Name, x.Kind.ToString())));
        }

        // ---- Documents: matched on code alone ------------------------------------------------
        // The applicable set is DocumentMechanisms.Transactional -- "everything with a Draft/Approve
        // lifecycle, a document number and a detail page", which is precisely the set a search by
        // document number can be about. That is phase-30's lesson taken deliberately: the rule, not
        // a sample. GlobalSearchSweepGuardTests fails the build if the switch below drifts from it.
        // Phase 39 -- an empty set rather than a guard clause around the loop, so the fan-out
        // disappears without re-indenting eighteen queries' worth of body.
        IReadOnlyList<DocumentType> documentTypes =
            Wants(GlobalSearchCollection.Document) ? DocumentMechanisms.Transactional : [];

        foreach (var documentType in documentTypes)
        {
            var key = DocumentPermissions.ViewPermissionFor(documentType);

            // Phase 32b's two halves, in order. Organization-wide first: holding the key outright
            // means every location and costs no further query -- which is every Admin and every role
            // on a tenant that has never opened the Location-specific section. Otherwise ask which
            // locations this caller holds it at; ForKeyAsync returning null here means they hold it
            // nowhere at all, so the type is skipped and its documents are unfindable.
            IReadOnlyList<Guid>? allowedLocations = null;

            if (!granted.Contains(key))
            {
                allowedLocations = await LocationAccessScope.ForKeyAsync(
                    db, currentUser, request.OrganizationId, key, cancellationToken);

                if (allowedLocations is null)
                {
                    continue;
                }
            }

            var rows = await SearchDocumentsAsync(
                request.OrganizationId, documentType, term, allowedLocations, perCollection, cancellationToken);

            hits.AddRange(rows.Select(x => new GlobalSearchHitDto(
                GlobalSearchCollection.Document, documentType, x.Id, x.Code, null, x.SubKind)));
        }

        return hits
            .OrderBy(x => x.Collection)
            .ThenBy(x => x.Name ?? x.Code, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Maps a document type onto the table its numbers live in. One arm per type rather than a
    /// dictionary of <c>Func</c>s: CLAUDE.md's standing gotcha (phase-9 bug #1) is that a captured
    /// delegate reaching <c>.Where()</c> compiles, passes every InMemory test, and then fails to
    /// translate against real SQL Server. What crosses the boundary here is a type argument, which
    /// is resolved at compile time and cannot.
    /// </summary>
    private Task<List<CodeHit>> SearchDocumentsAsync(
        Guid organizationId,
        DocumentType documentType,
        string term,
        IReadOnlyList<Guid>? allowedLocations,
        int perCollection,
        CancellationToken cancellationToken) => documentType switch
        {
            DocumentType.Quotation => ByCodeAsync<Quotation>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.SalesOrder => ByCodeAsync<SalesOrder>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.Invoice => ByCodeAsync<Invoice>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.CreditNote => ByCodeAsync<CreditNote>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.Payment => PaymentsByCodeAsync(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.PurchaseOrder => ByCodeAsync<PurchaseOrder>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.PurchaseBill => ByCodeAsync<PurchaseBill>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.Expense => ByCodeAsync<Expense>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.DebitNote => ByCodeAsync<DebitNote>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.JournalVoucher => ByCodeAsync<JournalVoucher>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.CashTransfer => ByCodeAsync<CashTransfer>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.WarehouseTransfer => ByCodeAsync<WarehouseTransfer>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.InventoryAdjustment => ByCodeAsync<InventoryAdjustment>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.ProductionOrder => ByCodeAsync<ProductionOrder>(organizationId, term, allowedLocations, perCollection, cancellationToken),
            DocumentType.ProductionJournal => ByCodeAsync<ProductionJournal>(organizationId, term, allowedLocations, perCollection, cancellationToken),

            // Unreachable: the caller iterates DocumentMechanisms.Transactional, and the guard test
            // pins that this switch covers it exactly.
            _ => throw new ArgumentOutOfRangeException(
                nameof(documentType), documentType,
                "Not a transactional document type -- see DocumentMechanisms.Transactional."),
        };

    /// <summary>
    /// Payment is the one type that cannot go through the generic query, because
    /// <see cref="GlobalSearchHitDto.DocumentType"/> alone does not say where the row lives: a
    /// received payment and a paid one are one aggregate here but two detail screens in the client
    /// (<c>/payments/{id}</c> and <c>/purchasing/supplier-payments/{id}</c>). So this arm carries the
    /// direction out in <see cref="GlobalSearchHitDto.SubKind"/>, which is that field's own purpose
    /// -- the row's sub-classification -- rather than a new column for one type.
    ///
    /// <para>Everything else about it is identical to <see cref="ByCodeAsync{TDocument}"/>, including
    /// composing the location filter rather than folding it into one predicate.</para>
    /// </summary>
    private async Task<List<CodeHit>> PaymentsByCodeAsync(
        Guid organizationId,
        string term,
        IReadOnlyList<Guid>? allowedLocations,
        int perCollection,
        CancellationToken cancellationToken)
    {
        var query = db.Payments
            .Where(x => x.OrganizationId == organizationId && x.Code.Contains(term));

        if (allowedLocations is not null)
        {
            var ids = allowedLocations.ToList();
            query = query.Where(x => x.LocationId != null && ids.Contains(x.LocationId.Value));
        }

        var rows = await query
            .OrderBy(x => x.Code)
            .Take(perCollection)
            .Select(x => new { x.Id, x.Code, x.Direction })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(x => new CodeHit(x.Id, x.Code, x.Direction.ToString()))];
    }

    /// <summary>
    /// The one query, generic over the fifteen aggregates. Properties are read through
    /// <see cref="EF.Property{T}"/> because the aggregates share no interface -- phase-2 bugs #1/#5's
    /// documented remedy for a handler generic over a constrained type, and the reason the constraint
    /// here is only <c>class</c>. <see cref="PropertyNames"/> pins the three names against a guard
    /// test, so a rename that would otherwise surface as a runtime translation failure breaks the
    /// build instead.
    ///
    /// <para>The location filter is applied by <b>composing a second Where</b>, never by folding a
    /// <c>allowedLocations is null</c> test into one predicate. An expression tree does not
    /// short-circuit, so the folded form would hand EF a null list to translate <c>Contains</c>
    /// against -- which throws during translation rather than returning everything, and only on the
    /// path where the caller is unrestricted. Composing matches every phase-32b list handler.</para>
    /// </summary>
    private async Task<List<CodeHit>> ByCodeAsync<TDocument>(
        Guid organizationId,
        string term,
        IReadOnlyList<Guid>? allowedLocations,
        int perCollection,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        var query = db.Set<TDocument>()
            .Where(x => EF.Property<Guid>(x, PropertyNames.OrganizationId) == organizationId
                        && EF.Property<string>(x, PropertyNames.Code).Contains(term));

        if (allowedLocations is not null)
        {
            // A location-restricted caller sees only rows carrying one of their locations. A row with
            // no location at all stays hidden from them, matching AuthorizationBehavior's own
            // LocationScopeOutcome.NoLocation branch: there is nothing a location grant can cover.
            var ids = allowedLocations.ToList();

            query = query.Where(x =>
                EF.Property<Guid?>(x, PropertyNames.LocationId) != null
                && ids.Contains(EF.Property<Guid?>(x, PropertyNames.LocationId)!.Value));
        }

        return await query
            .OrderBy(x => EF.Property<string>(x, PropertyNames.Code))
            .Take(perCollection)
            .Select(x => new CodeHit(
                EF.Property<Guid>(x, PropertyNames.Id),
                EF.Property<string>(x, PropertyNames.Code)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// The four property names <see cref="ByCodeAsync{TDocument}"/> reads by string. Declared against
    /// <see cref="Invoice"/> with <c>nameof</c> so they are at least one real type's real members,
    /// and pinned across all fifteen by <c>GlobalSearchSweepGuardTests</c>.
    /// </summary>
    public static class PropertyNames
    {
        public const string Id = nameof(Invoice.Id);
        public const string Code = nameof(Invoice.Code);
        public const string OrganizationId = nameof(Invoice.OrganizationId);
        public const string LocationId = nameof(Invoice.LocationId);
    }
}
