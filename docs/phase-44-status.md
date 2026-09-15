# Phase 44 — report semantics, read live first

## TL;DR

The phase whose premise was that its controls had never been operated. Six live reads on Moonbeam
and Cadehi (2026-09-15), **three of which overturned a decision this codebase had written down**:

1. *Display Warehouse in Column* is not an alternative to *Group by Warehouse* — it is `disabled`
   until that one is ticked, and the pair turns the per-warehouse split from rows into **quantity
   columns**. Phase 36 could not tell them apart on a tenant it believed had one warehouse; Moonbeam
   has four.
2. **Reporting Tags are OR *within* a category and AND *across* categories.** Phase 19 chose
   "any-of across everything" as a judgement call and phase 36 kept it explicitly because no tenant
   had two categories' worth of tagged data. Moonbeam has six. Measured four ways; the inherited
   rule is wrong and is now replaced.
3. **Inventory Master's Txn Type filter offers Opening Balance and Warehouse Transfer**, which
   phase 26c's Decision D excluded. 26c predicted the exact shape — two rows per transfer, every
   money column blank — and treated that as the reason to leave it out. The product ships it anyway.

Phase 43's Decision D is applied in full: the **Purchase Register, Purchase Return Register, VAT
Summary and Annex 13** now report base currency, folded per line before the bucketing, in the shared
readers, in memory after `ToListAsync`. Four partition tests, and an E2E on a real USD document chain.

Three roadmap items shipped beside them: **System Audit gains a location** (stamped by
`AuditBehavior`, closing the last of the 43 report screens), a **`BackfillDocumentLocationsCommand`**
for the documents written while their type was out of scope, and the **printed header's centred
arrangement** (39 #3).

**The phase's own find:** the Sales Register and the Purchase Register have *accepted* a
`LocationId` since phase 35b and never applied it to their primary document half — only the return
half narrowed. Picking a location dropped the credit/debit notes and left every invoice and bill in
a statutory register. `ReportLocationSweepGuardTests` could not see it, because it asks whether the
*query record* accepts a filter, not whether the handler applies one.

Tests: Domain 666 (unchanged), Application **1103 → 1130**, Api.IntegrationTests 29, Angular 447.

---

## The live reads

Read-only on `moonbeamtradingandsuppliers.tigguat.com` and `cadehi.tigg.app`, 2026-09-15. The user
logged in; no credentials were entered by the agent and none are recorded here. Nothing was saved on
either tenant — only filters were operated.

### A. Display Warehouse in Column vs Group by Warehouse (36 #2, Decision K)

Inventory Position's drawer: Period / Products / **Show Columns** / View Options / Reporting Tags.
The heading "Show Columns" over both checkboxes is itself a fact phase 36 did not record.

**`Display Warehouse in Column` is `disabled` until `Group by Warehouse` is ticked**
(`input.disabled === true`, wrapper `ant-checkbox-wrapper-disabled`, `pointer-events: none`). They
are a parent and its modifier, not two alternatives — which is precisely the question phase 36 could
not answer and named as its reason for leaving the second unbuilt.

| Run | Rows | Header | Totals |
|---|---|---|---|
| neither | 319 | `Code/Goods, Category, Qty, UOM, Rate, Amount` | Qty 7,654,351,724.888 / Amount 30,687,004.936 |
| Group by Warehouse only | 319 | **identical** | **identical** |
| both | 319 | `Code/Goods, Category, Kathmandu, Patan, Lalitpur, default, Qty, UOM, Rate, Amount` | identical |

Moonbeam has **four** warehouses. One **quantity** column per warehouse is inserted after Category;
Qty becomes the signed total across them (`cream (23)`: Kathmandu (10) + Patan (6) = Qty (16)); Rate
and Amount stay single — there is no per-warehouse value column.

**A live defect, observed and deliberately not copied:** with the columns on, the body rows have ten
cells and the `Total` footer still has six, so its figures sit under the wrong headers. This
codebase's footer spans the warehouse columns instead.

### B. Reporting Tags — the inherited rule is wrong

Moonbeam has six tag categories (BUSINESS, SERVICE, Vehicle, DSPL, DTRG, Import). BUSINESS holds
{BUSINESS, asdasd}; SERVICE holds {sdfsdfds}.

| Selection | Rows | Set |
|---|---|---|
| none | 319 | all |
| BUSINESS{BUSINESS} | 3 | P0163, P0396, P0325 |
| BUSINESS{BUSINESS, asdasd} | 4 | P0163, P0396, P0325, **P0596** |
| BUSINESS{BUSINESS} + SERVICE{sdfsdfds} | **2** | P0163, P0396 |
| BUSINESS{both} + SERVICE{sdfsdfds} | 3 | P0163, P0396, P0596 |

Adding an option *within* a category **grew** the set (3 → 4, exactly the union), which AND-within
cannot do. Adding a *second category* **shrank** it to a strict subset (3 → 2, and 4 → 3), which
OR-across cannot do. So: **OR within, AND across** — standard faceted filtering, and not what phases
19 and 36 implemented.

This is phase-32b's precedent (a confirm-live pass can falsify an earlier one) and the reason the old
rule survived two phases is that it was recorded as *inherited* rather than as observed.

UI note: a category's dropdown has SELECT ALL / DESELECT ALL, but its **APPLY is disabled when the
selection is empty** — *Reset to default* is the control that clears a category back to All.

### C. Do tags and group-by-warehouse compose? Yes

BUSINESS{both} + both Show Columns boxes → the tag filter's 4 rows rendered *with* the four warehouse
columns. Both apply; neither wins.

### D. Journal report — Reporting Tags (35b #3)

Drawer: Period / Transaction Type / the same six tag categories. 233 entries → **2** with
BUSINESS{BUSINESS}. Document-level, already built. The report groups by document (a colspan-4 title
row, its GL lines, a per-entry Total) and its pager counts **entries**, not rows.

### E. Sales Register — Group By Bill / Include Credit Note (35b #3)

Drawer: Period + View Options{Include Credit Note In Calculation, Group By Bill}, both ticked by
default. **No Reporting Tags section** — this codebase's `SalesRegisterQuery` has carried
`TagOptionIds` since phase 19; recorded as a divergence, not acted on.

| Run | Rows | Footer |
|---|---|---|
| both ticked | 33 | (93,831,004,212,410.17) / (93,831,004,364,803.66) / 152,393.49 / 19,811.17 |
| Group By Bill OFF | 58 | **identical** |
| Include Credit Note OFF | 20 | 472,316.46 / 250,236.35 / 222,080.11 / 28,870.43 |

Group By Bill off widens `बीजक` from colspan 4 to 7 — item name, quantity, unit after the buyer's
PAN — and leaves the footer unmoved. Exactly the property phase 36 measured (27→50 there, 33→58
here) and exactly the three columns already built. Confirms phase 36 and phase 31; nothing to change.

### F. Inventory Master's Txn Type (37 #3, 26c Decision D) — Decision D falsified

Seven options, in this order:
`Opening Balance | Invoice | Credit Note | Purchase Bill | Debit Note | Inventory Adjustment | Warehouse Transfer`

Filtering to Warehouse Transfer returns **two rows per transfer**, one per leg:
`WT0002 | Kathmandu | +10` and `WT0002 | Patan | (10)` for one product on one date, with Contact,
Account, Rate, Amount, both discounts, VAT, Total and Additional Cost all blank. Receiving warehouse
positive, sending warehouse negative.

That is *exactly* what 26c predicted as its reason for excluding the type. The reasoning was right
and the conclusion was wrong.

**Production Journal is NOT on the live list**, and this codebase has always included it. Left in
place: Moonbeam shows the three production reports in its index, so Manufacturing looks enabled, but
index visibility was not proven to track entitlement, and removing a working type on an unproven
negative is the wrong risk. Recorded as observed.

**Opening Balance is offered but returned 0 rows** for 17-07-2026..15-09-2026 — the fiscal year
starts on the first of those dates and opening stock predates it. Its row shape is **unobserved**.

### G. Cadehi — `sales-summary`'s Group Wise location (36 #4)

Filter bar: BS fiscal year | Select Mode | Billing Location (All) | **`Group Wise location`** (an
inline checkbox) | GENERATE | Show Filters.

| Run | Header |
|---|---|
| unticked | `Date, Sub Total, Discount, Service Charge, Non Taxable Sales, Taxable Sales, VAT, Total` |
| ticked | `Date, **Location**, Sub Total, ...` |

`Bhadra, 2083 | 100 | ...` becomes `Bhadra, 2083 | HeadOffice | 100 | ...`. It adds Location to the
row key as the second column. A grouping, not a filter, and it composes with the Billing Location
filter beside it.

### H. What could NOT be confirmed, stated because it matters

**The base-currency fold itself is not observable on either tenant.** Chased the figure 13,300
because it is the exact number phase 36 used for "a foreign invoice contributes its own 100, not
13,300" — invoice 2062 (Bikal Karki) turns out to be an **NPR** invoice, Grand Total `NPR 15,029`,
Sub Total 13,300 = 1,600 + 10,800 + 900. Coincidence.

Moonbeam's currency list is NPR-only (2026-09-10 scan), so no foreign document exists there; Cadehi
has the currencies and almost no data. The available live support is the column headers: every money
column on the Sales Register and the VAT Summary is labelled **(रु)** — rupees. A register whose
columns say rupees reports rupees.

Phase 43's rule therefore stands on that plus the statutory argument. **It is not "confirmed live"
in the strong sense and this phase does not claim it is.**

---

## Scope decisions

### A. The four statutory reports fold to base currency — application, not a new decision

Phase 43 decided the rule (*a statutory register filed with the IRD is denominated in NPR*) and
named the four that still carried the defect. All three of its mechanics are followed exactly:

- **Per line, before the bucketing.** `PurchaseReturnReader.Bucketed.Total` is the sum of the other
  seven, so converting finished buckets would let Total stop equalling its parts. On the VAT Summary
  it is not even a rounding nicety: a bucket is *invoice lines at this rate MINUS credit note lines
  at this rate*, so two documents at two rates must be made commensurable **before** they are netted
  against each other.
- **In the shared reader.** `PurchaseReturnReader` folds, not its two callers — otherwise the
  Purchase Register would report a foreign return in rupees and the Purchase Return Register the same
  return in dollars, which is the exact failure phase 26c extracted the reader to prevent.
- **In memory after `ToListAsync`.** `ExchangeRates.ToBase` is a static call: untranslatable on SQL
  Server, silently evaluated in C# by InMemory, so every handler test would pass while the endpoint
  500s (phase-34b).

Annex 13 converts `Amount` and `VatAmount` separately and then adds them, rather than adding and then
converting, so a bill's gross there is exactly the sum of the two figures the Purchase Register
reports for the same bill — the two round identically by construction.

### B. Build only what the reads show, and record every absent control as absent

Four controls were built because the reads showed them (Display Warehouse in Column, the two new
Inventory Master types, Group Wise location, and the corrected tag semantics). Everything the reads
did **not** show is recorded above with the tenant and the date, not deferred vaguely — section H is
the most important of those, because it is a negative about this phase's own headline change.

### C. Where this codebase knowingly diverges from the live product

Three, each stated at the code:

1. **Group by Warehouse alone does something here and nothing live.** On Moonbeam, ticking it changed
   nothing at all — same 319 rows, same columns, same totals, on a four-warehouse tenant. Phase 36
   had already built it as a row split, which is what its name plainly means and which is useful.
   Matching the live no-op would mean deleting a working feature to reproduce what looks like a
   defect. The row split stays; the column crosstab is added beside it.
2. **The crosstab's footer is widened.** Live leaves it six cells against ten.
3. **Production Journal stays an Inventory Master type.** See section F.

### D. `DisplayWarehouseInColumn` is refused on its own, not ignored

The live control is disabled until its parent is ticked; a screen's `disabled` attribute is not a
server rule, so the validator states it and returns a **400 naming the field**. Phase 43's rule: a
field that will not be honoured should be refused rather than accepted and dropped, because
present-and-ignored reads to a client as accepted.

### E. System Audit gets a stamped location, not a join (35b #2)

Recommended by the roadmap and taken. `AuditBehavior` already runs **after** the handler and already
holds the document's `(DocumentType, Id)` — so one lookup by primary key, on a command that has just
written, is all it takes. Phase 35b's alternative (the 17-way join back that Decision B rejected for
the GL) was rejected for a *report over a period*, where it runs per row; this is a different
question with a different answer.

It reuses **phase 32b's existing `DocumentLocationReader`** rather than a new one — which this phase
discovered by writing a duplicate and having the compiler point at the original. The existing reader
is better: concrete blocks rather than a generic helper, and it explicitly warns against exactly the
`EF.Property` generic shape the duplicate used.

**Null is a normal value** and the report says so: Deal and WorkTask carry no location (phase 43 made
them auditable and they are records, not documents), the two opening-balance kinds sit outside that
reader, and every row written before this phase has none. Pre-existing rows are **not backfilled** —
the column records where the document *was when the action happened*, and that is not knowable after
the fact.

`SystemAuditReportQuery` is no longer exempt in either sweep guard. Satisfying the C# guard also
forced the handler to take `ICurrentUserService` and honour `LocationWiseReportPermission`, which it
had never done.

### F. The backfill is a command with a screen, not a migration (35a #5, 35b #6)

`BackfillDocumentLocationsCommand` assigns the tenant's HeadOffice to every **in-scope** document
that carries none.

- **A command**, because this is not a schema change with one right answer: it is a tenant deciding
  that its historical documents belong to its head office. A migration would make that decision
  silently, for every tenant, at deploy time.
- **Admin-only** (`Tenancy.Organization.BackfillLocations`), at the bar phase 39's
  `OrganizationProfileManage` and phase 16a's `OrganizationLockDateManage` set.
- **Idempotent by construction** — it only ever writes rows whose location is null. That is what
  makes a dry run unnecessary rather than merely unbuilt.
- **Scope-aware**: it backfills only the types `DocumentLocationScope.For(mode)` returns, so it
  cannot write a value the write path deliberately leaves null.
- **A screen**, on Configurations > Organization Profile, because phase 31's rule is that a
  tenant-level write is reachable only if you can name both the command and the screen that calls it.

It needed a new Domain mutator. `SetLocation` guards `EnsureDraft`, and the whole population this
command exists for is **Approved** historical documents — so fifteen aggregates gained
`BackfillLocation(Guid)`, which fills a null and refuses to move an assigned location. The two
reasons `SetLocation` is draft-only are exactly the two that cannot apply: a document with no
location was numbered from the *unscoped* pool, so there is no location-wise numbering to restate,
and it was counted under *no* location, so no filtered report's past answer is revised — it was
missing from every one of them, which is the defect being repaired.

A source-scan sweep guard asserts every `DocumentLocationScope.AllLocationBearingTypes` member is
covered, and was shown to fail when one was removed.

### G. The printed header (39 #3)

The organization block is centred beside the logo and the document title centred below, matching the
reference product. Phase 39 recorded the divergence and deliberately did not chase it because
re-laying out the one shared layout changes all fifteen types — doing it as its own item is what
makes "all fifteen" a reason to be careful rather than a reason to decline. The logo is *balanced*
by a constant spacer on the right, so the block is centred on the page rather than on the space left
beside the mark. All five existing PDF tests pass unchanged.

---

## The bug this phase found

**The Sales Register and the Purchase Register accepted a Billing Location filter and never applied
it to their primary document half.**

Phase 35b gave both queries a `LocationId` and taught `SalesReturnReader` / `PurchaseReturnReader` to
honour it. The invoice half and the purchase-bill half were never touched — the `Where` on
`SalesRegisterQueryHandler`'s invoice query is unchanged since **phase 19**. So choosing a location
removed the credit/debit notes from a statutory register and left every invoice and bill in it.

That is phase-34b's rule with statutory numbers behind it: *a filter a screen displays but did not
apply is worse than no filter.* The Sales Master Report has applied it to both its document queries
since phase 32, so the two registers were also inconsistent with their own siblings.

**Why the guard could not see it.** `ReportLocationSweepGuardTests` asks whether the *query record*
accepts a `LocationId` — which both always did. Whether a handler applies what it accepts is not
decidable by reflection, and a handler with two document queries that filters one of them is exactly
the shape that slips through. Fixed with two lines, pinned **behaviourally** with one test per
report, both shown to fail against the pre-phase-44 handlers, and the guard's own doc now states the
blind spot so the next reader does not assume it is covered.

---

## What shipped

**Base currency (43 Decision D)** — `PurchaseRegisterQueryHandler`, `PurchaseReturnReader`,
`VatSummaryReportQueryHandler` (a shared `FoldToBase` all four document types go through),
`AnnexThirteenReportQueryHandler` (all five accumulation loops).

**From the live reads** — `ReportingTagFilter` rewritten to OR-within/AND-across (both overloads
share one rule); `InventoryMasterReportQueryHandler` gains Warehouse Transfer (one row per leg, its
own warehouse per leg) and Opening Stock (dated at the tenant's accounting start date);
`InventoryPositionReport` gains `DisplayWarehouseInColumn` plus its validator;
`SalesSummaryReportQuery` gains `GroupWiseLocation`, and `TradeLineReader.Fact` gains a `LocationId`
so it can group by one.

**Roadmap items** — `Audit.LocationId` + the stamp + the report filter and column;
`BackfillDocumentLocationsCommand` + 15 `BackfillLocation` mutators + permission + endpoint + screen;
the centred print header.

**Fixes** — the location filter on both registers' primary half.

Two migrations, both hand-reviewed: `Phase44BackfillLocationsPermission` (two seed rows, no schema
change) and `Phase44AuditLocationId` (one nullable column, no backfill — see Decision E).

---

## Manual E2E (real API, fresh organization, 2026-09-15)

Master data seeded by curl + cookie jar; a USD @ 133 document chain built on it.

| Check | Result |
|---|---|
| Purchase Register, USD bill + note | DebitNote **−5,320 / −691.60** (not −40 / −5.20); partition holds (footer = sum over rows = 10,017.40) |
| Purchase Return Register, same note | **+5,320 / +691.60** — the shared reader makes the two agree |
| VAT Summary | purchase 13%: **7,980 / 1,037.40** (13,300−5,320, 1,729−691.60); sales **26,600 / 3,458**; both totals equal their bucket sums |
| Annex 13 | supplier **10,017.40**, customer **30,058**, both partitioning to TotalActivity |
| Sales Register location filter | HeadOffice → **1 row / 30,058**; Branch → **0** (both directions) |
| Purchase Register location filter | all → 3; HeadOffice → 3; Branch → 0, *after* the backfill |
| Inventory Master, Warehouse Transfer | **2 rows**, Second +1 / Main −1, rate and amount 0, contact null, net 0 |
| Inventory Position crosstab | `warehouseColumns: [Main, Second]`, `warehouseQuantities [0.6, 1.0]` summing to Qty 1.6 |
| the modifier alone | **400** naming `DisplayWarehouseInColumn` |
| Sales Summary | ungrouped `location: null`; `groupWiseLocation=true` → `location: HeadOffice` |
| System Audit | Invoice row `location: HeadOffice`; filter HeadOffice → 2 rows all HeadOffice, Branch → 0 |
| Backfill, negative | **403** naming `Tenancy.Organization.BackfillLocations` against a nonexistent org id — 403-not-404 proves `AuthorizationBehavior` fired before the handler |
| Backfill, positive | same Admin, same org, same run: **4 updated** (PurchaseBill 2, DebitNote 1, WarehouseTransfer 1) |
| Backfill, idempotent | second run **0 updated, 0 counts** |
| `sqlcmd` | PurchaseBills at HeadOffice **2**, still NULL **0**, at Branch **0**, Invoice untouched at HeadOffice **1** — true only if the write was right |

**The audit trail's honesty, demonstrated by accident and worth keeping.** The purchase bills' audit
rows read `location: null` while the documents now read HeadOffice — because they were audited
*before* the backfill, and at that moment they had no location. The backfill changed the documents,
not the history. That is the intended semantics of a stamped column.

---

## Bugs and snags hit

1. **`sed -i` flipped a whole file to LF** (`VatSummaryReportQueryHandler.cs`, all 128 lines) — the
   documented gotcha, caught by checking `CRLF == LF` immediately after and restored.
2. **A `Where` taking captured `Func` selectors** was written into the backfill's generic helper —
   the phase-2/9/25 gotcha in its purest form — and rewritten to `EF.Property` before it ever ran.
3. **A duplicate `DocumentLocationReader`.** The Write tool reported "updated", not "created", and
   the phase-32b original was overwritten. Recovered from git; the original is the better design and
   is what shipped. *The Write tool's own wording is the tell.*
4. **A scripted insert landed inside a doc comment** on 15 aggregates, stealing `SetLocation`'s
   documentation and mangling indentation, because the anchor walked *backwards* to find the comment
   start. Reverted all 15 (the phase's only change to them, so `git checkout --` was safe here) and
   re-anchored on the **end** of `SetLocation`, which is identical in every aggregate.
5. **Heredocs ate `\n` escapes** twice, exactly as `known-gotchas.md` says. Both times the fix was
   the Write tool.
6. **`SetLocation` guards `EnsureDraft`** — found by the backfill test, and the reason
   `BackfillLocation` exists. A bypass through the change tracker was considered and rejected.
7. **The E2E's accounting-defaults PUT silently ignored two fields.** `defaultReceivableAccountId` /
   `defaultPayableAccountId` are not the request's names (`defaultAccountsReceivableId` /
   `defaultAccountsPayableId` are) and the wrong names returned **200**, not a 400 — the failure
   surfaced three steps later as a 409 at Approve.
8. **A multi-line JSON body to `curl -d` was rejected** as unreadable JSON while the identical
   compact body worked.

---

## Known limitations / follow-ups

1. **The base-currency fold is not confirmable on either live tenant** (section H). Re-entry: a
   tenant with both multi-currency and real data.
2. **Opening Stock's row shape in Inventory Master is unobserved**, and this phase *decided* its
   date: the tenant's `AccountingStartDate`, not the row's `CreatedAt`, because `OpeningStockLine`
   stores no business date and a figure landing in whichever month it was typed would make a period
   report disagree with the stock it explains. A future live read may settle it differently.
3. **Production Journal's absence from the live Txn Type list is observed, not settled** — entitlement
   gating of the reports index was not proven.
4. **The Sales Register's live drawer has no Reporting Tags section**, but this codebase's query has
   carried `TagOptionIds` since phase 19. Recorded, not acted on: removing a working filter on one
   tenant's drawer is the inverse of the mistake this phase exists to correct.
5. **The corrected tag semantics change what existing tenants' tag-filtered reports return.** That is
   the point — they were returning the wrong set — but it is a behaviour change, not a fix nobody can
   see.
6. **No new Angular tests.** The Angular work (four screens) is covered by `ng build`, by the
   location sweep guard now that System Audit is no longer exempt, and by the E2E through the API.
   The count stayed at 447 and that is a gap, not a claim.
7. **`Audit.LocationId` has no index of its own.** Phase-34c's rule is to add one only when a
   measurement asks; the existing `(OrganizationId, CreatedAt)` index carries the report's period.
8. **The crosstab loads every warehouse**, so a tenant with many warehouses gets many columns. The
   live report does the same and no cap was observed.
