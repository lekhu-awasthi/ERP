# Phase 21c — Migrated tax-register import + the migrated Sales/Purchase Register variants

## TL;DR

FR-2.10 and the last unbuilt piece of FR-9.4. **Nepal's statutory report set is now complete.**

Two tenant-scoped aggregates — `MigratedSalesRegisterEntry` and `MigratedPurchaseRegisterEntry` —
hold a prior system's filed Sales and Purchase Book rows, carried across at cutover so they appear in
two new statutory reports **without existing as documents and without ever touching the General
Ledger**. Two new Admin-only report screens, one new Admin-only Migration screen under
Configurations, an .xlsx template per register, and a per-row importer that rides Phase 21a's
existing `ImportJob` — because the job is the same job.

**The invariant is the phase, and it is stated in prose here and in both aggregates' doc comments so
the next person does not try to give it a lifecycle.** A migrated register entry posts no
`GlJournalEntry` and no `GlLine`; creates no `StockLedgerEntry`, `StockMovement`, `Payment` or
`ContactLedger` movement; draws no number from `DocumentNumberGenerator` (its `DocumentCode` is the
*prior* system's, copied verbatim); has no Draft/Approve/Void lifecycle and so can never be approved,
voided, reversed or queued for approval; is not lock-date sensitive; and appears in **exactly two**
reports — the two migrated registers — and nowhere else in the product. It is real enough for a tax
return and deliberately not real enough to be anything else.

Seven decisions, all argued rather than inherited. **A — what a migrated row is**: two tables, a
free-text party with an optional best-effort `ContactId`, two appended `DocumentType` members, and a
return modelled as a negative row. **B — two new screens**, never a mode on the live registers.
**C — no new job table**: the two migrated types are `ImportEntityType` members on the existing
`ImportJob`, which is the *opposite* conclusion to 21b's Decision C and for the same reason 21b
reached its own. **D — the acting identity, re-argued from scratch** (21a's justification does not
transfer) and landing on the same answer for different reasons. **E — the template columns come from
Phase 19's live-confirmed statutory register, not from the reference product's own migration
template**, which could not be opened. **F — reach**: the two migrated registers only, with VAT
Summary, Annex 5, Annex 13 and the TDS report each opened, each found structurally unable to consume
a register-level row, and each given a test proving it is unaffected. **G — three new Admin-only
permission keys.**

**Confirm-live was not performed.** This session was non-interactive and CLAUDE.md's standing rule is
that the user signs in themselves, so `Configurations > Organization > Migration` and the reference
product's own two migrated report screens and templates stay unopened — see Decision E for why
shipping anyway is defensible here in a way it would not have been in 21a, and "Outstanding" for what
is left. **The browser pass on this phase's three new screens is also outstanding** (the Browser
pane's navigation to localhost was denied in this session); everything behind them was proven through
the real HTTP endpoints and real SQL Server instead.

Tests: Domain 192 (unchanged), Application.UnitTests **452 (+37)**, Api.IntegrationTests **14 (+4)**,
Angular **26 (+12)**. `dotnet build` / `ng build` / `ng test` / `dotnet test` all green. Manual E2E
against two fresh Organizations on real SQL Server uploaded real files through the real endpoint,
downloaded and opened both exported workbooks, proved zero GL/stock/payment rows with `sqlcmd`,
proved tenant isolation both directions, proved the unique index is enforced by the database, ran the
accidental second upload, and got four 403s each naming its exact key against a nonexistent
organization.

---

## Step 2 — confirm live: what could not be done, and what stood in for it

CLAUDE.md's rule is unambiguous: when a screen's shape is unconfirmed, read the live Tigg UAT tenant
first, **and the user signs in themselves**. This session was non-interactive, so no live pass
happened. Per the kickoff's instruction, that is stated plainly rather than papered over.

What that would have settled, and how each was resolved instead:

| Question the live pass would have answered | How it was resolved here |
| --- | --- |
| The migration templates' exact columns | **Derived, not guessed** — see Decision E. The column set is Phase 19's live-confirmed, column-by-column reading of the statutory registers themselves, which the migrated variants must match by construction. |
| Whether Migration is one importer or two | Two, one per register — the module scan's own note (line 394) says the Migrated Reports panel lists *Sales Register* and *Purchase Register* as separate rows, and 21a's live pass confirmed that panel exists. |
| Whether the migrated report screens show more or fewer columns than the live pair | Assumed identical, because that identity is the feature: FR-9.4 calls them variants of the same two reports. The one deliberate difference is that the migrated Sales Register can populate the four Export columns its live sibling always leaves empty (Decision A). |
| Whether `?type=migration` means one screen in two modes or two screens | **Decided on merit, not mimicry** — two screens (Decision B). The reference product's own menu lists them as two entries; the URL flag is the weaker signal, and the risk a shared screen carries is specific enough to settle it either way. |
| Whether a migrated import changes the tenant's VAT Summary there | Unanswerable without the live pass. Decided from this codebase's own handlers instead (Decision F), and the reasoning is recorded per report so it can be revisited cheaply. |
| Whether the wizard has a dry-run step | Inherited from 21a's answer, which *was* live-confirmed: the reference product's wizard is synchronous with a review step, this product's is async with an after-the-fact per-row result grid (NFR-4.3). Nothing new to decide. |
| `Organization > Developer Mode` / `> Documents` (carried from 21b) | Still unopened. |
| The outstanding browser pass on `Configurations > Import / Export` (carried from 21b) | Still outstanding — no browser session was possible at all this phase. |

**This is a reduced scope only in the sense that the template's header *wording* may differ from the
reference product's.** Nothing about the feature was cut for it, and the data the template carries has
an independent live-confirmed derivation. A user filling this app's own downloadable template is
unaffected either way, because `ImportTemplateDefinition` is one declaration driving both the file
written out and the headers parsed back in.

---

## Decisions

### A — What a migrated register row *is*

This is the phase. Every aggregate in this tree so far has been the opposite of what this needs: a
numbered, approvable, postable, voidable, lockable document. FR-2.10 asks for something that is real
enough to appear in a tax report and nothing else.

**One table or two? → Two.** The two registers share exactly five fields (date, document code, party
name, party PAN, tax-exempt bucket) and then diverge completely: Sales has one taxable bucket plus
four Export columns; Purchase has three taxable value/VAT pairs split Local / Import / Capital, plus
a customs declaration number. One table would carry eight permanently-null columns for every row in
either direction. That is precisely the reasoning 21b's Decision C used to split `ExportJob` from
`ImportJob`, applied unchanged. They also live in different bounded contexts, alongside the live
registers that mirror them (`Domain/Sales`, `Domain/Purchasing`).

**Does a row reference a `Contact`, or carry a free-text party? → Free text, with an optional
best-effort link.** This is the sub-decision with no free option, so its consequence is taken
explicitly:

- The row stores `PartyName` (required) and `PartyPan` (optional) — what the prior system actually
  printed on the statutory register, which is the only thing a cutover can promise.
- `ContactId` is set **only** when an existing `Contact` in that organization carries exactly that
  PAN, and is null otherwise. Never matched by name (two contacts sharing a trading name is common;
  an exact PAN match is a strong identity claim). **No `Contact` is ever created** — minting master
  data to satisfy a report column would put junk in the customer list of every tenant that migrated,
  and the alternative (requiring every historical customer to be imported first) would make the
  feature unusable at exactly the moment it is needed.
- **The consequence, taken rather than dodged:** `SalesRegisterRowDto.ContactId` and
  `PurchaseRegisterRowDto.ContactId` widen from `Guid` to `Guid?`. Every live document row still
  fills it, so the live registers' behaviour is unchanged; the nullability is a compile-time-visible
  statement that a register row need not point at a Contact.
- There is deliberately **no FK** to Contacts. A required relationship would force the ordering
  problem above, and a cascade would silently delete filed statutory history when someone tidies up
  a contact.

**What plays the part of `DocumentType`/`DocumentCode`?**

- `DocumentCode` is the prior system's own document number, copied verbatim, and it is the natural
  key: unique per `(OrganizationId, DocumentCode)`, which is what makes an accidental second upload
  reject rather than double a tenant's filed sales (see "Re-import safety" below).
- `DocumentType` gets **two appended members**, `MigratedSalesEntry` and `MigratedPurchaseEntry`.
  Reusing `Invoice`/`PurchaseBill` was considered and rejected: the register's Type column is a
  display label, but labelling a migrated row "Invoice" invites a future reader to treat
  `(DocumentType, DocumentCode)` as a pointer at a document that does not exist. Appending is safe
  and precedented — 21b appended `DataExport` for the same class of reason, `DocumentType` is
  persisted as a **string** with `HasMaxLength(30)` everywhere it is stored (both new names fit), so
  no persisted ordinal moves and no stored value changes meaning.

**A sales/purchase return is a negative row, not a second type.** The live Sales Register already
renders a CreditNote as a negated row in the *same* register, and the live Purchase Register does the
same for a DebitNote (Phase 19 decision #3). A migrated return carrying negative values therefore
produces a byte-identical register shape with no return modelling at all — no extra enum, no extra
template column, and one less thing for a user to get wrong. The alternative (a "Sales / Sales
Return" choice column that negates on ingest) buys friendlier data entry and costs a column that can
disagree with the sign already in the cell. The template's instructions state the convention in
capitals.

**The invariant, as an invariant.** Written at the top of `MigratedSalesRegisterEntry` and referenced
from its Purchase-side twin, because the next person to open the file will otherwise try to give it a
status: posts nothing, numbers nothing, moves no stock, takes no payment, has no lifecycle, is not
lock-date sensitive, and is read by exactly two query handlers.

### B — One screen or two, and where

**Two new Angular report pages** (`/reports/migrated-sales-register`,
`/reports/migrated-purchase-register`), each with its own route, its own component, its own nav
entry, its own permission key and a banner on the page. Plus a **separate Migration screen** under
Configurations (`/configuration/migration`), matching the reference product's own
`Organization > Migration` tab rather than adding a fourth entry to Import / Export's Upload Type
dropdown.

Alternatives and why not:

- **A mode toggle on the live register pages.** Tempting, because the column sets are identical by
  construction — that is the whole point of the feature. Rejected on the specific risk: these rows
  were typed into a spreadsheet by whoever ran the cutover, were never validated against a document,
  an approval or a GL posting, and reading them as this year's real books is the one mistake this
  data makes possible. A toggle also collides with Angular's default route-reuse strategy, which
  keeps one component instance alive across a same-component navigation (phase-3's bug #1) — the
  exact conditions under which a stale mode flag shows migrated data under a live heading.
- **One page with a source selector.** Same objection, plus the reference product's own menu lists
  the two migrated registers as separate entries.

**What *is* shared is the DTO, deliberately.** Both migrated queries return the live
`SalesRegisterDto` / `PurchaseRegisterDto`. A parallel DTO could only drift from the statutory form,
and sharing it means the ClosedXML export path, the pagination contract and the Angular row model are
one piece of code. The exporter takes a `migrated: true` flag that changes **only** the sheet name and
the file stem — a downloaded file called `SalesRegister.xlsx` that actually holds unposted pre-cutover
history is precisely the cross-reading the separate screens exist to prevent, and a spreadsheet
outlives the screen it came from.

**The live registers do not change behaviour.** The only edit to them is the DTO's `ContactId`
nullability.

### C — Which job carries the import → the existing `ImportJob`, with two new `ImportEntityType` members

**This is the opposite conclusion to 21b's Decision C, reached by applying 21b's own test.** 21b split
`ExportJob` from `ImportJob` because an export's columns are mostly meaningless to an import and vice
versa — half the table would always be null. Run the same test here and it comes out the other way:
**every column on `ImportJob` applies to a migrated register upload without exception** — an uploaded
.xlsx behind a storage key, a file name, total/processed/succeeded/failed row counts, a cancellation
flag, a heartbeat, a failure reason, an artifact-purged stamp. And the loop is not merely similar, it
is the same loop: parse a sheet, claim each row under a unique index, send a command, record an
outcome, resume after a crash, sweep the blob after retention.

So Decision C costs **two classes and two DI lines**. No new table, no new `IQueuedJobProcessor`, no
`QueuedJobRunnerOptions` subclass, no `AddHostedService`. `IQueuedJobProcessor`'s doc comment asks to
be joined only when the loop really is the same one; here it is, verbatim.

What is separate is the **screen**, not the machinery. `ListImportJobsQuery` gained an optional
`EntityTypes` filter, and the two screens each ask for their own set — so a master-data import never
appears in the migration log and a migrated tax-register upload never appears in the Import / Export
history. Verified live: with two migration jobs present, the master-data list returns `totalCount: 0`.

**Two consequences, both taken deliberately:**

- `ImportMode.UpdateExisting` is meaningless for a migrated row: there is no "update a historical
  statutory row" story, because the row is a copy of what a prior system already filed. The two types
  are **create-only**, rejected by `CreateImportJobCommandValidator` at upload as one whole-file
  mistake rather than one identical row error repeated N times, and re-checked in each importer as
  defence in depth. That restriction has precedent — the reference product itself offers Create only
  for Product Category and Account Group.
- `ImportEntityType` now holds two members that are **not** in the reference product's Upload Type
  dropdown, which its doc comment previously pinned itself to. The comment now says so and says why.

**What Phase 22 inherits:** the same four small pieces 21b costed, still four — but 21c is evidence
that the cheaper move is often to join `ImportJob` rather than add a fifth table. The test is 21b's:
*would the new job's rows leave columns permanently null, and is its loop genuinely a different
loop?* A document inbox is user-initiated and file-backed like an import, but its unit of work is a
document that outlives the job, and it has no notion of rows — that is a real difference, and it
argues for its own table. `QueuedJobRunnerHostedService<TProcessor, TOptions>` is there when it wants
it.

### D — The acting identity, argued rather than inherited

21a's rule was "a job that writes reuses the real Create command through the pipeline, under
`IJobActingUser`", and its **reason** was that every rule about creating a Product correctly already
lived in `CreateProductCommandHandler`. **That reason does not transfer**, because there was no
handler for "create a migrated register row" until this phase wrote one. The choice was genuinely
open. It went the same way, for different reasons:

**Chosen:** author `CreateMigratedSalesRegisterEntryCommand` /
`CreateMigratedPurchaseRegisterEntryCommand`, send them through the normal six-behavior pipeline from
inside `IEntityImporter`, under the scoped `IJobActingUser` the processor already assumes per row.

What that buys:

- `ValidationBehavior` runs the validator on every row, so a malformed row is one clearly-attributed
  row error rather than a provider exception the user cannot read.
- `AuthorizationBehavior` **re-checks `Configuration.MigratedRegister.Manage` per row at execution
  time**, so a user stripped of it between upload and run has the job stopped rather than honoured.
  This matters more here than anywhere else in the tree: these rows go straight into a statutory
  return with no posting behind them to reconcile against.
- `AuditBehavior` attributes every seeded row to the person who ran the cutover, via the two new
  `DocumentType` members.
- The importer stays inside `IEntityImporter`'s stated contract ("implementations must send the real
  create/update command through MediatR, never write the entity directly"), so `ImportJobProcessor`
  needs no change at all.

What it costs, stated plainly: two commands whose only caller is a background job. That is a small
price for the three behaviours above, and the alternative — writing rows directly, 20e/21b's
identity-free path — would have needed a bespoke write path *inside* an importer, in contradiction of
the seam's own contract, with validation and attribution hand-rolled beside it.

**Two deliberate omissions inside the command, both load-bearing:**

- **No lock-date marker interface.** `LockDateBehavior` gates only on `ILockDateSensitive` /
  `ILockDateSensitiveDocument` — "no marker interface, no gate" — so implementing neither is a
  decision, not an oversight. Every migrated row is dated before the tenant's accounting start date
  and so before any plausible lock date; gating it would make the feature unusable for the only thing
  it is for. What makes that safe is the invariant: there are no books behind a lock date to
  retro-edit, because the row posts nothing. Proven live (a row dated 2023-05-15 imported cleanly
  under a lock date of 2026-03-31) and locked down by a test, so nobody "fixes" it later.
- **No VAT cross-check, and no non-negative constraint.** Requiring `VatAmount == TaxableValue × 0.13`
  is tempting and would be wrong: a prior system's register carries whatever was actually filed,
  rounding included, and silently "correcting" or rejecting a filed number would make the migrated
  register disagree with the return the tenant has already submitted to the IRD — the one thing this
  feature must never do. Negative amounts are the return convention (Decision A), not an error.

### E — The template, derived rather than guessed

**Stated first, because it is the honest part:** the reference product's own migration templates were
never downloaded. This session was non-interactive.

What makes shipping a template anyway defensible **here**, and would not have made it defensible in
21a: this column set is not a guess about a screen nobody has seen. It is Phase 19's live-confirmed,
column-by-column reading of the statutory Sales and Purchase Books themselves (decision #3), which the
migrated variants must match by construction — the entire point of FR-9.4's migrated variants is that
pre-cutover history appears in the same statutory form as post-cutover activity. 21a's Product
template had no such independent derivation, which is exactly why 21a looked. The residual risk here
is header *wording* and party-identification convention, not the data.

**Migrated Sales Register template** (`MigratedSalesRegisterTemplate.xlsx`, sheet
"Migrated Sales Register"; `**` marks a required column):

| Column | Required | Notes |
| --- | --- | --- |
| Date | ** | AD, `yyyy-MM-dd` preferred; several other formats accepted, day-first on ambiguity |
| Document No | ** | The prior system's own invoice number. Unique per organization |
| Customer Name | ** | Free text |
| Customer PAN | | Links to an existing Contact only on an exact match; never creates one |
| Total Sales Value | ** | |
| Tax-Exempt Sales Value | | |
| Taxable Sales Value | | |
| VAT Amount | | Copied verbatim, never recalculated |
| Export Value | | |
| Export Country | | |
| Export Declaration No | | |
| Export Declaration Date | | |

**The four Export columns are the one place a migrated row may legitimately carry more than its live
sibling.** Phase 19 shipped them on the live Sales Register hardcoded to `0`/`null`, because this
codebase's `Invoice` has no export-sale flag (FR-5.8, deferred to Phase 23). A migrated row has no
such gap — the prior system knew, and the spreadsheet can carry it. Accepting them costs four columns
and is the only statutory data a cutover would otherwise lose outright. Verified live: the exported
Migrated Sales Register shows `250,000.00 | India | DEC-77 | 2024-08-03` on a row the live register
could only ever render as zeros.

**Migrated Purchase Register template** (`MigratedPurchaseRegisterTemplate.xlsx`):

| Column | Required |
| --- | --- |
| Date | ** |
| Bill No | ** |
| Import Declaration No | |
| Supplier Name | ** |
| Supplier PAN | |
| Tax-Exempt Value | |
| Taxable Non-Capital (Local) Value / VAT | |
| Taxable Non-Capital (Import) Value / VAT | |
| Taxable Capital Value / VAT | |

**The Capital/Non-Capital and Local/Import splits are separate columns, not a classification word.**
The IRD Purchase Book prints three value/VAT pairs side by side, so a prior system's export of it
already carries them apportioned; and this codebase's own live Purchase Register apportions per
*line* (`ExpenditureClassification`, Phase 8e), so collapsing a migrated row to a single bucket would
make the two registers structurally incomparable and would misrepresent any bill with mixed lines.

Both templates' instruction blocks say, in the file itself: that these rows are never posted to the
General Ledger and appear only in the Migrated Register report; that values are copied verbatim and
nothing is recalculated; that a return is a **NEGATIVE** row; that the document number must be unique
and a repeat upload is rejected; and that no Contact is ever created by the import.

### F — Reach: which statutory reports see migrated rows

FR-2.10 says "for continuity of statutory tax reporting", which argues for breadth; FR-9.4 names only
the two register variants, which argues for exactly two. The tension is real, so **every one of the
other four handlers was opened** and the answer derived from what each can actually consume rather
than from the FR's wording.

| Report | Reads | Verdict |
| --- | --- | --- |
| **Migrated Sales Register** | `MigratedSalesRegisterEntry` | **In scope — built.** |
| **Migrated Purchase Register** | `MigratedPurchaseRegisterEntry` | **In scope — built.** |
| VAT Summary | Buckets by `VatRate` **per document line** | **Deferred.** A migrated row is a document-level total with no lines and no per-rate breakdown; including it would mean inferring a rate from a value/VAT ratio, i.e. guessing at a number the tenant has already filed. **This is the one most worth revisiting**, and the cheapest way in is a VAT Rate column on the template. |
| Annex 5 | `ContactId`, `ContactCode`, name, PAN; splits by per-line `VatRate` | **Deferred.** Keyed on a real `Contact`, which a migrated row deliberately need not have (Decision A), and on lines it does not have. |
| Annex 13 | Aggregates per contact **and per product** above a threshold, split by each line's `ExpenditureClassification` | **Deferred — structurally impossible**, not a judgment call. A migrated row has no product lines, so there is nothing to group by. |
| TDS Report | A document's `TdsType` and withheld amount | **Deferred.** The statutory Purchase Book carries neither column, so a migrated row has no TDS data even in principle; accepting one would mean inventing a template column with no statutory source. |
| Trial Balance / every GL report | `GlJournalEntry` / `GlLine` | **Never, by invariant.** |

Each deferral has a test asserting the report is unaffected, not silence — otherwise the next person
cannot tell "deliberately excluded" from "nobody looked". All seven verified live against a tenant
whose only data was migrated rows: every one returned zero.

### G — Permission keys

Derived per CLAUDE.md's rule and seeded through `RolePermissionConfiguration.HasData` **before**
scaffolding the migration (phase-9's lesson), so the seed is real rather than an empty scaffold.
`PermissionKeyCatalog` picks the constants up by reflection.

- **`Reports.MigratedSalesRegister.View` / `Reports.MigratedPurchaseRegister.View` — Admin-only.**
  The bar lands exactly where Phase 19 put the live pair: a flat per-transaction register carrying a
  party PAN column, where both factors independently justify Admin-only. **They get their own keys
  rather than riding `SalesRegisterView`/`PurchaseRegisterView`**, and the brief asked for that to be
  said either way. Two reasons: the data has a different provenance and a different trust story
  (typed into a spreadsheet, never validated against a document, an approval or a posting), so an
  organization that wants to show a bookkeeper this year's real register without also handing over an
  unvetted dump of the prior system's history can now express that; and a shared key would leave the
  audit trail unable to say which register was read. Riding the existing keys would have been
  defensible and cheaper — it would just have been irreversible without a migration.
- **`Configuration.MigratedRegister.Manage` — Admin-only.** The per-row write key, exercised on
  **every row at execution time**. This is 21a's corollary restated: a feature-level `*.Manage` key
  does not replace the per-entity key the rows still exercise. Enqueuing the upload is still
  `Configuration.ImportJob.Manage` (it is an `ImportJob`), so a user needs both, and a user with
  `ImportJobManage` alone imports nothing — proven by a test. It is Admin-only because writing rows
  that appear in a statutory tax report with no document, approval or GL trace behind them is the
  least reviewable write in the product: nothing else here can put a number in front of the tax
  authority with no posting to reconcile it against. One key rather than a Sales/Purchase pair,
  because "may seed this tenant's pre-cutover statutory history" is one decision an organization
  makes once.
- Reading the job list on the Migration screen rides the existing `Configuration.ImportJob.View`,
  since it is literally that list filtered.

---

## What shipped

**Domain** — `MigratedSalesRegisterEntry` (Sales), `MigratedPurchaseRegisterEntry` (Purchasing);
`DocumentType.MigratedSalesEntry` / `.MigratedPurchaseEntry`;
`ImportEntityType.MigratedSalesRegister` / `.MigratedPurchaseRegister`.

**Application** — `CreateMigratedSalesRegisterEntryCommand` / `CreateMigratedPurchaseRegisterEntryCommand`
(+ validators, handlers); `MigratedSalesRegisterQuery` / `MigratedPurchaseRegisterQuery` (+ validators,
handlers) returning the live registers' own DTOs; `MigratedSalesRegisterImporter` /
`MigratedPurchaseRegisterImporter`; `ImportRowReader.GetOptionalDate`/`GetRequiredDate`;
`ListImportJobsQuery.EntityTypes`; `CreateImportJobCommandValidator`'s create-only rule; three
`PermissionKeys` constants.

**Infrastructure** — two EF configurations (unique `(OrganizationId, DocumentCode)`, indexed
`(OrganizationId, Date)`, `decimal(18,2)` money, no FK to Contacts), the
`Phase21cMigratedRegisters` migration (two tables + six role-permission rows; read before applying,
no destructive operation, nothing else touched), two `IAppDbContext` DbSets, two DI lines.

**Api** — `/reports/migrated-sales-register` and `/reports/migrated-purchase-register` plus their
`/export` siblings; `ReportSpreadsheetExporter.Export{Sales,Purchase}Register(report, migrated)`;
`entityTypes` on `GET /import-jobs`.

**Angular** — `migrated-sales-register-page`, `migrated-purchase-register-page`, `migration-page`
(+ routes, Configurations nav entry, two dashboard report links); four service methods; widened
`DocumentType` union and nullable `contactId` on both row models; `MIGRATION_ENTITY_TYPES` /
`MASTER_DATA_ENTITY_TYPES`, with the Import / Export page now filtered to the latter.

---

## Testing

**Counts.** Domain 192 (unchanged — both new aggregates are factory-plus-fields with no behaviour to
test at that level; every rule about them lives in a handler or a query, which is where they are
tested). Application.UnitTests **452 (+37)**. Api.IntegrationTests **14 (+4)**. Angular **26 (+12)**.

**The headline test is that nothing posts.** `MigratedRegisterImportTests` imports a batch through
the real processor and asserts zero `GlJournalEntry`, `GlLine`, `StockLedgerEntry`, `StockMovement`,
`Payment` and `Invoice` rows; `MigratedRegisterReportReachTests` asserts the same at query level and
that Trial Balance stays at 0/0. Both re-checked live with `sqlcmd` against real SQL Server, because
this is the claim a future phase is most likely to break.

Also covered: both directions of "migrated rows and live documents never appear in each other's
register" (same tenant, same date range, one Approved Invoice and one migrated row, each register
returning exactly one of them); tenant isolation asserted the strict way (org B's rows **absent**,
not merely outnumbered); footer totals over the full filtered set with more rows than one page holds;
per-row errors carrying the spreadsheet's own row number and column; partial success as a `Completed`
job; re-import safety; the pre-lock-date row; the per-row permission re-check; exact-PAN linking with
no Contact ever created; create-only mode rejection; template self-consistency; and date parsing,
including the ambiguous `7/8/2024` case pinned to day-first — a wrong guess there imports the wrong
month, silently, in statutory data.

**The real .xlsx round trip** (`MigratedRegisterTemplateRoundTripTests`, no Docker) renders each
template through the real `ImportTemplateWriter` and reads those exact bytes back through the real
`ClosedXmlImportFileReader` and `ImportRowReader`, asserting every declared column resolves and the
sample row parses. This is 21a's "do not synthesise a workbook by hand" lesson turned into an
assertion. One wrinkle worth recording: `Results.Stream`'s `IResult` resolves an `ILoggerFactory`
from `HttpContext.RequestServices`, so a bare `DefaultHttpContext` throws
`ArgumentNullException(provider)` before writing a byte — the context needs a real service provider.

**What the InMemory provider cannot prove**, verified against real SQL Server instead: it enforces no
unique index, so the `(OrganizationId, DocumentCode)` constraint is unreachable from unit tests. A
direct duplicate `INSERT` via `sqlcmd` was rejected by the database (`Cannot insert duplicate key row
in object 'sales.MigratedSalesRegisterEntries'`), and both unique indexes were confirmed present.

### Manual E2E (real server, real SQL Server, two fresh Organizations)

Master data seeded via curl + cookie jar under the `Testing:*` admin identity; two fresh
Organizations created for the phase.

1. Both templates downloaded from the real endpoint — `200`, correct
   `Content-Type: …spreadsheetml.sheet` and `Content-Disposition` filenames.
2. Each template **filled** (never synthesised) with real rows via ClosedXML, including two
   deliberately bad rows and a negative return row, and uploaded through the real multipart endpoint
   — `201`, and both jobs polled to `Completed`. Sales: 6 rows, 4 succeeded, 2 failed. Purchase: 4
   rows, all succeeded.
3. Per-row errors named the spreadsheet's own rows: row 6 → `Date` /
   `'not-a-date' is not a valid date; use yyyy-MM-dd (for example 2024-07-30).`, row 7 →
   `Document No` / `'Document No' is required.`
4. `UpdateExisting` rejected at upload with `400` and the create-only message.
5. Both migrated reports returned the right rows and the right full-set totals — Sales
   `408,200.00 / 140,000.00 / 18,200.00` across four rows including the negative return; Purchase
   local `75,000.00 / 9,750.00` net of the return.
6. **Tenant isolation both directions:** org B's migrated registers returned `totalCount: 0` while
   org A's held the data.
7. **Decision F verified live:** live Sales Register, live Purchase Register, Annex 5, Annex 13 and
   the TDS report all `totalCount: 0`; VAT Summary `0/0`; Trial Balance `0/0`.
8. **`sqlcmd`:** `MigratedSales=4 MigratedPurchase=4 GlJournalEntries=0 StockLedger=0
   StockMovements=0 Payments=0 Invoices=0`.
9. Both register **exports** downloaded and opened: sheet named `Migrated Sales Register`, filename
   `MigratedSalesRegister_2024-01-01_2024-12-31.xlsx`, every column present, Export columns populated,
   footer `Total Value 408,200.00`.
10. **Accidental second upload of the same file:** `Completed`, 0 succeeded, 6 failed, every repeated
    row carrying `…document number 'OLD-INV-0912' has already been imported…`; the register's total
    unchanged at `408,200.00`.
11. **Lock date:** organization locked at 2026-03-31, a row dated 2023-05-15 imported cleanly.
12. **Four 403s against a nonexistent organization id** — 403-not-404 proving the check fired before
    any handler — each naming its exact key: `Reports.MigratedSalesRegister.View`,
    `Reports.MigratedPurchaseRegister.View`, and `Configuration.ImportJob.Manage` (twice: enqueue and
    template download). The fifth key, `Configuration.MigratedRegister.Manage`, is proven by unit
    test rather than live, since demonstrating it needs a role that lacks it and Admin is a shared
    system role.

---

## Outstanding

- **No confirm-live pass** (Decision E). `Configurations > Organization > Migration` and the
  reference product's two migrated report screens and templates remain unopened, as do
  `Organization > Developer Mode` and `> Documents`, carried from 21b.
- **No browser pass on this phase's three new screens.** `ng serve` compiled them and `ng test`
  covers the assertions that matter (the "never posted to the General Ledger" banner, totals read
  from the server rather than reduced over the page, the upload-type list, the history filter, the
  create-only upload); the Browser pane's navigation to localhost was denied in this non-interactive
  session, so nothing was clicked. Everything behind the screens was proven through the real HTTP
  endpoints instead.
- **21b's outstanding browser pass on `Configurations > Import / Export`** is still outstanding, for
  the same reason.
- **VAT Summary reach** is the one Decision F deferral a user may reasonably want reversed; a VAT
  Rate column on both templates plus a bucket contribution in `VatSummaryReportQueryHandler` is the
  whole of it.

## New known gotchas

- `Results.Stream`'s `IResult` resolves an `ILoggerFactory` from `HttpContext.RequestServices` when
  executed, so testing a file-download endpoint's result against a bare `DefaultHttpContext` throws
  `ArgumentNullException (Parameter 'provider')` before a single byte is written. Give the context a
  real `ServiceProvider` with `AddLogging()`.
- `EF.Functions.Like` cannot be translated by the InMemory provider at all, so a `LIKE`-style search
  in a handler whose tests run on InMemory must be written as `String.Contains` — which SQL Server
  translates to the same `LIKE '%term%'` and InMemory evaluates directly.
- A date column in an import template needs an **explicit format list with day-first ahead of
  month-first**, not a bare `DateTime.TryParse`. `07/08/2024` is a real date under both readings, so
  the wrong default imports the wrong month with no error anywhere — the failure mode no test finds
  unless it asserts that exact case.
