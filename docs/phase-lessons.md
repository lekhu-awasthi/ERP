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
- `phase-26c` - before building any report over **stock**, before adding a report that has to agree
  with a register that already exists, and before writing anything on an **unauthenticated** path.
  Four things it settled. (1) The confirm-live rule earned its keep twice in one phase, both times by
  contradicting the plan: the roadmap said the main registers must now *exclude* credit and debit
  notes and they must not (the same notes appear in both registers, negative in the main one and
  positive in the return one, footer net of them - phase 19's folding was parity), and it said the
  Purchase Return Register was the sales one's mirror when it is the *Purchase* Register's, seven
  money columns to four. Both would have shipped wrong from the doc alone. (2) **A dated stock report
  cannot read the FIFO layer table.** `StockLedgerEntry.QuantityRemaining` is decremented in place, so
  it only ever answers "as of now"; `StockFactReader` derives Opening/In/Out/Balance from the
  append-only `StockMovement` instead, which is correct at any date and equals the FIFO figure today -
  proved three ways by `sqlcmd` in the E2E. Its one deliberate exception (a negative balance carries
  no value) is **unreachable today**, because `ConsumeAsync` throws on an oversell rather than warning
  as the reference product does; the test pins the throw and says so in its name, so the guard is not
  deleted as dead code before the setting that needs it exists. (3) The shared-reader rule extended
  from one reader to three - `StockFactReader` for the four inventory reports plus Net Trading Assets,
  `SalesReturnReader`/`PurchaseReturnReader` for the four register screens - and the second pair was
  extracted from *shipped* handlers, so the discipline now costs a refactor rather than only shaping
  new code. (4) `UserLoginEvent` is the precedent for storing something that is **not tenant-scoped**:
  signing in happens before an organization is chosen and a failed attempt has none even in principle,
  so the row carries no `OrganizationId` and the *report* does the scoping by joining
  `OrganizationMembership` - plus the attempted email, which is the only way an attack on a
  colleague's address becomes visible. Read Decision F before writing on any unauthenticated path: the
  write is swallowed on failure, deliberately the opposite of `AuditBehavior`, because an audit row
  must never become a way to deny someone their session. Also: `decimal` has a signed zero, and no
  test can see it.
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
- `phase-27a` - before adding a new `DocumentType` member, or building any future cross-cutting
  sweep across every document type. The confirm-live pass corrected the roadmap's own scope twice:
  Custom Fields applies to 13 document types, not 15 (Warehouse Transfer and Inventory Adjustment
  carry no such section live at all), and Reporting Tags is wider than the roadmap said (all 15
  transactional types **plus** OpeningBalance/OpeningStock, each tagged by its own row's line id).
  One classification table (`DocumentMechanisms`) plus one server guard test
  (`DocumentMechanismSweepGuardTests`) and one client guard spec
  (`document-mechanism-sweep-guard.spec.ts`) are what make "sweep complete" a build failure rather
  than a claim - modelled directly on phase-23's `sweep-guard.spec.ts` and phase-24's
  `ProductVariantSweepGuardTests`. `Comment` went polymorphic here, on the exact trigger phase-18
  decision #3 set for it ("generalize only if/when a second parent type is actually needed") - read
  phase-18 first if extending `Comment` further. Also the precedent for a permission check that
  cannot be a declared `PermissionKey` at all: when the real key depends on a column of the row the
  handler is about to load, declare a blanket key to clear `AuthorizationBehavior` and re-check the
  real one inside the handler once the row is loaded (`AttachmentAccess`, third use of the
  `TransactionApprovalView` pattern) - read this before adding any other id-only mutation whose
  permission depends on what the id points at.
- `phase-27b` - before adding a document type to the print pipeline, before rendering any date in
  server-produced output (a PDF, an `.xlsx`, a file name), and before giving a `CustomTemplate` type
  a consumer. The confirm-live pass reshaped the phase: the reference product prints **one page frame
  with a varying number of titled tables** (Production Journal 3, Cash Transfer 2, Payment 2), so
  phase-20d's "`Lines` XOR `GlLines`" DTO was replaced by a section list and `DocumentPdfRenderer`
  now switches on **no `DocumentType` at all** - adding a type is a case in a handler, never a new
  layout. Print itself is universal across all 15 transactional types (`DocumentMechanisms.Printable`),
  and rides each type's own `View` key rather than a key of its own. This is also where phase-23
  Decision A's stated limitation was closed: the client sends `X-Calendar`, one middleware parks it in
  `RequestCalendar` (an `AsyncLocal` - the codebase's one deliberate exception to constructor
  injection, and its reasoning is written down), and business dates convert while audit timestamps and
  file-name dates stay AD. Terms and Conditions corrected the roadmap's scope again, 27a-style: 5
  document types, not 2, and it stores the document's **own text** seeded from a template, never a
  template reference. Read before wiring `Send Email` or the `Email` template type in Phase 30 - the
  same three types carry both actions.

- `phase-28` - before adding a second currency-aware anything, before gating a new feature flag, and
  before touching a posting rule's inputs. Three things worth carrying:
  **(a) The fold goes on the posting rule's *inputs*, never on the finished `GlLineInput` list.**
  Every rule here derives its balancing leg as a sum of the other legs, so converting afterwards
  rounds that leg independently of what it balances and `GlJournalEntry.Post`'s
  sum(Debit)==sum(Credit) invariant fails intermittently (two 0.05 debits against one 0.10 credit at
  rate 1.5 give 0.16 against 0.15). Converting first keeps every entry balanced by construction.
  Only `JournalVoucher` and `CashTransfer` cannot take that route - their rules take the aggregate
  itself - and they go through `GlCurrencyConversion`, which *books* the residue to the forex
  account rather than absorbing it. And nothing already denominated in base currency is converted:
  FIFO unit costs, COGS, historical `GlLine`s.
  **(b) An entitlement can be a cap on a *list* rather than a gate on the things that read it.**
  MultiCurrency is phase-20f Decision #4's shape for the second time, and stronger: because a
  document's Currency picker reads the tenant's own list and its rate input disables itself on the
  base currency, a one-entry list makes the whole surface degenerate to "NPR, rate 1, read-only" by
  itself - so **no document command is feature-gated at all**. Check whether a cap upstream already
  satisfies the requirement before gating N consumers downstream.
  **(c) The confirm-live rule cuts both ways.** The roadmap's decisive experiment could not be run -
  the reference product's own currency-catalog picker returns "No data" on the UAT tenant, so no
  second currency can be activated there - and the allocation posting rule is therefore reasoned
  from first principles and **recorded as reasoned**, in the code's own doc comment as well as the
  status doc. Blocked is a finding; guessing quietly is not. What the pass *did* settle reshaped the
  design anyway: the printed figure (one money column, no NPR equivalent), the rate being typed and
  stored per document, Opening Balances' Conversion Rate being the same control, and the reference
  chart carrying a realised Forex Gain account with **no loss counterpart** - which we diverge from
  deliberately, on phase-6's VAT-Receivable-vs-Payable precedent
- `phase-29` - before capitalising anything into a FIFO layer, before adding a tenant-default GL
  account, and before asking permission to run a confirm-live **write**. Four things worth carrying:
  **(a) Check whether existing data already answers the question.** The roadmap's decisive
  experiment was a write on the reference tenant; two already-approved bills there carried Additional
  Cost rows and settled every open question read-only. A blocked confirm-live is a finding
  (phase-28); an *unnecessary* one is a cost, and the cheap check is to look for a document that has
  already done the thing.
  **(b) The reference product offering something is not a reason to offer it.** Its Additional Cost
  product picker lists service lines, which is harmless there because nothing is posted and nothing
  is capitalised. Here a service line creates no FIFO layer, so an allocation to one would vanish and
  break the conservation law - so we reject such a row rather than dropping it silently, and record
  the divergence.
  **(c) Build the GL leg from the value the ledger actually received.** `capitalised = layer value
  created - goods amount`, never the figure the user typed, which is what makes the Inventory account
  equal the FIFO ledger by construction. The gap between the two is the residue, and it is named on
  the document (phase-25's rule, applied to a second aggregate). Rounding the unit cost **once**, at
  the ledger's own scale, from the line's total landed value is the other half of that.
  **(d) A new capitalised cost changes what a *reversal* owes.** `ConsumeAsync` relieves layers at
  their landed cost while `DebitNotePostingRule` credited Inventory the return price, so a return off
  a landed-cost bill would have left Inventory permanently above the ledger - phase-6 bug #3's trap in
  a new place. Trace the net effect on every account across original **and** reversal before assuming
  an existing rule still mirrors a rule you have just changed. Also the phase that found the
  Accounting Defaults screen was three accounts behind its own API (phase-23 bug #1 in reverse).

- `phase-30` — before wiring a Send Email action, before adding a background job that *reads* through
  a permission-gated request, before assuming a `CustomTemplateType` member is the right home for
  something, and before trusting an earlier phase's "one enum member and a branch" estimate. Five
  things generalise.
  **(a) A shared UI panel is not evidence of a shared model.** The reference product renders its
  email templates inside the Custom Templates panel, which is why phase 27b added
  `CustomTemplateType.Email`. Underneath, it serves them from a *different resource*, with six extra
  fields, a disjoint type vocabulary, and a type that is disabled on the edit form. `EmailTemplate`
  became its own aggregate and the dead enum member was **deleted** rather than left looking like a
  feature — phase-18 Decision #2's "confirm it is really the same concept" applied to a lookup
  instead of a polymorphic parent, and coming out the other way.
  **(b) A sampled list becomes a wrong list; find the rule instead.** Phase 27b recorded Send Email as
  present on three document types by sampling three. It is on seven live screens (six `DocumentType`s,
  since Customer and Supplier Payment collapse onto one) — and the *rule* is that Send Email exists
  exactly where an email template context exists, which also settles the five types never probed.
  `DocumentMechanisms.Emailable` and `EmailTemplateContexts` are asserted to describe the same set in
  **both** directions, because the six/seven mismatch makes a naive count comparison pass while the
  sets disagree.
  **(c) The rule for whether a background job needs an identity is not "does it write?" — it is "does
  it send a MediatR request?"** This job only reads, so phase 20e's no-identity default appeared to
  apply as it did for phase 21b's exporter. But it renders the attached PDF through
  `PrintDocumentQuery` *precisely so* an emailed PDF cannot drift from a printed one, and a MediatR
  request with no acting user fails `AuthorizationBehavior`. First job in the codebase to need
  `IJobActingUser` for a read-only reason; the alternative was duplicating a fifteen-document
  pipeline.
  **(d) Do-exactly-once and "a resend is a new row" are compatible only if the key is an *intent*.**
  An occurrence key (20e's) needs a schedule and there is none; a content hash would silently swallow
  the legitimate second send a customer asks for. A **request id minted when the dialog opens** splits
  them exactly, under a unique index. Related: `EmailSendLog` carries a rowversion, which phase-21a's
  rule forbids on `ImportJob`/`ExportJob` — that rule protects rows with *two* writers, and the
  question to ask is how many writers the row has, not whether a job touches it.
  **(e) "We already have the interface" measures the wrong thing.** `AlertMedium.Sms` was predicted as
  one enum member and a branch because `ISmsSender` had existed since phase 18. It was four changes:
  the member, recipients changing meaning (addresses → phone numbers, so validation switches), the
  subject ceasing to be meaningful, and — the one nobody anticipated — it **spends money** through
  phase 18's credit ledger, so an unaffordable occurrence has to fail *visibly in the send ledger*
  rather than throw inside a timer tick.

- `phase-31` — before enforcing any tenant setting, before adding a second confirmable warning to a
  document that already has one, before adding a non-nullable column to a populated table, and
  before deciding a subscription or entitlement is "just a flag". Six things generalise.
  **(a) A setting with no command behind it is not a dead setting — it is an absent feature.** The
  roadmap listed three `TenantSettings` fields as dead. Four of the five had no command, no endpoint
  and no screen at all, and the fifth (`NegativeStockBalanceAction`) was *read* by
  `FifoStockAvailabilityPolicy` and still could never be moved off its seed. So the phase's first
  deliverable was reachability, not enforcement. Phase 29's rule — grep `web/` for the field name
  before calling it done — extends one step: **grep for the command too.**
  **(b) Two confirmable warnings on one document cannot share an override flag.** An invoice can trip
  both a stock shortfall and a credit-limit breach, and the reference product shows them as two
  dialogs. One shared `OverrideWarning` would mean acknowledging the stock dialog silently waived a
  credit breach the user was never shown. Two flags, plus a `warningKind` extension on the 422's
  ProblemDetails so the client sets only the flag matching the dialog it just displayed.
  **(c) Reuse an existing marker set rather than inventing a third.** `SubscriptionExpiryBehavior`
  (the fifth pipeline behavior) gates exactly what `ILockDateSensitive`/`ILockDateSensitiveDocument`
  already mark — which *is* "a create, update, approve or void of a transactional document" across
  all fifteen types, the same set a lock date freezes and for the same reason. No sweep, and the two
  gates cannot drift apart. The command that lifts the expiry is excluded automatically, because it
  carries no document date and could not implement either marker if it tried.
  **(d) The `AttachmentAccess` pattern, second use — and the shape is wider than it looked.** Phase
  27a's rule was "the real key depends on a column of the row the handler is about to load". Here it
  depends on the *requested value* as well: bouncing a cheque needs `Payments.Payment.Void`, but only
  when the linked payment is Approved. Same remedy (a blanket key on the request, the real key
  re-checked inside the handler with the identical `ForbiddenException` shape), and the E2E has to
  prove it in **both** directions or it proves nothing: the same Member gets 404 on a nonexistent
  cheque (so they do hold the pipeline key) and 403 naming the second key on a real one.
  **(e) A non-nullable column added to a populated table needs its backfill written by hand.**
  `migrations add` scaffolds `NOT NULL DEFAULT '0001-01-01'` for a `DateOnly`, which back-dates every
  historical row to the year 1 and leaves a stray default constraint. Add nullable, `UPDATE` from the
  sibling column, alter to NOT NULL — CLAUDE.md's "read any migration that replaces or retypes a
  column" applied to one that merely *adds* one.
  **(f) An invariant that only time can violate cannot be constructed through its own aggregate.**
  `TenantSubscription.Renew` refuses an end date before the start date and a trial starts *now*, so
  the expired-tenant test reaches through EF's change tracker rather than weakening the invariant to
  make itself easier to write. Weakening a Domain rule so a test can reach a state is the wrong
  trade every time.
  Also, and cheaply: phase-23's `sweep-guard.spec.ts` caught a raw `<input type="date">` this phase
  introduced on a screen written eight phases after that guard — the clearest return a guard test in
  this codebase has yet produced.

- `phase-32` — before deciding a screen cannot be confirm-lived, before letting a tenant setting
  choose which document types store a field, and before adding a filtered unique index because the
  standing gotcha says to. Five things generalise.
  **(a) A blocked confirm-live is a state, not a verdict.** The roadmap did not merely predict this
  phase would be derived — it pre-authorised phase-21c's derive-instead precedent, because Billing
  Location is an entitlement that is off on the Moonbeam tenant. A *second* tenant with the
  entitlement on existed the whole time. Taken at face value, the phase would have shipped a body
  field instead of a header picker, a user-chosen `LocationType`, a fixed scope list instead of a
  runtime setting, and no Advanced panel at all — four wrong things, confidently documented. **Ask
  whether a different tenant, account or environment can observe it before invoking the precedent.**
  **(b) A setting that selects among *sets* forces the storage to the union, not the current value.**
  This is phase-31 lesson (a) inside out. The Advanced panel lets an Admin widen location scope from
  the sales-side three to all seventeen document types with one click, at any moment. Sizing the
  schema to the default would have made that click a lie — it would appear to work and silently
  record nothing on eleven types. So `LocationId` is nullable on all 17, and only *behaviour* is the
  selection (`DocumentLocationScope`). Whenever a setting picks a subset, ask what the widest subset
  costs, because that is what the schema owes.
  **(c) When EF's convention and this codebase's own gotcha agree, and both are wrong, record it at
  the index.** CLAUDE.md's standing rule is that a unique index over a nullable column needs
  `.HasFilter("[Col] IS NOT NULL")`, and `migrations add` scaffolds exactly that. For the numbering
  counter it is inverted: SQL Server treating NULLs as *equal* is the invariant being bought, because
  it is what admits one and only one settings row per (org, type). Under the filter those rows leave
  the index, a tenant can acquire two competing counters, and `DocumentNumberGenerator`'s lazy-create
  race stops being guarded. `HasFilter(null)` is load-bearing; the next reader's instinct will be to
  "fix" it.
  **(d) Two lists that must not drift belong in the file that exists to stop drift.**
  `DocumentLocationScope` began with private copies of the 17 and the 3; they moved into
  `DocumentMechanisms`, so 27a's existing `DocumentMechanismSweepGuardTests` fails the build when a
  later phase adds a `DocumentType` without deciding whether it carries a location. Reusing the guard
  that already exists beats writing a second one.
  **(e) A scripted sweep across 32 commands is safe only if every edit asserts its own anchor count.**
  Each script here counted its anchor and refused to write on anything but the expected number, and
  preserved per-file CRLF and BOM. That caught a stray `LocationId` on `BillOfMaterialsRequest` (a
  shared record tail matched three records where two were meant). The counter-discipline to CLAUDE.md's
  `sed`-over-a-glob warning is not "never script" but "never script without a pre-flight count".

- `phase-32b` — before enforcing a permission that depends on a row the handler has not read yet,
  before re-confirming a screen an earlier phase already confirmed, and before adding any request over
  a location-bearing document type. Five things generalise.
  **(a) A confirm-live pass can falsify an earlier confirm-live pass.** Four documents recorded that
  turning `LocationWiseReportPermission` on pulls the 52 Reports keys into the per-location matrix —
  the roadmap, phase 32's status doc, the module scan's own 2026-09-07 appendix, and the field's C#
  doc comment. Flipping the toggle on the live tenant and hard-reloading showed the matrix unchanged
  at 94 × N with no Reports group. It scopes report *rows*, not report *keys*. Phase 32's lesson was
  "ask whether another tenant can observe it"; this one is the next step — **a screen someone already
  read is not thereby settled, if what was recorded is an inference about a control nobody actually
  operated.** One reversible tenant setting, flipped with permission and reverted, was the whole cost.
  **(b) The `AttachmentAccess` pattern's third use is where it stops being a per-handler re-check.**
  27a and 31 were one handler each; this spans ~120 requests, and a single missed re-check is an open
  door rather than a bug. So the check moved *into* `AuthorizationBehavior` — not a sixth pipeline
  stage, because the location half must know whether the organization-wide half passed, and passing
  that between two behaviors needs a scoped context a nested `ISender.Send` would corrupt. The count
  is not what promotes a pattern; the blast radius of missing one instance is.
  **(c) A marker interface is only as good as the guard that requires it.** Four markers
  (`ILocationBearingCommand` reused from 32, plus `ILocationScopedDocument` /
  `ILocationFilteredQuery` / `ILocationAgnosticRequest`) and `LocationScopeSweepGuardTests`, which
  reads `PermissionKey` off `RuntimeHelpers.GetUninitializedObject` and fails the build on any
  location-scopable request declaring none of them. It found two real defects on its first run,
  including a command that would have looked a Journal Voucher id up in the Payments table and
  silently skipped its own check. Requests whose key cannot be evaluated statically are treated as
  scopable *conservatively*, and the two genuine exemptions are named in a dictionary with reasons
  (27a's `NotApplicableReasons` idiom) rather than filtered out by a predicate.
  **(d) Reuse a marker by reading it, not by merging it.** All thirty Approve/Void commands already
  declare `ILockDateSensitiveDocument`, which names a document by (type, id) in exactly the needed
  shape — so `LocationScopeResolver` reads that marker when the location one is absent, and thirty
  files needed no edit. The two interfaces stay separate because the sets genuinely differ (a lock
  date freezes writes; a location grant also governs reads), and a guard test pins that the reuse
  still covers them. Phase-31 lesson (c) said reuse when the set is the same; this is its complement.
  **(e) A nullable FK whose NULL is a sentinel wants an *unfiltered* unique index — second instance.**
  `RolePermission.LocationId` null means *the* organization-wide grant, at most one per (role, key),
  so `HasFilter(null)` is the enforcement, exactly as phase 32's numbering counter found. Two
  instances in consecutive phases is what turned CLAUDE.md's standing rule from "always filter" into
  "ask what a NULL there means first".

## Phase 33 — platform chrome (global search, History, Quick Links, the per-user store)

**Read `docs/phase-33-status.md` before touching the shell (`web/src/app/app.ts`/`app.html`), before
adding a per-user setting, before adding a request that searches across document types, and before
trusting any list of "what a screen does" that nobody has operated a control on.**

**(a) A confirm-live pass can falsify *three* things at once — and the corrections were not
refinements.** Phase 32b's lesson recurred at scale: of the four claims the roadmap made about this
phase, three were wrong. History turned out to be **client-only `localStorage`** listing **screens,
one per module**, not a server-recorded list of opened records — which settled Decision C by
observation rather than by costing a write-per-document-open, and a cost argument can be wrong where
an observation of the shipped product cannot. Global search turned out to be **half a command
palette**, its navigation and create-action half usually the majority of a result set, with a whole
`collection` (Accounts) missing from the recorded list. And a document is matched on its **number
alone** — it carries no name in the payload at all, so searching a customer's name never surfaces
that customer's invoices. Only "Quick Links is per-user and server-stored" survived intact.

**(b) Extracting a shared helper is not done until the copies it replaced are deleted.**
`GrantedPermissionReader`'s own doc comment said phase 12's and phase 23's inlined permission joins
"are one method now". They were not: both still carried the inlined join, and it predated phase 32b,
so neither filtered `LocationId == null` — a branch-scoped grant read as an organization-wide one in
both the Transaction Approval queue and the Home recent-activity feed. **A doc comment claiming an
extraction is complete is not evidence; `grep` is.** It was found only because this phase's search
handler was about to become the third copy of the same pattern — which is the general case: the
moment you are about to write copy N+1, check that copies 1..N were actually retired.

**(c) An expression tree does not short-circuit, so `flag || list.Contains(x)` hands EF a null
list.** Folding an optional filter into one predicate with a captured `bool` compiles, and fails
during *translation* — on the branch where the caller is unrestricted, i.e. almost everyone. Compose
a second `.Where()` instead, which is what every phase-32b list handler already does. Neither the
compiler nor an InMemory test can catch this.

**(d) Derive a catalogue from the router rather than writing one down.** The reference product serves
its search's navigation half from the server; this codebase keeps it client-side and derives it from
`Router.config`, so there is no second copy of the route table on either side of the wire. A route a
later phase adds appears in the search *and* in the Quick Links picker with no edit, and one deleted
disappears from both — the same "find the rule, don't sample the list" discipline as phase 30, except
here the rule is executable. The guard spec pins the two silent failure modes: a screen that yields
no entry, and an entry whose url is not a route.

**(e) A per-user store wants a row per key, not a document per user.** One JSON blob per user makes
every write a read-modify-write of the same row, so two settings saved from two tabs silently discard
one. A row per `(OrganizationId, UserId, Key)` makes "last writer wins" mean *of that setting*. And a
stored url that a client later navigates to is an **open-redirect surface** unless the validator
requires it to be application-relative.

**(f) Phase 23's declined decision was this phase's premise, and it did not need revisiting to be
reversed.** Decision C declined a table for one boolean and named `DatePreferenceService` as "the
single seam to move behind an endpoint if that changes". Quick Links made the table necessary anyway,
so the second consumer cost one `activate()` call — with `localStorage` kept as the *synchronous
cache* so the first paint never flashes the wrong calendar. **A well-named seam is what makes a
declined decision cheap to reverse later**, which is the argument for writing the decline down.

## Phase 34a — accessibility: the WCAG 2.1 AA sweep and the guard that keeps it done

**Read `docs/phase-34a-status.md` before adding any template, before choosing a colour, before
writing a guard that reads a file off disk, and before scripting an edit anchored on two markers with
a lazy match between them.**

**(a) A component boundary hides a control from a control-shaped scan.** The sweep for
`<input>`/`<select>`/`<textarea>` with no accessible name found 348 and fixed them, and was
*complete and wrong*: 121 date fields had a visible label, a real input, and nothing joining them,
because the input lives inside `<app-bs-date-input>`. Nothing about a better version of that scan
would have found them. The **mirror** check did — every `<label>` must be associated with something —
and the pair is what makes the rule total. Whenever a sweep enumerates one side of a relationship,
ask what the other side would enumerate; the difference is where the wrapped cases hide.

**(b) A palette's margin can be the white in the worked example.** Bootstrap's brand tones are built
to clear WCAG's 4.5:1 against pure white and only just — `text-primary` is 4.50:1. This app's page
background is `#f8f9fa`, where the same four colours fall to 4.27–4.45:1 and fail. **It was the
computation that found this, not the audit**: had the contrast rule been written down as a list of
forbidden class pairs (which is what the sweep started as) it would have recorded the badge defect
and never looked at the page ground. Record the *palette* and derive the conclusion; a list keeps the
answer and loses the reason.

**(c) A confidently wrong accessible name is worse than none.** Naming 96 line-item controls from
their column `<th>` by counting `<td>`s got six wrong — they sat in a `<td colspan="5">` expansion
row, where index 0 is the whole row — so an *Amount* field announced as "Name". A screen-reader user
is then told something false rather than nothing, and has no way to detect it. Any positional
derivation needs an audit for the shapes the position cannot see, and the audit is a second pass, not
a careful first one.

**(d) A guard that reads a file must assert the file is non-empty, not merely defined.** The test
keeping `styles.scss` in step with the measured palette read it via `import.meta.glob(…, '?raw')`;
Vite compiles SCSS and returns an **empty string** — not an error. `toBeDefined()` passed on `''` and
every assertion over the contents was vacuously true. This happened *inside the file whose own header
warns about vacuous guards*, and was caught only because a failure message printed the length. The
rule phase-23 Decision D wrote for globs ("assert it matched a plausible number first") is really a
rule about every input a guard reads.

**(e) A lazy `.*?` between two anchors silently spans the instances in between.** A sweep matching
`<label…>(.*?)</label>\s*<app-bs-date-input` merged pairs of labels wherever the first was not
followed by a date input, because the engine expands `.*?` across the intervening `</label>` to
satisfy the trailing anchor. Exclude the closing marker explicitly — `((?:(?!</label>).)*?)`. **The
tell was two independent counts disagreeing (99 against 121)**, which is the argument for deriving a
sweep's expected number twice rather than trusting the sweep's own tally, on top of phase-32's rule
that it must be asserted before anything is written.

**(f) Splitting a phase is a question about order, not only about size.** Phase 34 was three sweeps
and a 130-route re-layout. The obvious order — re-layout first, audit the final layout once — is
wrong, because the template-level accessibility work (a label's `for`, a `<th scope>`, a colour
class, an icon's `aria-hidden`) is **layout-independent** and survives the re-layout untouched, while
the small landmark-level part is better built *into* the new shell than retrofitted onto it. The
split that minimises rework is therefore per-template now, per-layout with the layout — and the guard
spec built first is what makes the next phase's markup conformant on the day it lands.

**(g) An accessibility defect and a consistency defect can be the same defect.** The codebase paired
`bg-*-subtle` with the plain tone 161 times (3.4–3.9:1) and with `-emphasis` 50 times (7.2–10.5:1):
one screen was doing it right, every other screen was doing it wrong, and that is simultaneously
WCAG 1.4.3 and NFR-6.1. Expect more of these when 34b measures the list screens.

---

## Phase 34b — consistency and the shell (NFR-6.1)

**Read this before building anything that sits outside a page**, before adding a paginated list
query, and before wiring a filter a screen displays but does not own.

**(a) Ask what the codebase already is before choosing between two ways to change it.** The kickoff
framed the shell as a choice between re-laying out 130 page templates and an offcanvas overlay that
leaves them alone. One grep dissolved it: all 130 already open with the same
`<div class="container py-5">`, and a centred container re-centres inside a narrower `<main>`. A
fixed rail plus a left inset on two shell elements gave a permanent left nav for **zero** page edits.
The generalisable move is not "prefer the cheap option" — it is that a dichotomy offered by a plan is
a hypothesis about the code, and it is worth one grep before spending a phase on either branch.

**(b) A filter a screen *displays* but did not *apply* is worse than no filter.** The global date
range arrives from the per-user store asynchronously, after a list page's constructor has already
issued its request — so the chrome rendered "Last 30 days: 2026-08-11 – 2026-09-10" above an invoice
dated 2026-07-20. It looked checked and it was wrong. Anything global that a screen both shows and
sends must reload that screen when it changes, and the reload has to exist from the first version,
not be retrofitted once someone notices.

**(c) An `effect()` cannot distinguish the write that is already being acted on from a new one.** A
handler set a signal and scheduled work; an effect watching the same signal cancelled that work,
believing it stale. Clearing the search box silently never restored the list. If a handler already
schedules the response to its own write, an effect over that signal is a race, not a safety net.

**(d) Deriving beats listing for a catalogue; listing beats deriving for a tray.** The left nav is
built from `NavigationCatalog` and only its area *ordering* is written down, because a missing nav
entry is a screen nobody can reach. The Create New flyout's 18 shortcuts *are* written down, because
deriving them would produce a different, longer panel and lose the curation that is the feature. Both
are guarded the same way: every url must resolve to a real catalogue entry. Decide which kind of list
you have before reaching for the router.

**(e) A shared matcher inside a LINQ predicate is untranslatable, and InMemory hides it.** A
`SearchTerm.Matches(column, term)` helper was written and deleted before use: a static call in a
`Where` cannot be translated, nor can
`string.Contains(term, StringComparison.OrdinalIgnoreCase)` — and every handler test would have
passed, because InMemory evaluates the expression in C#. Phase-25's captured-`Func` gotcha through a
different door. Related: the single-argument `Contains` matches case-insensitively on SQL Server
(collation) and case-sensitively on InMemory, so a handler test must search with the stored casing.

**(f) A uniform sweep is worth more than its subject, because it is the first thing that asks the
question uniformly.** Adding a search term to every paginated list found three pre-existing gaps
nobody was looking for: two queries with no validator at all (so their paging was unbounded too), and
one that had accepted a term since phase 25 with no length cap on it. Phase-33's finding restated.

**(g) The screenshot is ground truth in the browser pane.** Under viewport emulation,
`getComputedStyle` and `getBoundingClientRect` can lag the rendering after a resize — the mobile
drawer measured as on-screen with an identity transform while the screenshot showed it correctly
tucked away, and a fresh load at the emulated size agreed with the screenshot. Reload after emulating
a viewport rather than trusting measurements taken across a resize.

---

## Phase index entries as written in CLAUDE.md before the 2026-09-10 trim (26a–34b)

Moved verbatim; the one-line versions in CLAUDE.md keep the same "before X" hooks in fewer words.

- Phase 26a: the five missing Accounting reports + FR-9.1's Compare column on the three financial statements. Before adding a period-over-period comparison, or any report that reads `GlLine` back to its source document — `docs/phase-26a-status.md`
- Phase 26b: Receivable/Payable + Sales/Purchase analytics (13 reports, 7 shared handlers) and the server-side BS calendar. Before ageing anything, or any report keyed by a fiscal year — `docs/phase-26b-status.md`
- Phase 26c: the Reports catalogue completed — 4 inventory reports, both return registers, Net Trading Assets, Exceptional Report, User Log. Before a report over stock, a second report that must agree with a register, or anything written on an unauthenticated path — `docs/phase-26c-status.md`
- Phase 27a: swept Custom Fields/Custom Status/Reporting Tags/Tasks-Documents-Activity across every document type, generalized `Comment` to a polymorphic parent. Before adding a `DocumentType` member, or building a second cross-cutting mechanism sweep — `docs/phase-27a-status.md`
- Phase 27b: print/PDF for all 15 document types on one generic section layout, BS dates in server-rendered PDFs/`.xlsx`, the last three pagers, wizard Turnstile, a feature-flag route guard, and `CustomTemplate`'s first two consumers. Before adding a type to the print pipeline, rendering a date in server-produced output, or giving a `CustomTemplate` type a consumer — `docs/phase-27b-status.md`
- Phase 28: multi-currency — a tenant `Currency` list, `CurrencyCode`/`ExchangeRate` on 12 document types,
  the base-currency fold on posting-rule *inputs*, and a realised forex rule on Payment allocation. Before
  converting anything into the general ledger, before gating a feature flag, or before trusting a
  confirm-live pass to be possible — `docs/phase-28-status.md`
- Phase 29: landed cost — an Additional Cost section on the Purchase Bill, allocated at Approve by
  Value or Quantity across the bill's *goods* lines and capitalised into the received FIFO layers'
  unit cost, against a new Landed Cost Clearing account. Before capitalising anything into a stock
  layer, before adding a tenant-default GL account, or before asking to run a confirm-live *write* —
  `docs/phase-29-status.md`
- Phase 30: Communications — a **Send Email** dialog on 6 document types plus the Contact page, an
  Email Logs tab with data behind it, an Email Templates config page, `AlertMedium.Sms`. Before
  wiring a Send Email action, before a background job that *reads* through a permission-gated
  request, before assuming a `CustomTemplateType` member is the right home, or before trusting an
  earlier phase's "one enum member and a branch" estimate — `docs/phase-30-status.md`
- Phase 31: credit control — `Contact.CreditLimit`/`CreditTermId`/`AcceptsReverseTransactions` and a
  **Credit Limit Exceeds** policy at Invoice Approve, plus the **Configurations > General** screen
  that made all five behaviour settings reachable for the first time; Negative Cash Balance,
  Suggest Selling Price and Product Price Basis enforced; a stored `DueDate`; a bounced cheque that
  voids its payment; subscription expiry. Before enforcing a tenant setting, before adding a second
  confirmable warning to a document, before adding a non-nullable column to a populated table, or
  before writing a test that needs a state only time can produce — `docs/phase-31-status.md`
- Phase 32: Billing Locations — a `BillingLocation` aggregate with **HeadOffice seeded
  unconditionally** and `MultipleLocations` as a cap at one, nullable `LocationId` on all 17
  location-bearing types, the **Advanced** panel that makes location scope a runtime tenant setting,
  location-wise numbering, and the location filter/column on the Invoice list and Sales Master
  Report. Before deciding a screen cannot be confirm-lived, before letting a setting choose which
  types store a field, or before adding the filter to a unique index over a nullable column —
  `docs/phase-32-status.md`
- Phase 32b: per-location permission scope — the role editor's second matrix (the 77 transaction
  keys per location), a nullable `RolePermission.LocationId` where **null is the organization-wide
  grant**, and enforcement as one extra branch inside `AuthorizationBehavior`. Before enforcing a
  permission that depends on a row the handler has not read yet, before re-confirming a screen an
  earlier phase already confirmed, or before adding a request over a location-bearing document type
  — `docs/phase-32b-status.md`
- Phase 33: platform chrome — a **global search** (Ctrl + /) in the shell, a **History** popover, the
  **Quick Links** tray, and `UserPreference`, the per-user store (a row per
  `(OrganizationId, UserId, Key)`). Before adding a per-user setting, before a request that searches
  across document types, or before trusting a recorded description of a control nobody operated —
  `docs/phase-33-status.md`

- Phase 34a: the WCAG 2.1 AA sweep (page titles, control names, `th scope`, icon names, contrast)
  and `a11y-sweep-guard.spec.ts`. Before adding a template, choosing a colour, writing a guard that
  reads a file, or scripting an edit with a lazy match between two anchors — `docs/phase-34a-status.md`
- Phase 34b: the shell (left nav, Create New flyout, company switcher, global date filter) built on
  `NavigationCatalog` for **zero page-template edits**, a Reports index page, and NFR-6.1's list
  chrome — a search term on 25 `List*Query` types and a date range on 16. Before adding a paginated
  list query, before a filter a screen displays but does not own, before putting `overflow` on a
  layout container, or before trusting a measurement taken across a viewport resize —
  `docs/phase-34b-status.md`
- Phase 34c: scale (NFR-5.1/5.2) — the 50,000-invoice dataset and its committed harness
  (`tools/scale/`), a p95 budget per class of screen, `TenantIndexConvention`'s **50 indexes derived
  from a rule** after finding 18 tenant-scoped tables with no index leading on `OrganizationId`, and
  the shell `@defer`ed off the login path. Before adding an index of any kind (an index added for one
  access path changes the plan for every other path over the same table — this one made search
  *worse* while making the list 10× faster), before mapping a new tenant-scoped entity (the
  convention throws at model build if it cannot classify it), before quoting any performance number
  from this repo (the report class swung 4× between two identical passes; only the list class is
  reliable here), and before believing a structural inference about multi-tenant cost — the
  `GlLine`-has-no-tenant-column story was refused by seeding a second tenant and re-measuring —
  `docs/phase-34c-status.md`
- Phase 35a: ledger drill-down and the location dimension on documents. Phase 35 was split with the
  user after a survey showed it was two phases (34a/b/c's precedent): 35a is the drill-down plus the
  document-side sweep, 35b is the report half. Shipped `?accountId=` on Detail General Ledger with a
  **View Ledger** row action on the Chart of Accounts and the global-search Account hit pointing at
  it (closing phase-33 #1 — the reference product has no per-account page either), the location
  picker extracted into `app-document-location-picker` and swept onto all 15 document forms, the
  LOCATION cell and a Billing Location filter onto all 15 lists via `ListChrome`/`ListFilter`, and
  `BillingLocation.WarehouseId` as a real prefill. **Read this before adding a field to many
  aggregates at once**: phase 32's write-path sweep guard was green while 14 of 15 detail DTOs and
  all 5 conversion templates dropped the field on the way back out, so a form could store a branch,
  never show it, and overwrite it on the next save. Also before trusting a gotcha's generalisation —
  the five handlers matching `known-gotchas.md`'s forbidden `x == null || …` predicate turned out
  **not** to be broken on SQL Server, and only injecting the old shape and running it showed that —
  and before a confirm-live pass over a catalogue, where reading all 49 report screens rather than
  one per group is what made the six exceptions (one of which breaks the obvious rule) trustworthy —
  `docs/phase-35a-status.md`
- Phase 35b: the location dimension in the reports — the other half of 35. The gate was
  `GlJournalEntry` carrying no `LocationId` while seven GL reports filter by one live, and the same
  gap turned out to exist on `StockMovement` and `StockLedgerEntry`; all three are settled by one
  rule — **an append-only fact row carries the billing location of the document that created it,
  stamped at write time**, with reversals *inheriting* rather than re-deriving (a void landing
  elsewhere leaves a branch's Trial Balance permanently off while the organization total still
  balances). On top of that: the filter on 36 report queries, their handlers and 86 endpoint
  constructions, five shared readers threaded, `app-report-location-filter` on 43 screens, the
  LOCATION column on both Master reports, and `LocationWiseReportPermission` given every report it
  was meant to govern. **Read this before filtering an append-only fact table** (the column-vs-join
  argument is Decision B, with phase-34c's "the report layer's cost is the period" as the deciding
  measurement), **before extracting a shared `Where` out of several handlers** (the first
  `GlEntryLocations` took a pre-filtered queryable and left nine handlers each responsible for
  `OrganizationId`, in a codebase with no global query filter — a helper must own every condition
  the `Where` it replaced carried), and **before writing a test that proves a location permission**:
  a `Reports.*` key cannot be granted per location at all, so a report's scope is derived from the
  caller's *transaction* grants, and the first negative E2E asserted a grant shape the API refuses
  outright. Also the reason both a server guard and a client guard exist — phase 32 is the worked
  example of a query, DTO and `.xlsx` export all carrying `locationId` for three phases while the
  screen sent nothing — `docs/phase-35b-status.md`

- **Phase 36 — allocation, forex and ageing consistency.** The phase that made two reports and two
  posting paths agree *by construction* rather than by patching, and that fixed the
  `featureGuard('MultipleWarehouses')` bug which stopped a flag-off tenant creating its first
  warehouse (the server enforces a cap; the page now shows that cap, and the route guards nothing).
  **Read this before assuming a document has exactly one GL entry** — `SingleAsync` over
  `(SourceDocumentType, SourceDocumentId)` was a habit six call sites shared and was *already* a 500
  on a twice-edited Opening Balance line; `SourceDocumentGlEntries` reverses what is **outstanding**,
  netting per location. **Before folding a settlement into the base currency**: a payment folds at the
  rate of *what it settles*, allocation by allocation, or a fully settled invoice keeps a residual
  balance equal to the realised forex, which belongs in the P&L and not in what a customer owes.
  **Before deciding two reports "already agree"**: phase 31 had patched both known divergences
  between the ageing pair and they still disagreed about which documents were ageable at all, which
  is why `OutstandingDocumentReader` now answers for both. And **before storing a set nothing
  enforces**: product-to-location was settled by one write on the live tenant plus a look at
  `performance.getEntriesByType('resource')`, which showed the picker calls
  `products-minimized?…&location_id=<the document's location>` — server-side filtering, one call per
  header change. *Display Warehouse in Column* is the deliberate omission: a single-warehouse tenant
  cannot tell it apart from *Group by Warehouse*, and a column nobody has seen is phase 8f's Annex 5
  waiting to repeat — `docs/phase-36-status.md`

- **Phase 37 — inventory policy: negative stock, returns at cost, the clearing unwind.** *Before
  removing a throw that a report guard was written against*: phase 26c's pinned oversell test was
  guarding a **fact**, not a requirement, and the replacement has to say what changed — the
  Negative Item Balance setting made the throw one of three behaviours, so Reject still throws and
  the guard's own output is now asserted rather than merely protected. **Before letting a balance
  go negative**: a shortfall is a **layer** (negative on both quantities, carried at the product's
  last known cost in that warehouse, zero when it has never been received there), and the next
  receipt fills it before the goods become stock on hand — the assumption it was issued at and the
  cost that finally covers it differ, and `filled × (real − assumed)` is the **cost catch-up**.
  **Before posting a value correction to stock**: it has to reach *three* views or two of them drift
  silently — the FIFO layers, the Inventory account, and the append-only movement history every
  dated report reconstructs from (`StockConservation.AssertHoldsAsync` asserts all three, because
  any two can be patched into agreement). **Before adding a leg to a receipt's posting**: eleven
  call sites build entries through six posting rules, so the catch-up is its **own** entry against
  the same source document — which is only available because phase 36 had already replaced every
  `SingleAsync` over `(SourceDocumentType, SourceDocumentId)`. **Before deciding what a return
  credits Inventory**: the layers give up whatever FIFO chooses, which need not belong to the
  document being returned against; credit that, debit the supplier the return price, and derive the
  difference as the **plug that balances the entry** — never as a separately computed figure, which
  is what keeps it right under the currency fold and absorbs the rounding residue in one place. And
  **before adding a NOT NULL column with a default**: a default is safe exactly when it is the truth
  about the rows already there, which is why `ValueAdjustment` needed no backfill where phase 31's
  `DueDate` did — `docs/phase-37-status.md`
