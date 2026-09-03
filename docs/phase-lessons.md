# Phase lessons — the "read this before touching X" index

One paragraph per phase, moved verbatim out of `CLAUDE.md` (2026-09-02) so the root file stays small.
Each bullet names the situation in which a future session must open that phase's status doc first,
and summarises the lesson it will find there. The full history (scope decisions with reasoning, bugs
hit and fixed) is in the corresponding `docs/phase-N-status.md`; the recurring gotchas distilled from
these are in `CLAUDE.md`'s Known gotchas section. When a phase completes, append its paragraph here.

- `phase-1a` — before touching auth/config wiring (eager-config-read, JWT claim mapping, cookie gotchas)
- `phase-2` — before writing any raw-SQL EF Core query (`Database.SqlQuery<T>` composability, generic-LINQ translation)
- `phase-3` — before building an Angular component serving both `.../new` and `.../:id` on one route (route-reuse)
- `phase-4` — before replacing an encapsulated child collection wholesale in a handler (InMemory-provider mistracking)
- `phase-5`/`phase-6`/`phase-7` — the `[value]`-vs-`@for` native-`<select>` race, escalating from display glitch to wrong persisted data
- `phase-6` — before building any "Convert to X" flow or "reverse of X" posting rule (conversion enforcement, net-effect tracing)
- `phase-8b` — before writing a report-test suite that seeds documents through real Create/Approve handlers (DraftCode vs real code, shared fake-number-generator collisions)
- `phase-8c` — before seeding a Goods-type test Product (pre-existing 422 stock-warning / 409 missing-COGS-account behavior)
- `phase-8f` — the confirm-shape-live-before-building precedent (speculative Annex 5 design vs the real screen: nothing in common)
- `phase-9` — before writing a generic `IQueryable<T>` helper taking a `Func` selector, or hand-writing a permission-seed migration instead of updating `RolePermissionConfiguration.HasData` first
- `phase-11` — environment gotchas for scripted manual E2E (bracket-quoting SQL Server's `identity` schema, `dotnet run` launch-profile/`Secure`-cookie interaction)
- `phase-12` — why every `IOrganizationScoped` request must implement `IRequirePermission` (`AuthorizationBehavior` is the only org-membership check)
- `phase-13` — before naming a Domain type after a common BCL word (`Task`/`TaskStatus` collision → `WorkTask`)
- `phase-14` — `PermissionKeyCatalog` reflects over `PermissionKeys.cs`, so new key constants are auto-discovered; register/verify E2E snags
- `phase-16a` — the Void lifecycle's reversal mechanism (`GlJournalEntry.PostReversalOf`, `IStockLedgerService.ReverseIncrementAsync`) and `LockDateBehavior`'s two-marker-interface split — read before building any future document type's own reversal, or before running `dotnet ef` right after a single-project (not full-solution) rebuild
- `phase-16b` — before adding any per-line/per-document adjustment field (discount, future surcharge/rounding): the "fold every adjustment into the stored `Line.Amount`/`VatAmount` so GL/report code needs zero changes" pattern, and the "confirm live which GL account it posts to before writing any posting-rule code" precedent (discount turned out to have none)
- `phase-16c` — before adding a footer/summary total to any paginated screen (it must come from a server-computed field over the *full* filtered set, never a client-side reduce over the current page — a bug this phase found in four pre-existing report pages); before writing any file-download endpoint (Kestrel disallows synchronous writes to the live response stream — ClosedXML/any sync-only writer must target a `MemoryStream` first, then `CopyToAsync` the real stream)
- `phase-18` — before designing a second polymorphic (ParentType, ParentId) entity: confirm live whether it's really the same concept as an existing one (`Attachment` vs. `WorkTask`) before reusing its enum — Decision #2's Contact-Documents-vs-Workflow-Document split; before assuming a new "sub-record of a Contact" needs Phase 4's full-collection-replace treatment — confirm live whether the real UI even submits it as a list (`ContactPersonnel` didn't, so it's a standalone entity like `WorkTask`/`Deal`, sidestepping the gotcha by design); before writing any Minimal API endpoint that binds `IFormFile` (needs `.DisableAntiforgery()` — see CLAUDE.md's Known gotchas)
- `phase-20a` — before building a second "cross-cutting data attached to a document" editor (custom fields, reporting tags, or similar): confirm live whether it saves inline with the document's own Save action (Custom Fields) or as its own independent post-creation action (Reporting Tags, Phase 19) — the two aren't the same shape and the live reference product answers this per-feature, not by analogy to the last one built
- `phase-20b` — a third "cross-cutting data attached to a document" shape: confirm live *where the control even renders* before assuming it's on the detail page at all — Custom Status turned out to live only in the document's LIST grid (a per-row column, saving instantly, no detail-page presence whatsoever), which neither 20a's nor Phase 19's shape anticipated; also, before assuming a lookup's candidate document-type list from the module scan is accurate, check live whether each candidate type actually has the picker (Invoice didn't, despite being assumed) and whether any candidate's pipeline is genuinely orthogonal to the native lifecycle (Cheque's wasn't — its custom-status values matched its native lifecycle enum exactly, a sign to exclude rather than force-fit)
- `phase-20d` — before building a screen the module-scan flagged as a "gallery of named layout variants," confirm live whether picking one is a *choice* or the gallery's "Add Template" actually opens a real visual toggle/canvas editor (Printing Templates turned out to be the latter, and got descoped to metadata-only by explicit user decision rather than built) — the same confirm-live-before-assuming discipline as `phase-8f`'s Annex 5 lesson, now applied to "this looks like a simple lookup" instead of "this looks like a report"; also the precedent for picking a PDF-rendering approach (QuestPDF, in-process, over a headless-browser pipeline) as an explicit recorded decision rather than a default
- `phase-20e` — before adding **any** background job, or any second one: the runner/decider split (a
  `BackgroundService` that owns only the timer, the per-tick DI scope and `IOptionsMonitor`, driving an
  Application-layer service that owns every decision behind an injected `TimeProvider`), and the
  "claim a ledger row under a unique index, *then* do the external side effect" idiom that gives
  idempotency-across-restart and multi-instance safety in one move. Also the precedent for **not**
  giving a job an identity at all — the anticipated authentication-bypass surface was avoided rather
  than narrowed, by having the job read through a purpose-built service taking an explicit
  `OrganizationId` instead of sending a MediatR request
- `phase-20f` — before gating anything behind a tenant feature flag: check whether a flag-*off* tenant can still function with the gate on. `MultipleWarehouses` had to become a **cap at one** rather than an on/off block, because nothing seeds a default Warehouse at Organization creation and Invoice/PurchaseBill both require a `WarehouseId` — blocking creation outright would have left such a tenant permanently unable to invoice. A conditional gate like that can't ride a marker-interface pipeline behavior and belongs in the handler. Also the precedent for sizing a "sweep" phase down to what actually exists: only 2 of 7 flags had a surface to gate, and *both* of the FR's own worked examples were unbuildable
- `phase-21a` — before adding a background job that **writes**, or a second one of any kind: 20e's
  "send no MediatR request" escape hatch is unavailable to a write path, and the answer is the
  scoped `IJobActingUser` (an `HttpContext` always wins, so a job identity can never serve a
  request), which buys per-row permission re-checking at execution time for free. Also the
  claim-then-act idiom applied at *row* granularity, why partial success must be a `Completed`
  job rather than a `Failed` one, and — before putting a concurrency token on any row a background
  job writes repeatedly — the cancel-versus-progress conflict that wedged a running import
- `phase-21c` — before adding a **fourth** background job, and before modelling anything that must
  appear in a report without being a document: the answer to "new job table or new
  `ImportEntityType` member?" is 21b's own test run again (*would the new rows leave columns
  permanently null, and is the loop genuinely a different loop?*), and here it came out the
  **other** way — no new table, two enum members, two DI lines. Also the record of what a
  lifecycle-free aggregate needs stated in prose (the invariant at the top of
  `MigratedSalesRegisterEntry`), why `LockDateBehavior`'s "no marker interface, no gate" is used as
  a *decision* rather than an omission, and the precedent for **deriving** a template's columns
  from an earlier phase's live-confirmed reading when confirm-live is impossible — defensible only
  because the migrated registers must match the statutory form by construction, which 21a's Product
  template had no equivalent of
- `phase-21b` — before adding a **third** background job, or any job that *produces* a file: 21a's
  deferred "one runner or many?" question is now answered (separate tables, one shared timer host
  `QueuedJobRunnerHostedService<TProcessor, TOptions>` over `IQueuedJobProcessor` — a shared
  **loop**, deliberately not a job framework, with one hosted service per processor so a long
  import cannot hold up an export), and 20e's "no ambient identity" default is **available again**
  for a job that only reads. Also the phase to read before promising a user something the codebase
  cannot deliver: FR-2.8 says "backup", there is no restore path anywhere, and Decision A is the
  record of choosing to say so on the button rather than ship the word. And before writing
  anything to `IFileStorage` from a job, read Decision E — until this phase, exactly one caller in
  the whole tree ever deleted a blob
- `phase-22` — before sending **any** tenant data to a third party, or adding a second such
  integration: Decision C is the record of what leaves, what the two default-closed gates are (a
  withdrawable `TenantSettings` opt-in, *not* a `TenantFeature` — those are immutable after
  Organization creation — plus an Admin-only key), and why a vendor failure is an *outcome* rather
  than an error. Also the precedent for a **conversion that creates nothing**: the prefill query's
  permission key resolves to the *target document type's own Create key* (`PrintDocumentQuery`'s
  shape), so a "convert to X" flow can never become a side door around `AuthorizationBehavior` —
  read it before adding a fifth inbox target or any other cross-document prefill; and the reason
  Phase 6's `ReferrerType`/`ReferrerId` was **not** reused for the inbox link
- `phase-23` - before adding anything that renders a date or an amount, and before any future
  app-wide sweep: the invariant is **dates are stored in AD, always** (BS is presentation/entry only,
  converted in `web/src/app/shared/formatting/`), the **supported BS range is 2000-2092** and outside
  it conversion returns null rather than guessing, and sweep completeness is enforced by
  `sweep-guard.spec.ts` rather than by intent. Also the precedent for **cross-checking reference data
  across four independent sources** and for distinguishing "sources disagree" from "this source
  stopped having real data" (the 30/30/30 filler tell); Decision F's record of holding a dashboard to
  the queries that already existed and then **overriding that rule once, deliberately**, for the one
  thing a client-side merge cannot do (page a merged stream); and Decision G's blanket-key pattern,
  where a single key exists only so `AuthorizationBehavior` runs while the real gating is per
  document type inside the handler -- the key alone must show nothing, and there is a test saying so
- `phase-24` - before assuming a new concept needs a new key on the tables that already exist:
  **confirm live what the reference product's model actually is.** Variants looked like a
  (ProductId, VariantId) stock-key change across 12 entities, 25 query handlers and both composite
  FIFO indexes; the live pass showed a variant is simply a **Product** with a parent pointer, and
  the phase became five nullable columns plus one rule. Read it before adding any second "child of
  a Product" concept, before writing a guard test that must prove a sweep complete (its two
  guards, server- and client-side, are the working examples alongside phase-23's), and before
  appending a child to an already-tracked parent's encapsulated collection - see the new
  Modified-not-Added gotcha in CLAUDE.md's Known gotchas
- `phase-25` - before writing any posting rule for a document that **transforms** value rather
  than moving it, and before assuming the reference product's behaviour is the right behaviour
  here: its Production Journal posts **no GL at all** (proved by approving one and finding it
  absent from a 199-row Journal report covering that date), because that tenant runs *periodic*
  inventory - and we post anyway, because we are *perpetual*, which is Decision A's whole
  argument. Also the phase to read before adding a shared FluentValidation helper (a `Func`
  selector cannot name its own property, so every endpoint 500s and no handler test can see it -
  see CLAUDE.md's Known gotchas), before showing a figure smaller than the display precision, and before
  running a browser pass in a non-interactive session - Step 3 records the dev-cert + cookie
  transplant that finally made one possible, and closed four phases of debt
- `phase-26b` - before ageing anything, before any report keyed by a **fiscal year**, and before
  adding a second report that has to agree with an existing one. Three things it settled. (1) The
  live pass is what found that **all four Monthly variants take a BS fiscal-year picker, not a date
  range** - the roadmap had predicted the server-side BS calendar would have one consumer and it
  arrived with five - so `Domain/Common/BsCalendar` exists, is a *verbatim port* of phase-23's
  client table rather than a retyping, and is tested against that file's own live-confirmed anchors
  plus a 33,969-day round trip; a fiscal year runs Shrawan 1 to the last day of Asar and is named by
  its first BS year. (2) **Agreement between reports is a design property, not a coincidence**:
  Invoice Age's total balance equals Customer Receivable Summary's closing balance because both read
  `ContactLedgerReader`, and that is why the phase extended that reader with contact-tagged
  **Journal Vouchers** - a `JournalVoucherLine.ContactId` had existed unread since phase 17, so a JV
  posted to a customer moved the general ledger without moving that customer's Statement. Read
  Decision B before touching it: the fix deliberately changes two shipped screens (Contact Statement,
  Contact Overview). (3) The precedent for **refusing to build two live options** rather than faking
  them - Quick Payment/Receipt is not a document type here (phase-17 Decision #7) so there is nothing
  to age, and Sales Summary's Service Charge is a product flag this codebase lacks, so the column is
  absent with a note on the screen rather than zero-filled. Also: age runs from the **Due Date**, and
  only `Expense` stores one, which is phase-9's credit-term wall reached from the other side.
- `phase-7`'s addendum (bottom of the file) — before adding a new tenant-wide default GL account or changing which account a posting rule debits/credits: grep for the field name across every posting rule that's supposed to read it. `DefaultInventoryAccountId` sat completely unread by `PurchaseBillPostingRule` for 12 phases (Goods purchases debited Purchase Expense instead), silently double-counting Cost of Goods Sold in `IncomeStatementQueryHandler`'s Net Profit for any tenant whose Purchase account was Expense-typed — the obvious/default choice, caught only by a later phase's live E2E, not by any test or `dotnet build`
- `phase-26a` - before building any **period-over-period comparison**, and before any report that
  reads `GlLine` back to the document that posted it. Compare is **one request, not two**: the second
  window is computed inside the same handler and merged into the same response, because lining two
  responses up in the browser means re-deriving the row set, the ordering and the group rollups
  client-side - phase-16c's bug in a new costume. `ComparePeriod` is deliberately **two rules, not
  one**: a range report compares against the same-length preceding period, but an as-of report has no
  length to reuse, so it compares against the same date one year earlier - and the window it actually
  used is echoed on the response so the screen and the `.xlsx` can label the columns with real dates.
  Read it too before assuming a GL report can show a document's own date: `GlJournalEntry` stores
  only `SourceDocumentType`/`SourceDocumentId`/`PostedAt`, so every such report joins back across the
  eleven GL-posting types (`GlSourceDocumentResolver`) and shows the same date field it filters on,
  because a row that appears outside its own printed range is worse than an approximate one. Also
  the precedent for **deriving an attribute from the audit trail** when no aggregate stores it
  (Created By), for **refusing to total a column** whose values are not the same unit of account, and
  for mapping one enum onto another **by name with a test that says so** rather than by ordinal
