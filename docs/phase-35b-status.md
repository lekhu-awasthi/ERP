# Phase 35b — the location dimension in the reports

## TL;DR

The report half of phase 35. **43 of 49 live report screens carry a Billing Location filter and
exactly one query in this codebase accepted one**, so this phase swept it onto the other 35 — but the
gate the kickoff named turned out to be two gates, not one: `GlJournalEntry` had no `LocationId`
*and neither did `StockMovement` or `StockLedgerEntry`*, all three for the same structural reason.
Decision B settles all three the same way: **an append-only fact row carries the billing location of
the document that created it, stamped at write time.**

**What shipped.** Three columns with backfill migrations across 11 and 8 producing types; the stamp
at 11 GL posting sites and 15 stock-ledger sites, with reversals inheriting rather than re-deriving;
the filter on **36 report queries, their handlers and 86 endpoint constructions**; five shared
readers threaded; the control on **43 report screens** through one new `app-report-location-filter`;
the LOCATION column on both Master reports; and `TenantSettings.LocationWiseReportPermission` — which
had one consumer for three phases — now governing every report, closing phase-32b carried item #1.

**Two things worth carrying past the phase.** First, the E2E taught me the permission model I had
written down wrong: a `Reports.*` key **cannot be granted per location at all**, so a report's
location scope is derived from the caller's *transaction* grants. The first negative script asserted
a grant shape the API refuses outright, and only running it found that. Second, I nearly shipped a
tenant leak: my first GL helper took a pre-filtered queryable, which left nine handlers each
responsible for remembering `OrganizationId` in a codebase with no global query filter.

**Deferred with the user, up front:** Product-to-location (its enforcement is unobserved and probing
it needs two writes on the live trial tenant) and the three Moonbeam-only filters (not the location
dimension). Both are carried items, not omissions.

Tests: Domain 443, Application **978** (+9), Api.IntegrationTests 18, Angular **291** (+8). Plus 55
E2E assertions against SQL Server and a browser pass.

---

## Decision B — the fact tables carry a location, and there were three of them

The kickoff framed one question: the live product filters Trial Balance, Balance Sheet, Income
Statement, the Journal report and all three General Ledger reports by Billing Location, and
`GlJournalEntry` has no `LocationId`. Route (a) stamp it, or route (b) join back across 11 source
types.

**Route (a).** Three reasons, in order of weight:

1. **All 11 posting sources already carry `LocationId`** (`DocumentMechanisms.LocationBearing`
   covers every one), so stamping is a *copy*, not a derivation. There is nothing to infer and
   nothing that can be inferred wrongly.
2. **A financial statement aggregates before it pages.** Phase 34c measured that the report layer's
   cost is the period, not the page size; an 11-way join from `GlLine` back to its document would
   therefore run over every entry in the window on every request, on nine reports.
   `GlSourceDocumentResolver` (phase 26a) is that shape, and it exists for *display*, where it runs
   over one page.
3. Filtering must happen *before* aggregation, so route (b) could not have been pushed to the edges
   the way a display join can.

**The part the kickoff did not anticipate.** `StockMovement` and `StockLedgerEntry` have exactly the
same shape — `(SourceDocumentType, SourceDocumentId)` and nothing else — and six inventory reports
carry the filter live. Discovering that turned Decision B from a judgement about the GL into a rule:

> An append-only fact row carries the billing location of the document that created it, stamped at
> write time.

Stating it that way is what made the second and third columns obvious rather than a second argument.

**What the rule owes, answered explicitly:**

- **A reversal inherits.** `GlJournalEntry.PostReversalOf` copies `original.LocationId`, and
  `StockLedgerService.ReverseIncrementAsync` takes `layer.LocationId` rather than a fresh argument.
  This is not tidiness: a void landing at a different location (or at none) leaves the original
  branch's Trial Balance permanently off by the document's value while the organization-wide total
  still balances — phase-6 bug #3 with the location as the axis instead of an account. Pinned by
  `A_void_reverses_at_the_same_location_so_the_branch_nets_to_zero`.
- **Opening balances** stamp from the `OpeningBalanceLine` / `OpeningStockLine` row, both of which
  are in `LocationBearing`.
- **Null is honest, in three distinct cases** that must not be conflated: an entry older than the
  backfill could reach, one whose document was raised while its type was outside the tenant's
  `LocationScopeMode`, and a tenant that has never had the entitlement. All three mean "no location",
  and a filter for a specific location excludes them, because a null never equals a value.
- **No index, deliberately.** Every report applies the location *alongside* its period, so the seek
  is the `(OrganizationId, PostedAt)` index `TenantIndexConvention` already derives and the location
  is a residual over a set the date range has narrowed. Phase 34c measured what a second index over
  the same table does to the paths that do not use it — a non-matching search went 1.8× slower — so
  adding one here would have to be justified by a measurement this phase did not take.

**The backfills are two lists, not one copied twice.** The GL has 11 producing types and the stock
tables have 8, and neither set contains the other: `JournalVoucher`, `CashTransfer`, `Expense` and
`Payment` post no stock, while `WarehouseTransfer` and `OpeningStock` post no GL.

---

## Decision C — who the filter reaches, and how the set was derived

The universe is the 2026-09-10 confirm-live census (all 49 report screens opened, every filter bar
read), not a sample. `ILocationFilteredReport` marks the 36 queries that take it;
`ReportLocationSweepGuardTests` derives the universe from the `Reports.` permission-key namespace, so
a report written later faces the question the moment it exists.

**Nine exemptions, in two kinds — and the distinction matters:**

| | why |
|---|---|
| VAT Summary, TDS, Annex 13 | IRD returns; no control live |
| Ratio Analysis, Exceptional | whole-organization analytics; no control live |
| User Log | login events — no document behind a row |
| Migrated Sales/Purchase Register | the imported row is deliberately not a document (phase 21c) and has no `LocationId`; the cutover spreadsheet has no location column either |
| System Audit | an `Audit` row is `(UserId, Action, DocumentType, DocumentId)` and nothing else |

The first six come from the census; the last three are this codebase's own and have a **stronger**
reason than the product's — the underlying row carries no location, so the filter could not be
implemented honestly without inventing a join to a document that may not exist.

**Annex 5 carries the filter despite being an IRD annex**, and is therefore not exempt. Recorded as
observed and not reconciled. This is precisely why the census read all 49 screens: a sample of one
report per family would have produced the tidy rule "statutory reports have no location" and been
wrong (phase-30's find-the-rule lesson, with phase-35a's correction that a complete enumeration is
not a sample).

### Exempt from the filter is not exempt from the scope

Ratio Analysis and the Exceptional Report have no Billing Location *control* live, so they take no
filter. They now honour the location **permission scope** anyway. The two are different mechanisms
for different reasons: one is a filter the user chooses, the other a restriction an Admin imposes
through `TenantSettings.LocationWiseReportPermission` — and a report with no filter is exactly the
kind that would otherwise hand a branch-scoped user an organization-wide figure.

---

## Decision D — the ageing reports filter the ageable documents, never the settlements

A branch invoice settled by a payment or a credit note raised at head office is still settled.
Filtering the settlement side too would show that invoice as outstanding on the branch's ageing while
the organization-wide report showed it paid, and the two reports would disagree about the same row.
Allocations and returns are already keyed to the filtered document ids, so they follow the narrowing
without being narrowed.

**A limit named rather than hidden:** `Contact.OpeningBalance` is a contact-level migration figure
that predates every document and belongs to no branch. A location-filtered contact balance is
therefore "opening carry-forward plus this branch's movements", not "this branch's share of the
opening". Nothing in the schema could make it the latter, and splitting one number across branches by
guesswork would be worse than saying so. Stated in code on all three contact reports, because the
symptom otherwise is a reader noticing that branch totals do not add up to the organization total.

---

## Decision E — two shared narrowings, because the two families share nothing else

- `GlEntryLocations.ForReport(db, organizationId, requested, scope)` — the nine GL reports. Returns
  an `IQueryable<GlJournalEntry>` the caller joins `GlLines` onto.
- `ReportLocationFilter.AtLocations(query, requested, scope)` — everything document-sourced, generic
  over `EF.Property<Guid?>(x, "LocationId")`.

**Why generic over `EF.Property` rather than one `Where` per type.** The seventeen location-bearing
aggregates share no interface, and a generic `x => x.LocationId` bakes in the *interface's*
MemberInfo, which EF cannot match to the concrete entity's mapped property (phase-2 bugs #1/#5).
`EF.Property` is the documented remedy, and it is not a new bet here: phase 33 already ships this
exact expression over this exact column for all fifteen document aggregates in
`GlobalSearchQueryHandler.ByCodeAsync`.

**Both compose `.Where()` calls rather than folding `scope == null || …` into one predicate** — the
codebase's stated shape, and the one immune to both mechanisms phase 35a measured.

**`ForReport` takes the organization, not a pre-filtered queryable** — see *What was found*, below.

---

## What was found on the way

### 1. The helper that would have leaked a tenant

My first version of `GlEntryLocations` took `IQueryable<GlJournalEntry>` and applied only the
location conditions, so each of the nine handlers kept `entry.OrganizationId == request.OrganizationId`
in its own join. Rewriting nine query-syntax joins to target a pre-filtered queryable means moving
that condition nine times, and **this codebase has no EF global query filter** — CLAUDE.md says so
explicitly: every handler filters by `OrganizationId` in LINQ, by hand. I caught it while patching
the first handler, having already removed the predicate from its `where`.

Folding the tenant filter into the helper's own signature makes the mistake impossible rather than
merely unlikely. The general form: **a helper that replaces a per-handler `Where` must own every
condition that `Where` carried, not just the interesting one.**

### 2. `Reports.*` keys cannot be granted per location — the scope comes from transaction grants

The first negative E2E granted `Reports.TrialBalance.View` **false organization-wide and true at BR1
only**, which is the grant shape phase 35a established as the only one that proves anything. The API
refused it with a 400:

> `'Reports.TrialBalance.View'` cannot be granted per location. Only transaction permissions are
> location-scoped; General, Settings and Reports permissions are organization-wide.

Which is what `LocationAccessScope.ForReportsAsync`'s own doc comment says, and what phase 32b
recorded after its confirm-live pass — I had read it and still written the test the other way,
because "grant the key at one location" is the shape every other location proof in this codebase
uses. The real model:

- the report key is held **organization-wide** (or the caller cannot open the report at all);
- the caller's **locations** are the ones their role holds *transaction* grants at
  (`AnyGrantedLocationsAsync`);
- `LocationWiseReportPermission` applies that set to report rows.

The corrected E2E grants `Sales.Invoice.View` false organization-wide and true at BR1, and proves a
clerk who asks for nothing sees only their branch.

### 3. A generated change handler that skipped pagination

The page sweep generated `onLocationChange` as "set the signal, reload". On the twenty paginated
report pages whose reload is `load()` rather than `reload()`, every *sibling* filter handler also
calls `this.page.set(1)` — because on those pages `reload()` is the one that resets and `load()` is
not. Without it, narrowing to a branch while on page 3 returns nothing, which reads as "this branch
has no data". Caught by reading what the neighbouring handlers do rather than by a test; the twenty
pages that needed it were separated from the five that do not page by asking each file, not by
listing.

### 4. Seeding traps, all of the same family

Four separate 400/403s in the E2E, each a field or route whose wrong form fails silently or
misleadingly:

- `POST /organizations` takes **`multipleLocations`**, not `multipleLocationsEnabled`. The wrong name
  binds to `false` and the failure surfaces three steps later as a 403 about the feature being
  disabled.
- `POST /billing-locations` **requires `address`** despite the API record defaulting it to null.
- The invite route is `POST /organizations/{id}/invitations` and returns **`membershipId`**, not `id`.
- `POST /products` needs the full record and `vatRate` is **`ThirteenPercentVat`**, not `Taxable`;
  a wrong enum member fails as "Failed to read parameter … as JSON" naming no field, exactly the
  phase-34b `AccountKind` trap.

---

## Tests

**`ReportLocationSweepGuardTests` (4)** — the universe derived from the `Reports.` key namespace,
asserted non-empty first (phase-34a's rule); every report query implements `ILocationFilteredReport`
or is exempt with a reason; no stale exemptions; the filter defaults to null; and every
location-filtered report's handler takes an `ICurrentUserService`, without which it could not read
the permission scope at all. That last one is deliberately a weaker claim than "the handler applies
it", which reflection cannot decide — the behaviour is pinned below.

**`ReportLocationFilterTests` (5)** — that the filter *bites*: a GL report narrowing three ways, the
stamp asserted on the row itself (so a test that passed because both entries were unfiltered would
not pass), a void netting a branch to zero, a document-sourced report, and the permission scope
narrowing with no filter requested.

**Angular (8)** — four for `ReportLocationFilter` (the "All locations" default, the emitted change,
the two reasons it hides, the label/control association) and four for the client sweep guard, whose
page set is derived from the router rather than the filesystem, so a page nobody can reach is not
counted as a page that ships. **The guard was made to fail** by deleting the control from
`journal-report-page.html` — two of its four tests went red — and the file was restored **by hash**,
never `git checkout --`, which would have reverted the phase's own work on it (phase-34a's rule).

---

## E2E — 55 assertions against SQL Server, all printed

The point of running these outside the unit suite: **InMemory evaluates `EF.Property` and a joined
sub-query in C#**, so both shared narrowings could pass 978 handler tests while all 43 endpoints 500.
Only a real provider settles it.

**Seed:** a fresh organization with MultipleLocations/MultipleWarehouses/TrackInventory on,
`LocationScopeMode = AllTransactions`, a chart of accounts (nothing seeds one), HeadOffice plus a new
BR1, and one approved Journal Voucher at each.

**`e2e-assert.sh`, 46 of 46:**

- **37 report endpoints return 200 with a location filter** — the translation check, covering all
  nine GL reports plus one per mechanism family.
- A feature-gated report (`production-summary`, on a tenant with no Manufacturing entitlement) still
  answers **403 on the feature**, not 200 and not 500 — a location filter must not change which
  refusal you get.
- Trial Balance: **1250 unfiltered, 1000 at HeadOffice, 250 at BR1**.
- Transaction List: 2 rows unfiltered, 1 at BR1 — the document-sourced path, which shares nothing
  with the GL path but the argument list.
- The **`.xlsx` export** answers 200 at both locations (phase-16c's second caller).
- `sqlcmd`: 1 GL entry stamped at BR1 and **0 with no location** — the backfill and the stamp, read
  from the table rather than through the API that wrote them.

**`e2e-negative.sh`, 9 of 9 — both directions on one member (phase-31 rule d):**

| | |
|---|---|
| clerk asks for no filter at all | **250** — their branch only |
| clerk filters to their own branch | 250 |
| clerk filters to HeadOffice | **0** |
| `sqlcmd` | the HeadOffice entry **exists** |
| the same request as Admin | **1000** |
| clerk opens Balance Sheet (no key) | **403**, not an empty report |
| clerk opens the invoice list at HeadOffice | 200 and **empty** — the list and the report agree |

The sqlcmd row and the Admin's 1000 are what make the clerk's empty answer a **refusal** rather than
an absent row.

**Browser (this phase's own UI only):** Trial Balance renders the **Billing Location** control with a
proper `<label for>`; switching it to *Branch One (BR1)* takes the total from **1,250.00 to 250.00**,
still balanced. The Sales Master Report renders the **LOCATION column** — phase-35a carried item #3 —
showing *Branch One* for an invoice raised there, and its filter narrows 1 row to 0 at HeadOffice and
back to 1 at BR1.

---

## Lessons

**(a) A shared helper must own every condition it replaced, not just the new one.** Extracting the
location narrowing out of nine handlers meant those handlers stopped writing their own `where` — and
the tenant filter lived in the same `where`. With no global query filter to catch a miss, taking the
organization as an argument is the difference between a mistake being impossible and being unlikely.

**(b) Read the mechanism's own doc comment before writing the test that proves it.** The report
location scope reads *transaction* grants, which `LocationAccessScope` states plainly and phase 32b
confirmed live. I wrote the negative test around the shape every other location proof uses, and the
API refused it. A convention that holds in fifteen places is not a rule; the one place it does not
hold is where the test belongs.

**(c) When a gate turns out to have the same shape somewhere else, find the rule before fixing the
instance.** The kickoff named `GlJournalEntry`. Stating the answer as "an append-only fact row
carries its document's location" made `StockMovement` and `StockLedgerEntry` follow immediately,
rather than being discovered later by a stock report that could not be filtered.

**(d) A generated handler is only as good as the handler beside it.** The page sweep wrote a correct
`onLocationChange` and an incomplete one, and the difference was visible only in what the *sibling*
filters on each page did about pagination. A sweep should copy its neighbours' behaviour, not a
template.

**(e) Two guards, because the two halves fail independently.** The server guard proves every report
query takes the filter; the client guard proves every report screen sends one. Phase 32 is the worked
example of why one is not enough — the Sales Master Report's query, DTO columns and `.xlsx` export
all carried `locationId` for three phases while its screen sent nothing and rendered no column.

---

## Carried items

1. **Product-to-location** (deferred with the user). What the selector restricts is unobserved: the
   Cadehi form has a checkbox multi-select, default *All*, not required, and the Products grid has no
   LOCATION column or filter. Separating "filters the product picker on a location-scoped document"
   from "stored and unenforced" needs **two writes on the live tenant** — a product scoped to one
   location, an invoice raised at another. Do not store a set nothing enforces (phase 31).
2. **System Audit has no location, and giving it one is a real decision.** Either stamp a location at
   audit-write time — where an Approve command does not carry one, so it would mean loading the row —
   or the 17-way join back that Decision B rejected for the GL. Exempt with that reason rather than
   guessed at.
3. **The three Moonbeam-only filters** (deferred with the user): Reporting Tags on the Journal report,
   Reporting Tags + group-by-warehouse on Inventory Position, Group By Bill / Include Credit Note on
   the Sales Register. Absent on Cadehi; re-read Moonbeam before building any of them.
4. **`sales-summary` carries a "Group Wise location" control** — a group-*by* no other report has.
   This phase gave it the filter, not the grouping.
5. **The Purchase Master Report's LOCATION column is inferred, not observed.** The live column list
   was recorded for the Sales Master Report only. The two are mirrors in every other column here and
   both carry the filter, so shipping the column on one and not the other would be an inconsistency a
   user would notice — but it is the one place this phase went past what the census settled, and it is
   named in the DTO itself.
6. **A report's location filter and a document's `LocationScopeMode` can disagree.** A document
   written while its type was out of scope carries no location, so it is invisible to every filtered
   report while still counting in the unfiltered one. Inherited from phase 32 and unchanged here;
   worth a decision if a tenant ever narrows the mode after trading (phase-35a carried item #5, still
   open and now visible in more places).
