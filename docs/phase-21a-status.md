# Phase 21a — Async job foundation + bulk import (FR-2.9, NFR-4.3)

## TL;DR

Bulk import from a spreadsheet, running as a durable background job. This codebase's **second**
background job (`ImportJobRunnerHostedService`, a deliberate copy of Phase 20e's shape rather than
an extension of it), and the first one that **writes** — which forced the identity question 20e was
able to sidestep entirely.

**Decision B is the phase.** A scheduled alert only reads, so `AlertDispatcher` could send no MediatR
request at all. An import creates Products and Contacts, and every rule about doing that right lives
in the existing Create/Update handlers and their six-behavior pipeline. So the job **reuses those
commands** and re-assumes the initiating user's identity through a new scoped `IJobActingUser`. That
is not a fabricated principal: the user was authenticated and permission-checked by the real HTTP
request that enqueued the job, and because the commands travel the normal pipeline,
`AuthorizationBehavior` **re-checks the permission on every single row at execution time**. An
`HttpContext` always wins in `CurrentUserService`, so a background identity can never serve a real
request.

**Decision C** is at-most-once **per row**, not per job: an `ImportJobRow` is committed under a
unique index on `(ImportJobId, RowNumber)` *before* the row's command is sent — Phase 20e's
claim-then-act idiom, applied one level down. A crash at row 500 of 1,000 resumes at 501 and creates
nothing twice. Partial success is a **`Completed`** job; `Failed` means only that the *file* could
not be processed.

**Three of the reference product's seven upload types ship** — Product, Customer, Supplier — with
create and update modes both wired. Confirm-live corrected the brief on one point that would have
produced the wrong importer: the product's "Contact" upload type is **not** our `Contact` aggregate
but a person attached to a Customer/Supplier, i.e. `ContactPersonnel` (Phase 18).

**Two bugs that only manual E2E could find**, both now fixed: an optimistic-concurrency token on
`ImportJob` made the user's own Cancel collide with the runner's progress write and wedge the job
until its lease expired (removed — see Decision C's *Bug 1*); and stranded row outcomes were counted
before they were committed, so an interrupted row was reported as neither succeeded nor failed
(caught by a unit test, *Bug 2*).

Tests: Domain.UnitTests 185 (+8), Application.UnitTests 388 (+37), Angular 7 (unchanged).
`dotnet build` / `ng build` / `ng test` / `tsc --noEmit` clean. Manual E2E against two fresh
Organizations with real SQL Server and real uploaded `.xlsx` files proved the create/update/partial
-success/tenant-isolation/resume/cancel paths and the 403s.

---

## Scope: 21a of three

The roadmap lists Phase 21 as four numbered items. This session adopted the recommended split into
three independently shippable sub-phases and shipped **21a only**:

- **21a — async job foundation + bulk import (FR-2.9, NFR-4.3). ← this document.**
- **21b — full-tenant backup/export (FR-2.8).** Reuses this phase's runner; mostly breadth.
- **21c — migrated tax-register import + the migrated Sales/Purchase Register variants (FR-2.10,
  closing FR-9.4).** Architecturally distinct and untouched here — verified again during this
  session that `SalesRegisterQueryHandler`/`PurchaseRegisterQueryHandler` read live documents only,
  with no migrated flag, table or variant of any kind.

The split held up: 21a alone is the only one that forces the identity decision, and it turned out to
be the whole intellectual content of the phase.

---

## Step 2 — confirm-live findings (Tigg UAT, Configurations > Import / Export)

The wizard was opened live and its client bundle read, which answered more than clicking could.

### The Upload Type list is exactly seven, and it is conditional

`Product, Customer, Supplier, Contact, Account, Product Category, Account Group` — the module scan's
list is exhaustive and correct. One thing the scan missed: **Product is removed from the list
entirely for a tenant without `track_inventory`**, which is the reference product's own version of
Phase 20f's feature gating.

### "Select action" has exactly two options, and not for every type

`Update Existing Records` / `Create New Records`. **Product Category and Account Group offer Create
only** — a real asymmetry, not an oversight, and worth honouring if those two are ever built here.
The five other types offer both.

### The wizard is four steps and synchronous — not a background job at all

The scan recorded two steps. It is four, and the shape matters:

1. **Upload Type** (type + action) → Next
2. **Upload your file** (drag-and-drop, "Download X Template", *"This process might take few minutes
   depending upon size of file. Do not refresh this page while uploading."*)
3. **Validating Records** — a server-side **dry run**. The upload posts to a `dry_run_file` endpoint
   with a **20-minute client timeout** and returns `{statements, errors}`. The screen shows
   *"N records validated"* and *"N records have errors"* as two expandable sections, the second
   rendering each error as **`Row: {LineNo} {Header} {Message}`**. Nothing has been written yet.
   Buttons: **Confirm Upload** / **Reupload New File**.
4. **Upload successful.**

Confirm Upload posts the **parsed rows** (not the file) to a second endpoint —
`products-multi-add` / `products-multi-edit` / `accounts-multi-add` / … — so the server parses once,
hands the rows back, and takes them again on commit.

**There is no job/history screen, no progress bar, and no cancel anywhere.** The "async processing"
the scan noted is just the don't-refresh warning.

### What update-existing mode matches on

Not an id column. In update mode the template link is replaced by **"Download Full {X} List
(.xlsx)"**, which exports the tenant's existing records **including their Code column**; the user
edits that file and re-uploads it. **Code is the natural key**, which is exactly what the templates'
own instruction implies: *"Code field should be blank if customer code is set to auto in
configuration"*.

### The actual template columns (all seven downloaded and read)

This was the single highest-value item in Step 2, and guessing any of it would have been wrong.

| Type | Columns (`**` = required) |
| --- | --- |
| **Product** | Product Code, HS Code, **Product Type**, **Product Name**, **Category**, **VAT Applicable**, **Primary Unit**, Selling Price, Purchase Price, Sales Account, Sales Return Account, Purchase Account, Purchase Return Account, Valuation Method, Reorder Level, Track Inventory, Opening Quantity, Opening Rate, SKU, Available For Sale |
| **Customer** | Code, **Customer Name**, Contact Group, Phone No, Email, Address, PAN, Credit Limit, Credit Term, Opening Balance, Opening Balance Type |
| **Supplier** | *identical to Customer*, with **Supplier Name** |
| **Contact** | Code, **Contact Name**, Contact Group, Phone No, Email, Address, **Organisation**, **Title** |
| **Account** | Code, **Account Name**, **Account Group**, Opening Balance, Opening Balance Type, Description |
| **Product Category** | **Category Name**, Parent Category, Description |
| **Account Group** | **Name**, Description, **Parent Group**, **Primary Group** |

Every template carries a sample data row and an instruction block a few columns to the right. The
instructions settle the semantics:

- **Foreign keys are expressed by name, never by id** — *"'Category' should exactly match with
  Product Category name in the existing product category list"*, and the same for Primary Unit,
  Account Group and Contact's Organisation.
- **`Product Type`: "Goods" or "Service"**; **`VAT Applicable`: "Yes" if 13% VAT and "No" if VAT not
  applicable**; **`Valuation Method`: "Weighted Average" or "FIFO"**; **`Opening Balance Type`: DR or
  CR**.
- *"Note: Do not change Column Header and their position."*
- Product Category / Account Group add: *"Parent category should already exist or should be in the
  upcoming rows"* and *"Make sure there are no cyclic dependencies"* — i.e. those two importers need
  intra-file ordering logic the other five do not. A good reason they are deferred here.

### The correction that mattered most

**Tigg's "Contact" upload type is not our `Contact` aggregate.** Its columns are Code / Contact Name
/ Contact Group / Phone / Email / Address / **Organisation** / **Title**, with the instruction
*"'Organisation' should exactly match with customer or supplier name in the existing contact list"*.
That is a person attached to a Customer or Supplier — `ContactPersonnel` (Phase 18) in this
codebase, a different aggregate entirely. The kickoff's grounded finding #8 assumed
"Customers, Suppliers, Contacts" was one importer over `ContactType`; it is one importer over
`ContactType` for **Customer and Supplier**, plus a separate `ContactPersonnel` importer that is now
explicitly deferred rather than accidentally half-built.

### `Organization > Backup` — notes for 21b (not built)

There is no "Backup" screen. `Configurations > Organization` has six tabs — Overview, Tasks,
Documents, Features, **Migration**, Developer Mode — and **Migration** is a **"Migrated Reports"**
panel listing *Sales Register* and *Purchase Register* with an **IMPORT** button. So:

- **FR-2.10 / 21c lives here**, on its own screen, entirely separate from Import / Export. Its two
  report variants are named exactly as FR-9.4 promises.
- **FR-2.8's full-tenant backup has no counterpart in the reference product at all.** 21b will be
  designing rather than mirroring, and should re-confirm before assuming a shape.

---

## Decision A — how to generalize the job runner

**A second dedicated `BackgroundService` polling its own `ImportJobs` table.** Not a shared generic
runner with an `IJobHandler` registry, and not an in-process `Channel<T>`.

- **`Channel<T>` was rejected outright**: an import enqueued a second before a deploy would vanish
  with no trace and no way for the user to learn it never ran. Durability is not optional here, and
  the durable row is also what makes Decision C's resume story expressible at all.
- **A generic job framework was rejected on Phase 20e's own scope-control lesson**, which the
  kickoff flagged as applying twice as hard here. Alerts and imports share a *shape* and nothing
  else: alerts are schedule-driven, idempotent and answer "what is due right now"; imports are
  queue-driven, not idempotent, long-running and cancellable. The only code a shared runner would
  actually deduplicate is the six lines of timer-and-scope management. Building a handler registry
  for two consumers — one of which does not exist yet — buys a second schema and an abstraction to
  maintain in exchange for nothing.
- **The seam is `IImportJobProcessor`**, one method, `Task<bool> ProcessNextAsync(...)`. 21b and 21c
  can either add a job kind behind an equivalent seam or reuse this table; the choice is deferred to
  when there is a second consumer to look at, which is the point.

`ImportJobRunnerHostedService` copies 20e's three not-optional details verbatim, and for the same
reasons: **a DI scope per job** (`IAppDbContext`, `IFileStorage` and `IEmailSender` are all scoped
and a singleton cannot hold them), **`IOptionsMonitor` not `IOptions`** (phase-20g's caching trap),
and a tick whose exception is **logged and swallowed** so the loop survives. One thing it adds that
20e did not need: the tick **drains** — it keeps calling the processor until nothing is left, so
three queued imports do not wait three poll intervals. The poll interval is **5 seconds**, not 60:
someone is watching a progress bar.

---

## Decision B — the acting identity

**The job reuses the real `CreateProductCommand` / `UpdateProductCommand` / `CreateContactCommand` /
`UpdateContactCommand` through the full pipeline, acting as the user who enqueued it.**

### Why the alternative was worse

Bypassing MediatR would mean reimplementing, in a parallel import-only write path: document-number
generation, foreign-key existence checks, every FluentValidation rule, the `Audit` row, and the
permission check itself. That path would then drift from the real one silently — the classic way a
bulk importer ends up writing records the UI could never have created.

### What makes it defensible

The identity is **not fabricated**. `ImportJob.InitiatedByUserId` is captured from the real,
authenticated, permission-checked HTTP request that enqueued the job. The job is that user's own
action, deferred. Recording it is honest provenance, and `AuditBehavior` then attributes every
imported row to them by name — proven live (`Imported_contacts_are_audited_against_the_user_who_started_the_import`,
and the `Audits` rows in E2E).

### How it is scoped, and what contains the danger

`IJobActingUser` (Application) is a **plain scoped service**, not an `AsyncLocal`:

- Only code holding that exact instance can call `Assume` — the processor, on a scope it created
  one line earlier, per row. There is no ambient channel for unrelated code to set it through.
- **An HTTP request can never be served by it.** `CurrentUserService` consults it *only* when
  `HttpContextAccessor.HttpContext` is null. Inside a request the JWT wins unconditionally, and a
  request with a malformed `sub` still throws rather than falling through. Even a hypothetical rogue
  `Assume` call in a request scope changes nothing.
- Assignment is **single-shot** per scope: a job cannot switch users mid-run.
- It grants **no permission of its own**. It names *who* is acting; *whether* they may still act is
  re-derived from the database on every command.

**Residual risk, stated:** a bug in the runner that assumed the wrong user id would perform writes as
that user. The mitigation is that the id is read from the job row the runner just claimed and is
never derived from anything client-supplied.

### Is permission re-checked at execution time? **Yes, per row.**

This falls out of reusing the pipeline rather than being bolted on. A user removed from the
organization, or stripped of `Catalog.Product.Manage`, between enqueue and run has their job
**stopped**, not honoured. The processor treats `ForbiddenException` as a whole-job condition rather
than a row error — every remaining row would fail identically, so producing N copies of one message
would be noise. The job ends `Failed` with *"The user who started this import no longer has
permission to perform it: … (Catalog.Product.Manage)"*.

Note the corollary: **`Configuration.ImportJob.Manage` does not replace the per-entity key.** A user
holding only the import permission imports nothing.

---

## Decision C — semantics for a job that is not idempotent

### At-most-once per **row**, claim-then-act

An `ImportJobRow` is inserted and committed under a **unique index on `(ImportJobId, RowNumber)`**
*before* the row's create/update command is sent. This is Phase 20e's `AlertSendLog` idiom applied
one level down, and it does three jobs at once:

- **Resume after a crash**: the claims survive the process, so the resumed run skips them. No row is
  ever created twice.
- **Multi-instance**: a second runner's insert violates the index; it catches `DbUpdateException`,
  detaches and skips.
- **At-most-once**: the row being processed at the instant of a crash has an unknown outcome and is
  reported as such, never guessed at.

Rejected alternatives:

- **One all-or-nothing transaction** cannot express FR-2.9's required partial success, and would hold
  a write transaction open across thousands of commands.
- **Fail-and-require-a-fresh-upload** throws away work the user can already see succeeded.

### The status model, and why `Failed` is narrow

Partial success is the **normal** outcome, so `Completed` is reached whether or not rows were
rejected: *"3 rows failed, 997 created"* is a **successful** job. `Failed` means the job could not
process rows *at all* — file unreadable, columns not matching the template, no data rows, or the
permission revoked. Anything a single row can do to itself is a row outcome, never a job outcome.
`Cancelled` is its own terminal state.

Rows left `Pending` by a dead run are converted at finalisation to `Failed` with *"The import was
interrupted before this row's outcome could be recorded; re-upload this row"* — the honest answer,
and it names exactly which rows to re-upload.

### Cancellation

`CancelImportJobCommand` raises a flag; the runner reads it between rows (every 10, alongside the
heartbeat write) and never aborts mid-command, because a command's own transaction is the smallest
safe unit. A `Queued` job is retired immediately since no runner will see the flag.

**Nothing is rolled back.** Rows already applied are real Products and Contacts that other records
may already reference; deleting them would be a larger and less reversible surprise than stopping
where the user asked. The counts say exactly how far it got. Verified live: cancelled at 210 of
4,000, 210 products kept, 210 row claims, job `Cancelled`.

### Bug 1 — the concurrency token that wedged the job (found only by manual E2E)

`ImportJob` originally carried a `RowVersion` concurrency token so two runners could not claim the
same job. It works for claiming and breaks everything else, because **this row has a second entirely
legitimate writer**: the user's own cancel command. SQL Server bumps a rowversion on any UPDATE, so
cancelling a running import invalidated the runner's token and its very next progress write died
with `DbUpdateConcurrencyException`. The job wedged in `Running` until its 2-minute lease expired.

No unit test could have caught this — **the InMemory provider does not enforce concurrency tokens at
all**, so the conflict is unreachable there.

**The fix was to delete the token, not to handle the conflict.** Job-level claiming was never the
correctness mechanism: `ImportJobRow`'s unique index is. Two runners on one job interleave, each
skipping rows the other claimed, and both finalise to the same counts — duplicated effort, never a
duplicated Product. That is precisely Phase 20e's position, where the ledger index (not any lock) is
what makes a send happen once. Claiming is now **advisory**, and the heartbeat lease is what stops a
crashed job being stranded.

### Bug 2 — counts computed before they were committed

`FinalizeAsync` marked stranded `Pending` rows `Failed` and then called `ApplyCountsAsync`, which
aggregates **in the database**. The outcome was still sitting in the change tracker, so an
interrupted row was counted as neither succeeded nor failed. Caught by
`A_row_left_claimed_by_a_dead_run_is_reported_as_interrupted`. Fixed by committing before counting.

---

## Decision D — the parsing library and where it lives

**ClosedXML, moved into `Infrastructure`, behind an Application-layer `IImportFileReader`.**

ClosedXML 0.105.1 was referenced by `src/Api` only, and `ReportSpreadsheetExporter` is write-only.
The runner is a hosted service and nothing may depend on `Api` but `Program.cs`, so the reference was
added to `Infrastructure` rather than borrowed. Reusing this codebase's existing spreadsheet
dependency (chosen over the OpenXml SDK and NPOI in Phase 16c, reasoning recorded there) costs
nothing and avoids a second library to secure and update.

**The seam is `ImportSheet` — headers plus untyped string cells, and nothing else.** That is what
keeps the phase testable: everything worth testing (column mapping, required-field rules,
name-to-id resolution, numeric/boolean/enum coercion, per-row error text) lives in Application and
runs against a hand-built sheet with no file at all. `ClosedXmlImportFileReader` does only
bytes → strings.

**On buffering (the NFR-5.1 question, answered rather than dodged):** ClosedXML is not a streaming
reader — `XLWorkbook` materialises the whole sheet. Rather than pretend otherwise, the limits are
stated and enforced: **5,000 data rows** and **10 MB**, both rejected with a message naming the cap
and suggesting smaller files. A rejected 6,000-row upload beats an accepted one that exhausts the
server. `.xls` is rejected at upload validation rather than accepted and failed minutes later.

Two related notes:

- The reader takes every cell via `GetFormattedString()`, not `Value`: a date or a code typed into a
  cell must arrive as the text the user sees, not an OLE serial number, and `"P0062"` must not become
  a number.
- Trailing header-less columns are dropped, because the reference templates park their instruction
  text several columns to the right of the grid (column M against an 11-column grid) and
  `RangeUsed()` includes it.

### One template definition drives both directions

`ImportTemplateDefinition` is pure data owned by the importer. The Api renders it to `.xlsx`
(`ImportTemplateWriter`, buffering through a `MemoryStream` because Kestrel disallows synchronous
writes to the live response — phase-16c bug #3), and the **same** `Columns` list is what the parser
validates an upload's headers against. A template that can drift from its parser is the most likely
way a bulk importer is wrong in a way no test notices, so they are not allowed to be two lists.

**Header matching is by name, not position** — only half of the reference product's *"Do not change
Column Header and their position"* is enforced. Requiring exact ordering would reject files that are
unambiguously correct, and an ordering-sensitive parser silently imports the wrong column into the
wrong field when someone inserts one. Unrecognised extra columns are ignored so a user's own notes
column does not fail the file. The `**` required marker is presentation only.

---

## Decision E — what "notified on completion" means (NFR-4.3)

**Both, and each for its own case.** In-app polling on the job screen is the primary answer for the
user who stayed; a completion **email to the initiating user's own registered address** covers the
one who walked away, which is what NFR-4.3 actually says.

The email reopens none of Phase 20e's Decision B concerns, and the distinction is the point: 20e's
alert recipients are **unvalidated free text**, and that egress risk drove its entire permission
derivation. Here the address is looked up server-side from `InitiatedByUserId`. Nothing about the
recipient is caller-supplied, so there is no egress surface to reason about.

A failure to notify never fails the job — the outcome is already durably recorded and on screen, and
turning a successful 997-row import into an error because SMTP was down would be indefensible.

Polling, not a socket: a job's status is a cheap indexed read, and a push channel for one screen
would be a deployment concern in exchange for a couple of seconds of latency. The screen polls every
2 seconds **only while something is actually active**, and stops otherwise — an idle Configurations
tab must not sit hitting the API forever.

---

## Permission keys

`Configuration.ImportJob.View` and `Configuration.ImportJob.Manage`, **both Admin-only**, seeded
through `RolePermissionConfiguration.HasData` before the migration was scaffolded (phase-9's lesson).

- **Manage** is the easy half: enqueuing an import mutates master data at scale, in one action, under
  an identity the background runner re-assumes. That is a strictly larger capability than the
  per-record `Catalog.Product.Manage` / `Contacts.Contact.Manage` keys it then exercises, and sits
  with Phase 20d/20e's control-plane bar rather than the Member-View-by-default lookup norm. It does
  **not** replace the per-entity key (see Decision B).
- **View** is Admin-only for a reason specific to this feature rather than by symmetry: a job's
  row-level error report **quotes the uploaded file's own values back to the reader** (*"No record
  with code 'C0007' exists"*, *"Contact group 'VIP' does not exist"*), and a Customer/Supplier upload
  carries PAN, phone and email. The job list is therefore a partial view of whatever contact identity
  data was uploaded — the same exposure that makes the flat per-transaction registers Admin-only.

`GetImportTemplateQuery` is gated on **Manage**, not View: a template is only useful to someone who
can actually run an import, and it is the entry point to the whole flow.

---

## What shipped, by entity type

| Reference product's type | Status here | Note |
| --- | --- | --- |
| Product | **Shipped**, create + update | Richest column set; most name-resolved foreign keys |
| Customer | **Shipped**, create + update | One importer with Supplier, over `ContactType` |
| Supplier | **Shipped**, create + update | |
| Contact | Deferred | **Is `ContactPersonnel`, not `Contact`** — see confirm-live |
| Account | Deferred | Mechanical: a new `IEntityImporter` + one DI line |
| Product Category | Deferred | Needs intra-file parent ordering + cycle detection; Create-only in the reference product |
| Account Group | Deferred | Same, plus the fixed Primary Group list |

Columns deliberately **absent** from this codebase's templates, each because there is nowhere to put
them:

- **Product**: `Sales Account` / `Sales Return Account` / `Purchase Account` / `Purchase Return
  Account` (`CreateProductCommand` does not take them — update mode preserves whatever is already
  there rather than blanking it); `Valuation Method` (`Product.Create` does not accept one);
  `Opening Quantity` / `Opening Rate` (opening stock is `OpeningStockLine`, a separate day-zero
  transaction with its own GL consequences — folding it in would have this importer quietly writing
  inventory); `SKU` (no such field).
- **Customer/Supplier**: `Credit Limit` and `Credit Term` (`Contact` has neither field).
- **Customer/Supplier**: `Opening Balance Type`, for a more interesting reason.
  `Contact.OpeningBalance` is a single non-negative decimal whose DR/CR side is *derived* from
  `ContactType` by `ContactLedgerReader.BalanceType`. A DR/CR column would be either redundant with
  the upload type or, when it disagreed, unrepresentable. Accepting a column the model cannot honour
  is worse than not offering it.

Also deliberately **not** built: the reference product's **pre-commit dry-run review step**. It
cannot satisfy NFR-4.3 (it is synchronous with a 20-minute client timeout), and its information is
carried after the fact by the per-row results grid. Restoring it on top of this design is purely
additive — a validate-only mode plus a confirm command — and is deferred, not dismissed.

---

## Testing

**Unit** — Domain.UnitTests 185 (+8), Application.UnitTests 388 (+37).

The import suite is the one place in these tests that builds a **real DI container**
(`ImportTestHost`) rather than constructing handlers by hand. That is load-bearing: the processor
creates a scope per row, assumes an identity in it, and sends commands through the **full six-behavior
pipeline**. The two most important claims this phase makes — permission re-checked per row, and a
row's failure being the real validator's message rather than something the importer invented — are
only true if the pipeline actually runs. Stubbing `ISender` would make those tests vacuous. All
scopes share one named InMemory database, exactly as they would share SQL Server.

`FakeTimeProvider` throughout; **no `Task.Delay`, no `Thread.Sleep`, no real clock anywhere**.

Covered: happy path; partial success with row numbers and column names; wrong columns (fails fast,
names them, touches no row); empty file; unreadable file (reader's own message, not a
`NullReferenceException`); a file repeating a key inside itself; update-updates-and-does-not-create;
update-does-not-create-for-an-unmatched-code; immutable `ProductType` rejected; **tenant isolation**
(org A cannot match or update org B's record by code); a Supplier import cannot reach a Customer;
**resume creates no duplicates**; an interrupted row is reported, not retried; cancellation keeps
what landed; a revoked permission stops the job at the first row; audit attribution; completion
email. Plus the full cell-coercion matrix (thousands separators, non-numeric price, fractional
integer, Yes/No spellings, unrecognised boolean, absent optional column, invalid choice).

**What the InMemory provider cannot prove** (stated in the suite's own doc comment): it does not
enforce unique indexes, so the two-runners-race path is unreachable there — and, as Bug 1 showed, it
does not enforce concurrency tokens either. Both were verified against real SQL Server.

**Manual E2E** — two fresh Organizations, real SQL Server, real `.xlsx` files through the real
endpoint, master data seeded by curl + cookie jar:

- Template download: **HTTP 200**, correct content type and `Content-Disposition`, headers with `**`
  markers, sample row and instruction block — the Kestrel synchronous-write constraint handled.
- Upload: **HTTP 201** — `.DisableAntiforgery()` confirmed working (without it every upload 500s;
  no unit test touches real Minimal API metadata).
- Mixed 4-row Product file: `Completed`, 2 imported / 2 rejected, errors at **row 3 (Category)** and
  **row 4 (Product Name)** — spreadsheet row numbers, header included. Verified by `sqlcmd`.
- Update mode: `0002` renamed and repriced **in place** (same code); a Service row declared Goods
  rejected; code `9999` rejected **and not created** (product count unchanged).
- Supplier import: contact group resolved by name, blank group → NULL, decimal opening balance,
  bad group name → row error — and an **invalid email surfaced as `column: Email`, message
  `'Email' is not a valid email address.`**, i.e. the real `CreateContactCommandValidator` running
  inside the pipeline under the assumed identity.
- **Unique index**: a hand-written duplicate `(job, rowNumber)` INSERT was rejected by
  `IX_ImportJobRows_ImportJobId_RowNumber` — the thing InMemory cannot do.
- **Resume**: a completed job was pushed back to `Running` with a stale heartbeat and one row claim
  deleted; the runner re-claimed it, skipped the three claimed rows (**no duplicate products**),
  redid only the unclaimed one, and finalised `Completed`.
- **Cancel**: a 4,000-row import cancelled at 160 rows finished `Cancelled` at 210 processed, with
  210 products and 210 row claims kept. (This is the run that exposed Bug 1 on the first attempt.)
- **403s, against nonexistent ids so 403-not-404 proves the check fired first**:
  `Configuration.ImportJob.View` on the job endpoint and `Configuration.ImportJob.Manage` on the
  template endpoint, each naming its exact key.
- Throughput sanity: 600 rows in ~4 seconds through the full pipeline.

**Browser pass** (the user signed in; this session never enters credentials) — on the phase's own new
screen, against the same seeded organization:

- The history grid renders live status badges, progress bars, per-job counts and the initiator's
  name; **Show errors** expands the row grid inline (*Row 2 · Email · "'Email' is not a valid email
  address."*).
- **The zoneless path is clean**: changing the Upload Type select re-labels the template button
  ("Download **Supplier** Template") immediately — the plain `signal()` written by `(change)` rather
  than a `computed()` over a `FormControl`, which is the Phase 17 trap this would otherwise have hit.
  Selects bind `[selected]` per option, never `[value]`.
- **Template download** issues `GET /import-templates/Supplier → 200` as an authenticated `Blob`
  request (not an `<a href>`), with no console errors.
- **Upload through the UI**: `POST /import-jobs?entityType=Supplier&mode=CreateNew → 201`, the job
  appears **Queued** with a Cancel button, polls to **Completed** (1 imported / 1 rows) with the
  progress bar filled and Cancel gone.
- **Polling stops when nothing is active** — three polls after the upload, then silence. An idle
  Configurations tab does not sit hitting the API.

---

## Files of note

- `src/Domain/Imports/` — `ImportJob`, `ImportJobRow`, `ImportEnums`.
- `src/Application/Imports/` — `IImportJobProcessor`/`ImportJobProcessor` (every decision),
  `IEntityImporter` + `ProductImporter`/`ContactImporter`, `IImportFileReader`/`ImportSheet`,
  `ImportRowReader`, `ImportTemplateDefinition`, commands and queries.
- `src/Application/Common/Security/IJobActingUser.cs` — Decision B, with the full risk statement.
- `src/Infrastructure/Imports/` — `ImportJobRunnerHostedService`, `ImportJobRunnerOptions`,
  `ClosedXmlImportFileReader`.
- `src/Api/Endpoints/ImportsEndpoints.cs`, `src/Api/Reports/ImportTemplateWriter.cs`,
  `src/Api/Services/CurrentUserService.cs` (the job fallback, and why HttpContext wins).
- `web/src/app/features/configuration/import-page/`, `web/src/app/core/imports/`.

---

## What 21b and 21c inherit, and what they must still add

**Inherited, ready to use:**

- The **queue-driven runner**: `ImportJobRunnerHostedService`'s drain-per-tick loop, scope per job,
  `IOptionsMonitor`, swallowed tick failures.
- The **claim-under-unique-index** idiom at row granularity, plus the heartbeat-lease resume.
- **`IJobActingUser`** — the identity question is now answered and the mechanism exists. 21b's export
  is a *read*, so it may well not need it (20e's "no ambient identity" default still applies to
  read-only jobs); 21c's migrated-register import is a write and will.
- **`IImportFileReader`/`ImportSheet`** for anything that parses a workbook, and
  `ImportTemplateDefinition` + `ImportTemplateWriter` for anything that hands one out.
- The **status model** — in particular the `Completed`-with-failures vs `Failed` distinction, which
  any partial-success job wants.

**Still to build:**

- **21b** needs an *output* payload (an export produces a file to download, where an import consumes
  one), and a decision on whether it joins `ImportJobs` or gets its own table — deliberately not
  pre-decided here. Note from confirm-live that **the reference product has no backup screen at all**,
  so 21b is designing, not mirroring.
- **21c** needs migrated-register storage that appears in statutory reports **without existing as
  documents and without touching GL** — still 100% unbuilt, re-verified this session. Its home in the
  reference product is `Configurations > Organization > Migration`, a separate screen from
  Import / Export, with `Sales Register` / `Purchase Register` and an IMPORT button.
- **Neither inherits a generic job framework, because none was built.** That remains the right call
  until there is a second consumer to look at.

## Deferred / not built (mechanical follow-up)

- **Four of the seven entity types** (see the table above). Account is a new `IEntityImporter` plus a
  DI line; Product Category and Account Group additionally need intra-file parent ordering and cycle
  detection; Contact is really `ContactPersonnel`.
- **The pre-commit dry-run review step** — additive, and explicitly a trade against NFR-4.3.
- **A pager on the import history grid.** The query is paginated server-side and the endpoint takes
  `page`/`pageSize`; the screen renders the first page. Same deferral as Phase 20e's Email Logs.
- **Retry of a failed row.** Out of scope by Decision C, not an oversight: the user re-uploads the
  rejected rows, which is also how the reference product works.
- **A "download the errors as a file" action.** The reference product shows errors inline only; so
  does this.
