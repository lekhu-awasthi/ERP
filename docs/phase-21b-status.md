# Phase 21b — Full-tenant data export (FR-2.8, NFR-4.3)

## TL;DR

A tenant's data, on demand, as one multi-sheet `.xlsx`, produced by a background job and downloaded
through an authenticated endpoint. This codebase's **third** background job — and the first phase in
a while that is *designing* rather than mirroring, because the reference product has no backup
screen at all (established by Phase 21a's confirm-live pass, re-checked here and not re-litigated).

**Decision A is the phase, and it is a product decision, not a technical one.** FR-2.8 says
"backup/export". **This codebase has no restore path and none is planned**, so a button labelled
Backup would promise something the product cannot keep. What ships is an honest **export**: readable,
useful, lossy, and explicitly not restorable — and it says so in three places (the button's own
caption, the workbook's first sheet, and the completion email). Scope is FR-2.8's five named
categories — products, contacts, chart of accounts, ledger transactions, stock movements — not all
82 DbSets.

**Decision D inverts Phase 21a and restores Phase 20e's default.** 21a needed `IJobActingUser`
because an import *writes* through permission-gated commands. An export only **reads**, and reads
through hand-filtered org-scoped queries rather than MediatR requests — so it has **no ambient
identity at all**. The permission check and the `Audit` row both live on the enqueue command, in a
real HTTP request. `IJobActingUser` exists and was deliberately not used.

**Decision C is the job-table question 21a deferred to "when there is a second consumer".** The
answer is a **separate `ExportJob` table** (an import consumes a payload; an export produces one, and
`ImportJobRow`'s whole claim-under-a-unique-index machinery exists only because an import is not
idempotent) driven by a **shared timer host**, `QueuedJobRunnerHostedService<TProcessor, TOptions>`,
which 21a's runner was refactored into with no behaviour change. That is a shared loop, not the
generic job framework 21a was right to decline: no job-kind discriminator, no shared table, no
handler registry. The alert scheduler is deliberately left alone.

**Decision E is this phase's own consequence, and it fixes a leak it inherited.** Before this phase
exactly one caller in the tree ever invoked `IFileStorage.DeleteAsync`, so every workbook uploaded to
21a's importer leaked permanently. An export leaks something far larger and produced far more
casually. Both are now swept on a **7-day** retention, through a `SweepAsync` on the shared processor
seam — no third background service.

Tests: Domain.UnitTests 192 (+7), Application.UnitTests 415 (+27), Api.IntegrationTests +2 (a real
ClosedXML round-trip, no Docker needed), Angular 14 (+7). `dotnet build` / `ng build` / `ng test` /
`tsc --noEmit` all clean. Manual E2E against two fresh Organizations on real SQL Server: the real
file downloaded through the real endpoint and opened, tenant isolation proved **both directions with
a canary row**, retention proved by watching both blob kinds leave the disk, the abandoned-run
reclaim proved by hand, and **four 403s each naming its exact key against nonexistent ids**.

---

## Scope: 21b of three

- **21a — async job foundation + bulk import (FR-2.9, NFR-4.3).** Complete; see
  `phase-21a-status.md`.
- **21b — full-tenant backup/export (FR-2.8). ← this document.**
- **21c — migrated tax-register import + the migrated Sales/Purchase Register variants (FR-2.10,
  closing FR-9.4).** Untouched here.

---

## Step 2 — confirm live: not performed, and why

**This session was non-interactive, so nobody could sign in to the Tigg UAT tenant.** CLAUDE.md's
standing rule is that the user logs in themselves and this session never enters credentials, and
that rule was kept. The three items the brief listed as still worth a short look —
`Organization > Developer Mode`, `Organization > Documents`, and a report screen's export control —
were therefore **not opened**.

That is a smaller gap than it would be for most phases, because the decisive finding was already in
hand: **Phase 21a's confirm-live pass established that the reference product has no backup screen at
all**, and that `Configurations > Organization`'s six tabs are Overview / Tasks / Documents /
Features / Migration / Developer Mode with Migration being 21c's Migrated Reports panel. So 21b was
always going to design from FR-2.8 rather than mirror, and it did.

**What is still genuinely unknown, and what it could change:**

- `Organization > Developer Mode` — most likely an API-key screen. If it is, that is a future phase's
  problem and touches nothing here. It is *conceivable* it hides a data-export surface, in which case
  this phase's UI placement (see Decision A) would be worth revisiting; nothing about the job, the
  artifact or the permissions would change.
- `Organization > Documents` — if it turns out to be a generated-file store rather than plain
  attachments, that would be an alternative home for a finished export. The download idiom here
  (authenticated `Blob` fetch from a job row) would not change either way.

Worth an eye on the next live session; not worth blocking a shippable phase.

---

## Decision A — an export, not a backup

**The alternatives.**

1. A human-readable multi-sheet **export** of FR-2.8's five named categories. Honest, immediately
   useful (an accountant opens it in Excel), lossy, not restorable.
2. A machine-readable full-fidelity dump of all 82 DbSets. Restorable *in principle* — but nothing
   in this codebase or the roadmap can read one back, so in practice it is write-only: a file that
   only exists to be produced. It is also a tenant's entire database in one downloadable object,
   including `Audit`, `VerificationCode` and `AlertSendLog`.
3. Both, as separate actions.

**Chosen: (1).** The deciding argument is not effort, it is honesty. FR-2.8's phrase is
"backup/export", and **"backup" implies restore**. There is no restore path in this codebase, none in
the roadmap through Phase 25, and building one is a phase of its own (referential ordering, id
collisions, GL/FIFO reconstruction, lock dates). Shipping a button labelled *Backup* that produces a
file nobody can restore from is a promise the product cannot keep, and the user finds out at exactly
the worst moment. Option (2) would be that same broken promise with a machine-readable file format
attached — arguably worse, because the format *looks* restorable.

Option (3) was rejected as scope: it would ship (2)'s problems alongside (1) for no user benefit
today.

**What the artifact can do:** show, read and search every product, contact, account, posted ledger
line and stock movement the tenant has, in a spreadsheet anyone can open, with foreign keys resolved
to names rather than GUIDs, timestamps on the Nepal wall clock, and numbers kept numeric so the
arithmetic works.

**What it cannot do:** be uploaded back. It is not a backup of the database, it does not contain
documents (Invoices, Purchase Bills, Payments and the other 12 transactional types are represented
only by the GL lines and stock movements they posted), it does not contain configuration,
attachments, users, roles or permissions, and it cannot recreate an organization.

**Where that is said, in the product, not just in this document:**

- The screen's caption: *"This is a readable export, not a restorable backup — the file cannot be
  uploaded back to recreate this organization."*
- The workbook's own **Summary** sheet, first rows, before any data.
- The completion email.

The word "Backup" appears on no button, no nav entry and no heading. `web/src/app/core/exports/`
and the Angular spec both assert this.

### Scope: five categories, not 82 DbSets, and no filters

FR-2.8's parenthesis names five things and all five exist as first-class DbSets. Read literally,
"full data backup" means all 82 — which produces an artifact nobody can read. The FR's own list is
the narrower and more useful reading and is what ships. `ExportCategory`'s doc comment records this
so the next reader does not have to re-derive it.

The command takes **no parameters beyond the tenant**: no per-category checkboxes, no date range.
"Export my data" is one button, not a form. A date filter on the two transactional categories is the
natural follow-up if the row cap ever bites a real tenant, and is listed as deferred.

Two category choices worth recording:

- **`StockMovement`, not `StockLedgerEntry`.** The movement table is the tenant-visible record of
  what went in and out; ledger entries are FIFO cost layers whose `QuantityRemaining` only means
  anything to the costing engine that maintains it.
- **Ledger Transactions is one row per `GlLine`**, carrying its entry's `PostedAt`, source document
  type and source document id. `PostedAt` is stamped from the real clock at Approve time, never the
  document's business date (see `GlDateBoundary`) — so the column means "when it hit the ledger",
  which is what a ledger export should say.

---

## Decision B — one multi-sheet .xlsx, with stated caps

**The alternatives:** one multi-sheet `.xlsx`; a `.zip` of per-entity CSV/JSON files; a single JSON
document.

**Chosen: one `.xlsx`, one sheet per category, plus a leading Summary sheet.** It matches what the
audience actually does with the file (opens it in Excel), it matches every other file this product
produces (Phase 16c's 15 report exports and 21a's import templates are all ClosedXML `.xlsx`), and
multi-sheet output is already proven here — `ReportSpreadsheetExporter.ExportVatSummaryReport` writes
two sheets. A `.zip` of CSVs would be more streamable but hands a non-technical user something they
have to unpack; JSON would be the right answer for a restore path that does not exist.

**Where the writing code lives.** Not `ReportSpreadsheetExporter`: that is a static class in
`src/Api`, and an export runs in a background service — nothing may depend on `Api` but
`Program.cs`. Phase 21a hit exactly this from the read side and moved ClosedXML into Infrastructure
behind `IImportFileReader`; this is the same move in the write direction. `IExportWorkbookWriter`
(Application) takes plain rows; `ClosedXmlExportWorkbookWriter` (Infrastructure) turns them into
bytes. Every decision about *what* is in the rows stays in Application, unit-testable without a file.

**The caps, stated rather than pretended away.** ClosedXML is not a streaming writer: `XLWorkbook`
materialises every cell of every sheet before a byte is written, and `SaveAs` is synchronous-only so
it targets a buffer. Phase 21a met the same constraint from the read side and answered it with a
stated 5,000-row cap; the write side gets the same honesty.

- **`ExportLimits.MaxRowsPerCategory = 25,000`**, deliberately conservative. Five sheets at that cap
  is a worst case of 125,000 buffered rows.
- **A tenant past the cap still gets a complete, openable file.** The category is cut off at the cap
  *in its deterministic order* (products by code, ledger by posted-at, and so on), so two exports of
  unchanged data cannot silently disagree about which rows were dropped.
- **Truncation is disclosed in three places** and is **not** a status: the job stays `Completed`
  because the file is complete and downloadable. It appears on the Summary sheet's preamble
  ("TRUNCATED: …"), in that sheet's per-category `Truncated` column, on the job row's
  `TruncationNotice` (rendered as a warning in the grid), and in the completion email.
- **Raising the cap means moving to a streaming writer** (the OpenXml SDK's SAX-style writer). That
  is the recorded follow-up if a real tenant hits it; it is not a change this phase pretends to have
  made.

Also worth noting: `AdjustToContents` is capped to the header band plus the first 50 data rows.
Running it over a 25,000-row sheet costs more than writing the sheet did.

---

## Decision C — a separate table, a shared runner

Phase 21a recorded: *"whether one shared hosted service polls several job kinds or each gets its own
is deferred to when there is a second consumer to look at."* This phase is that consumer, and the
answer splits: **separate tables, shared loop.**

**Why not a `JobKind` discriminator on `ImportJob`.** The two share a lifecycle and nothing else.
`ImportJob.StorageKey` is an **upload**, set at creation and never changed; `ExportJob.StorageKey` is
an **output**, null until the job finishes and null again once retention deletes it.
`EntityType`/`Mode` have no export meaning. And the entire `ImportJobRow` ledger — the
claim-under-a-unique-index idiom, the `Pending`-as-a-claim status, the interrupted-row story — exists
because **an import is not idempotent**. An export is: re-running one regenerates a file and changes
nothing about the tenant. Merging would have produced a table where half the columns are always null
on half the rows, plus a migration against a table shipped one phase earlier.

**Why not a third hand-rolled `BackgroundService`.** 21a copied 20e's shape rather than extending it,
and that was right — alerts are schedule-driven, idempotent, and answer "what is due now". But
imports and exports are *both* queue-driven, user-initiated, cancellable and drainable: the loop is
identical, line for line, and it holds no business decision (21a's own runner doc comment said so).
Copying it a third time would be duplication with nothing bought.

**Chosen: `QueuedJobRunnerHostedService<TProcessor, TOptions>`** over a new
`IQueuedJobProcessor` seam, with `IImportJobProcessor` and `IExportJobProcessor` both deriving from
it. 21a's `ImportJobRunnerHostedService` was deleted and replaced by a closed generic; imports behave
identically and their `ImportJobRunner` configuration section is unchanged.

**This is a shared timer host, not a generic job framework**, and the distinction is the point: there
is no job-kind discriminator, no shared table, no handler registry, no dispatch. Each processor owns
its own table, its own semantics and its own decisions, and the host knows about none of it.

**One hosted service per processor, not one loop over all of them.** A single loop draining
processors in sequence would let a 5,000-row import hold up an export (and the reverse) for minutes.
Head-of-line blocking between two unrelated features is a real user-visible regression for no gain;
a registration line per processor buys each its own timer, poll interval and kill switch.

**The alert scheduler is deliberately left alone.** Retrofitting it onto this seam would mean
inventing a "there is nothing to claim, only a schedule to evaluate" shape for a single consumer.

**The crash story, which is where an export is genuinely simpler than an import.** A job that dies
mid-run stays `Running` with a heartbeat that goes stale; the next runner re-claims it past the
2-minute lease and **regenerates the whole workbook from scratch**, resetting progress to zero. No
resume logic, no per-row ledger, no risk of a duplicate — because re-running is free. The one thing
that must never happen is a half-written file offered as a complete download, and the shape of the
code is what prevents it: the workbook is built into a buffer, saved to `IFileStorage`, and only then
is the key written **alongside `Completed` in a single `SaveChangesAsync`**. A process that dies at
any point before that leaves a job with no `StorageKey`, which the UI shows as in-progress and never
as a download. The residue is at worst one orphaned blob (saved, never committed); that is an
accepted cost, not a correctness failure, and is deliberately not paid for with a two-phase protocol.

**No concurrency token on `ExportJob`**, for exactly the reason `ImportJob` has none (21a's Bug 1):
the row has a second legitimate writer — the user's cancel command — and SQL Server bumps a
rowversion on any UPDATE, so a cancel mid-run would invalidate the runner's token and wedge its next
progress write. Two runners racing on one export duplicate effort and produce one file each; the
second commit wins and the first blob is orphaned. Wasteful, never wrong.

**What 21c inherits from this.** A queue-driven job is now: a table, a processor implementing
`IQueuedJobProcessor`, an options class deriving from `QueuedJobRunnerOptions`, and one
`AddHostedService<QueuedJobRunnerHostedService<TProcessor, TOptions>>()` line. 21c's migrated-register
import **writes**, so it takes 21a's side of the identity question (`IJobActingUser`, per-row claim
under a unique index), not this phase's — but the runner, the sweep hook, the status model and the
notification pattern are all reusable as-is.

---

## Decision D — no acting identity, and why that is available again

Phase 20e's default was *a background job needs no ambient identity*: `AlertDispatcher` sends no
MediatR request, reading through a purpose-built service that takes an explicit `OrganizationId`.
Phase 21a had to give that up, because an import **writes** and every rule about creating a Product
correctly lives in the Create/Update handlers.

**An export only reads**, so the question reopens — and the answer is: reading through
permission-gated MediatR queries would buy nothing and cost the identity. The category readers are
plain org-filtered `IAppDbContext` queries; there is no numbering, no validation, no FK-resolution,
no audit obligation for a read. So this job has **no identity at all**, and `IJobActingUser` is
present in the tree, available, and deliberately unused. The `ExportTestHost` doc comment records
that absence so it does not read as an oversight.

**Where the identity does matter: the enqueue.** `CreateExportJobCommand` runs in a real
authenticated request. It implements `IRequirePermission` (`Configuration.ExportJob.Manage`) and
`IOrganizationScoped` (mandatory — Phase 12's lesson), so `AuthorizationBehavior` checks both the key
and org membership. It also implements `IAuditableRequest` with a new
`DocumentType.DataExport`, which is all `AuditBehavior` needs off the "Create" prefix: **a
full-tenant data export is the largest single data-egress action in the product, and it now leaves an
audit row naming who triggered it.**

`DocumentType.DataExport` is appended last, so no persisted ordinal moves. It is not a document in
the accounting sense — nothing numbers it, nothing posts it, no `GlJournalEntry` or
`StockLedgerEntry` ever carries it — and its doc comment says so.

**What is not audited: the download itself.** `AuditBehavior` only fires on
Create/Update/Approve/Void, and the download is a query. Recording each retrieval is a small additive
follow-up and is listed as deferred rather than dismissed.

---

## Decision E — retention, and the leak this phase inherited

**The finding.** A grep for `IFileStorage.DeleteAsync` across the tree found **exactly one caller**:
`DeleteAttachmentCommandHandler`. Nothing ever deleted an `ImportJob`'s uploaded workbook, so Phase
21a leaked every upload, permanently. This phase makes that materially worse — an export artifact is
the tenant's whole data set, produced in two clicks and regenerable on a whim.

**A full-tenant dump sitting on disk indefinitely is a security posture, not housekeeping.** So:

- **Retention is 7 days** (`JobArtifactRetention.Period`). Short enough that a dump is not sitting
  around; long enough that Friday's export is still there on Monday. Regenerating is idempotent and
  cheap, which is what makes a short window affordable. A constant rather than bound options,
  deliberately: Application takes no `Microsoft.Extensions.Options` dependency anywhere else, and
  this is a product decision rather than a per-deployment knob anyone has asked for.
- **The sweep is a step in the existing tick, not a third background job.** `IQueuedJobProcessor`
  gained a `SweepAsync` with a default no-op body; the host calls it once per tick, before draining,
  in its own scope and its own try/catch so a failing sweep can never stop jobs from running. One
  extra indexed query per tick costs the same as the poll already happening.
- **21b fixes 21a's leak too.** `ImportJobProcessor.SweepAsync` deletes the upload of any import that
  finished more than 7 days ago. Leaving a known leak in place next to a freshly-fixed one would
  have been indefensible, and the mechanism was already being built. The job row and its per-row
  results survive; only the blob goes. An import that is still Queued or Running is never touched —
  its upload has not been read yet, and that is the one way this sweep could break an import.
- **Blob first, then stamp the row.** A crash between the two re-sweeps the row next tick and deletes
  an already-deleted file, which `IFileStorage` treats as a no-op. The reverse ordering would strand
  the file forever.
- **The row survives the purge.** `ExportJob.MarkArtifactPurged` clears `StorageKey` (so nothing can
  hand out an identifier that no longer resolves) and stamps `ArtifactPurgedAt`, keeping the status
  and file name. A user who comes back to a week-old export is told it **expired** — the grid shows
  an "expired" badge and no Download button, and the endpoint answers *"This export has expired and
  its file has been deleted. Generate a new one."* rather than a bare 404 or, worse, a dead link.
- **A per-tenant cap of N retained exports was considered and not built.** A TTL alone is sufficient
  given that only one export may be in flight per organization at a time (see below), and a second
  bound would be two rules where one does.
- **Batched at 100 rows per sweep**, so one tick cannot become an unbounded delete loop.

---

## Decision F — who may download a completed export

Two independent questions, both answered deliberately.

**1. How does the file leave the server?** Through one door: an authenticated,
permission-checked endpoint. `IFileStorage` has no "resolve to a public URL" method by Phase 18's
own design, so there is no static path a browser could hit. The caller presents a **job id**;
`AuthorizationBehavior` checks `Configuration.ExportJob.View` and org membership *before* the handler
runs; the handler then re-filters by `OrganizationId` by hand, which is what makes a cross-tenant id
a 404 rather than a leak. **The storage key is never projected onto the wire shape** — a test asserts
`ExportJobSummary` has no such property — so there is nothing for a client to hold and nothing to
guess. The completion email carries no attachment and no token-bearing link, for the same reason.

**2. Which humans?** **Any Admin of the same organization**, not only the initiator. The artifact
contains nothing an org Admin cannot already read screen by screen; restricting to the initiator
would mean a second Admin cannot retrieve a colleague's export while that colleague is on leave — a
support burden buying no real containment, since the colleague could simply generate their own.
Cross-organization is a hard no, and that is what the negative tests prove, by job id *and* by
organization id.

**One live export per organization at a time.** Not a technical limit — the runner would happily
drain a queue — but a full-tenant workbook is the most expensive artifact this app produces, and an
impatient user clicking Export four times should get one file, not four identical ones each holding a
buffered workbook in memory. A second enqueue while one is Queued or Running returns 409 naming the
condition.

---

## Permission keys

Two new keys, both **Admin-only**, and this is the least borderline derivation in
`PermissionKeys.cs`.

| Key | Admin | Member |
| --- | --- | --- |
| `Configuration.ExportJob.View` | granted | denied |
| `Configuration.ExportJob.Manage` | granted | denied |

**Manage** generates an artifact containing every Product, Contact, Account, GL line and stock
movement in the tenant, in one downloadable file — the largest single data-egress action the product
has. That is a strictly *higher* bar than `Configuration.ImportJob.Manage` (already Admin-only), not
a lower one: an import is bounded by what the uploader already knows, whereas an export hands out
everything the tenant knows.

**Note there is no `Export` key anywhere in the file to follow as precedent.** Every existing
`/reports/{x}/export` endpoint rides its own report's View key, which works only because each covers
one report. A full-tenant export spans many reports' worth of data at once, so there is no single
report key it could ride, and it needs its own.

**View** is Admin-only for two independent reasons, either sufficient. First, the PAN/phone/email
exposure that makes `ImportJobView` Admin-only: the Contacts sheet carries contact identity for the
whole tenant. Second, and unlike the import case, **View here is not merely a list — it gates the
download**, so it is the key that actually controls whether the file leaves the system. Splitting
download onto a third key was considered and rejected: a role that may generate a full-tenant dump
but not read it is not a role anyone would configure, and the extra key would only make the grid's
Download button unexplainable.

Seeded through `RolePermissionConfiguration.HasData` **before** scaffolding the migration (Phase 9's
lesson); `PermissionKeyCatalog` picks the constants up by reflection.

---

## What shipped

**Domain** — `ExportJob`, `ExportJobStatus`, `ExportCategory`; `ImportJob.ArtifactPurgedAt`;
`DocumentType.DataExport`.

**Application** — `IQueuedJobProcessor` + `JobArtifactRetention` (`Common/Jobs`);
`IExportCategoryReader`/`ExportLimits`, `IExportWorkbookWriter`/`ExportWorkbook`,
`IExportJobProcessor`, `ExportJobProcessor`, `ExportJobMapper`, `ExportCell`; five readers
(`ProductExportReader`, `ContactExportReader`, `ChartOfAccountsExportReader`,
`LedgerTransactionExportReader`, `StockMovementExportReader`); `CreateExportJobCommand`,
`CancelExportJobCommand`, `ListExportJobsQuery`, `GetExportJobArtifactQuery`;
`ImportJobProcessor.SweepAsync`.

**Infrastructure** — `QueuedJobRunnerHostedService<,>` + `QueuedJobRunnerOptions` (replacing
`ImportJobRunnerHostedService`), `ExportJobRunnerOptions`, `ClosedXmlExportWorkbookWriter`,
`ExportJobConfiguration`, migration `Phase21bExportJobs`.

**Api** — `ExportsEndpoints` (`POST /export-jobs`, `GET /export-jobs`,
`GET /export-jobs/{id}/download`, `POST /export-jobs/{id}/cancel`).

**Web** — `core/exports/export.models.ts` + `export.service.ts`; the Export half of
`configuration/import-page` (Export card with the not-a-backup notice, Export History grid with
progress, truncation banner, expiry badge, `hasArtifact`-gated Download).

**The workbook**: `Summary` (preamble + a per-category row/truncation table), `Products`, `Contacts`,
`Chart of Accounts`, `Ledger Transactions`, `Stock Movements`. Foreign keys resolved to names,
timestamps rendered on the **Nepal wall clock** through `NepalTime` (CLAUDE.md's standing rule — the
:45 offset means an evening-UTC stamp is already tomorrow in Kathmandu), numbers kept numeric,
booleans kept boolean. File name: `DataExport_{Organization-slug}_{yyyy-MM-dd_HHmm}.xlsx`, the slug
reduced to letters/digits/hyphens because an organization is free to be called "Acme / Kathmandu
(P.) Ltd." and that string reaches a `Content-Disposition` header and then a file system.

---

## Testing

**Unit** — Domain.UnitTests 192 (+7), Application.UnitTests 415 (+27), Angular 14 (+7),
Api.IntegrationTests +2.

`ExportTestHost` builds a **real DI container** for the same reason 21a's did: Decision F is an
access-control claim, and a stubbed `ISender` would make every permission assertion vacuous.
`FakeTimeProvider` throughout; **no `Task.Delay`, no `Thread.Sleep`, no real clock**.

Covered by the processor suite: one sheet per category with its headers and seeded rows (asserted
cell by cell, not "a file was produced"); **tenant isolation, asserting org B's marker is absent from
every cell of every sheet** rather than merely that A's rows are present; an **empty tenant** exports
successfully with all six sheets and their headers; the **row cap** truncates, stays `Completed`, and
is disclosed on the job, the Summary sheet and the email; the Summary sheet says the file is not a
restorable backup; the file name is stamped on the **Nepal day, not the UTC day** (asserted with a
19:00 UTC clock, which is already tomorrow in Kathmandu); a **writer failure leaves no downloadable
artifact** and the download query refuses it; an **abandoned Running job is reclaimed and
regenerated exactly once**; **cancellation leaves no artifact and no partial file**; **retention**
deletes the blob, clears the key, keeps the row, makes the download report "expired", and is
idempotent on a second sweep; the completion email goes to the initiator's own registered address.

The request suite covers: enqueue returns a Queued job and writes an **audit row**
(`Create`/`DataExport`); a second concurrent enqueue is 409; **403 for download without View, against
a nonexistent id**, and 403 for enqueue without Manage; **a member of another organization cannot
download this one's export** — by organization id (403, non-member) and by job id under their own org
(404, the handler's hand-written filter), and their listing is empty; another **Admin of the same
organization can**; cancel of a queued job; and that the listing never carries a storage key.

The reader suite covers the cap and its true-total reporting, deterministic ordering under the cap,
**the ledger reader's filter through `GlJournalEntry.OrganizationId`** (`GlLine` has no
`OrganizationId` of its own — the single most leak-prone read in the feature), and left joins
surviving a null bank.

Two import-side tests prove the inherited leak is fixed and that an unfinished import's upload is
left alone.

**`ExportWorkbookWriterTests` (Api.IntegrationTests)** is the one test that touches the real
spreadsheet library: it writes with `ClosedXmlExportWorkbookWriter` and reads the bytes back with
ClosedXML, asserting sheet names, bold headers, the Summary preamble, numeric/boolean/date cell
types, and that an empty tenant still produces a valid openable workbook. It lives there because
Application.UnitTests cannot see Infrastructure — and it **needs no Docker**, starting no container
and booting no host. The reason it exists at all is 21a's finding that ClosedXML can silently return
empty text for cells that look fine in the XML: "the processor built the right structure" is not the
same claim as "the file contains the right values".

**Angular** — `import-page.spec.ts` renders the page with stubbed services and asserts the export
half. The load-bearing case is that **Download keys off `hasArtifact`, not `status`**: a Completed
export whose file retention has deleted must not be offered. Also asserted: the not-a-backup copy is
present and the word "Backup" is never a button label; truncation is disclosed without calling the
export a failure; Cancel appears while running and Download does not; and the download goes through
the service (an authenticated `Blob` fetch) rather than a raw link.

**What the InMemory provider cannot prove** (stated in the suite's own doc comment): it enforces
neither unique indexes nor concurrency tokens, so two runners racing for one job is unreachable
there. Unlike 21a, this design does not depend on winning that race — an export is idempotent, so a
duplicate run wastes effort and orphans one blob, never producing a wrong artifact.

**Manual E2E** — two fresh Organizations, real SQL Server, the real Kestrel response, master data
seeded by curl + cookie jar:

- Org A seeded with 1 product, 2 contacts, 11 accounts, and an **approved Purchase Bill** so the GL
  and stock-movement categories had real rows. Org B seeded with three rows all named
  `ZZLEAKCANARY …`.
- `POST /export-jobs` → **201 Queued**; a second immediate POST → **409** naming the condition.
- Job completed within one 5s poll: 5 sheets, **18 rows**, 13,391 bytes, `expiresAt` exactly 7 days
  out, file name `DataExport_Phase21b-Export-Co-A_2026-09-01_1324.xlsx` — **13:24 Nepal from 07:39
  UTC**, i.e. the local-clock stamp is real.
- **Download through the real endpoint → 200**, `Content-Type:
  application/vnd.openxmlformats-…spreadsheetml.sheet`, `Content-Disposition: attachment;
  filename=…` (both the plain and `filename*` forms).
- **The downloaded file was opened and every sheet dumped.** Summary carries the organization name,
  the not-a-restorable-backup sentence, the Nepal-time stamp, the initiator, and the five-row
  category table. Products/Contacts/Chart of Accounts/Ledger Transactions/Stock Movements all carry
  their headers and their rows; the Purchase Bill's three GL lines appear with account codes and
  names (AP 904 credit / Inventory 800 debit / VAT Receivable 104 debit), and the stock movement
  appears with product, warehouse, direction, quantity, unit cost and computed value.
- **Tenant isolation, both directions**: `ZZLEAKCANARY` appears in **no cell** of org A's workbook,
  and org B's own workbook contains its canary rows and **neither** "Salted Cashew" nor "Everest
  Wholesale". Org B's export also proved the **near-empty tenant** case — Ledger Transactions and
  Stock Movements sheets present with headers and no data rows.
- **`sqlcmd`** confirmed the `exports.ExportJobs` row (status, counts, size, `ExpiresAt`,
  `StorageKey`) and the blob on disk at `src/Api/App_Data/attachments/`.
- **Retention, watched live**: `ExpiresAt` backdated on the export and `CompletedAt` backdated 8 days
  on an old import job; within one tick the attachment directory went from 14 files to 12, both
  specific blobs were gone from disk, the export's `StorageKey` was `NULL` with `ArtifactPurgedAt`
  set, and the import's key was retained with `ArtifactPurgedAt` set (as designed). The download then
  returned **404 "This export has expired…"**, and the listing showed the job still `Completed`, still
  named, with `hasArtifact: false`.
- **Cross-tenant**: A's job id under B's organization id → **404**; B's listing → empty.
- **Four 403s, each naming its exact key, against nonexistent ids so 403-not-404 proves the check
  fired before the handler**: download and list on `Configuration.ExportJob.View`, create and cancel
  on `Configuration.ExportJob.Manage`. Grants restored afterwards and re-verified 200.
- **Abandoned-run reclaim against real SQL Server**: a completed job was pushed back to `Running`
  with a 30-minute-stale heartbeat, its key cleared and fake progress written (2/99); the runner
  re-claimed it, **regenerated the workbook from scratch**, and finalised `Completed` with the true
  5 sheets / 3 rows under a new storage key. (The by-hand key removal orphaned the previous blob on
  disk — an artifact of the simulation, not a path the app takes: in production the key is only ever
  set at completion and only ever cleared by the sweep *after* deleting the file.)
- **Cancel**: create-then-cancel returned 204, the job finalised `Cancelled` with **no artifact and
  no file name**, the runner never picked it up, and a second cancel returned 409.
- **Audit**: `workflow.Audits` holds one `Create` / `DataExport` row for the job id, attributed to
  the initiating user.

**Browser pass — not performed.** This session was non-interactive and CLAUDE.md's rule is that the
user signs in themselves; nobody could. The Angular spec above covers the template logic that a
browser pass would have checked, and `ng build`/`ng test`/`tsc --noEmit` are clean, but **the screen
has not been looked at in a real browser**. Worth a few minutes of clicking on the next live session:
Start Export → the job appearing Queued with Cancel → polling to Completed → Download producing the
file. Note the page follows the two standing Angular rules (plain `signal()`s written by `(change)`
handlers rather than a `computed()` over a `FormControl`; `[selected]` per option, never `[value]`
on a select), which are the two traps a browser pass most often catches here.

---

## Bugs and snags

Nothing of the severity of 21a's wedged-job bug. Three things worth recording:

1. **`ResolveReaders` throws when a category has no registration**, deliberately — a forgotten DI
   line should fail loudly rather than quietly produce a workbook missing a sheet FR-2.8 names. The
   first two stub-based tests tripped it (registering only the one reader they cared about) and were
   corrected to fill the remaining categories with empty stubs. The guard did exactly its job.
2. **`AdjustToContents` has no `IXLRangeColumns` overload**; column sizing had to go through
   `worksheet.Columns(first, last).AdjustToContents(startRow, endRow)`, which is also what made the
   50-row sampling cap explicit rather than accidental.
3. **A whole-page text search for "Download" proves nothing on this screen**, because the import half
   has its own "Download … Template" button. The export grid and its Download button carry
   `data-testid` attributes so the spec can assert on the export section alone — without that, the
   most important UI assertion in the phase (no Download once the file has expired) passed
   vacuously.

---

## Deferred / not built

- **Confirm-live on `Organization > Developer Mode` and `Organization > Documents`** — see Step 2.
- **Date-range or per-category selection.** One button today; a date filter on the two transactional
  categories is the natural answer if the row cap bites.
- **A streaming writer.** The 25,000-row cap is ClosedXML's buffering made explicit. Raising it means
  the OpenXml SDK's SAX writer.
- **Auditing the download**, not just the generation. `AuditBehavior` fires only on
  Create/Update/Approve/Void; recording each retrieval is additive.
- **A pager on the export history grid.** The query is paginated server-side and the endpoint takes
  `page`/`pageSize`; the screen renders the first page. Same deferral as 21a's import history and
  20e's Email Logs.
- **Categories beyond FR-2.8's five.** Each is a new reader, a DI line and an enum member.
- **A restore path.** Explicitly out of scope, and the reason the artifact is called an export. If
  one is ever wanted, it is its own phase and it wants a different format (Decision B's option 2).

---

## Files of note

- `src/Domain/Exports/` — `ExportJob`, `ExportEnums`.
- `src/Application/Common/Jobs/` — `IQueuedJobProcessor` (read its doc comment before adding a fourth
  background job), `JobArtifactRetention`.
- `src/Application/Exports/` — `ExportJobProcessor` (Decisions C/D/E in its doc comment),
  `IExportCategoryReader`/`ExportLimits` (Decision B's caps), `Readers/`.
- `src/Infrastructure/Jobs/QueuedJobRunnerHostedService.cs` — the shared timer host.
- `src/Infrastructure/Exports/ClosedXmlExportWorkbookWriter.cs`.
- `src/Api/Endpoints/ExportsEndpoints.cs` — the only door the artifact leaves through.
- `tests/Application.UnitTests/Exports/`, `tests/Api.IntegrationTests/ExportWorkbookWriterTests.cs`,
  `web/src/app/features/configuration/import-page/import-page.spec.ts`.
