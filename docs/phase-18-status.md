# Phase 18 status — CRM completion

## TL;DR

Backend complete and fully tested (125 Domain.UnitTests, was 112; 242 Application.UnitTests, was
231; both green). Ships `IFileStorage` (local-disk dev implementation, cloud-swappable interface) as
the codebase's first file-storage abstraction, reused as-is by Phase 22 later; `Attachment`
(polymorphic like `WorkTask` but its own `AttachmentParentType` enum, starting with `Contact` only);
`ContactPersonnel` and `Comment` (both standalone entities referencing `ContactId` directly, not
Contact's encapsulated child collection — a deliberate, live-confirmed simplification, see Decision
#4); a real auto-generated Activity feed that reuses Phase 16d's `Audit`/`AuditBehavior`
infrastructure exactly as its own doc comment anticipated, rather than a new event-log mechanism;
and the full SMS surface (`SmsTemplate`, `SmsLog`, `SmsCreditLedgerEntry`, `ISmsSender`,
`SendSmsCommand`) with a live-verified atomic multi-recipient send. A genuine scope expansion,
user-approved mid-phase: Sales Order has never had an Angular UI (deliberately deferred in Phase 5,
still true as of Phase 16b) — since "Create Sales Order" is one of FR-4.6's four quick actions, a
minimal Sales Order list/detail page (mirroring Quotation) was added so the quick action has a real
target, rather than silently dropping it. One real bug hit and fixed: uploading a file through a
Minimal API `IFormFile` parameter 500s with a missing-antiforgery-middleware error unless
`.DisableAntiforgery()` is applied per-endpoint — this app has no antiforgery middleware at all (its
CSRF mitigation is the CORS origin allow-list, not antiforgery tokens). Manual E2E against a fresh
Organization confirmed: Activities correctly shows both `CreateContact` and
`CreateContactPersonnel` audit rows scoped to the right Contact; an uploaded Attachment's file
genuinely exists on disk (not just a DB row), downloads byte-identical, and both the DB row and the
on-disk file are gone after delete with zero orphans; a real cross-tenant download attempt returns
404 (not 200 with file bytes); an SMS send with insufficient credit is rejected with zero SmsLog
rows and an unchanged ledger balance (`sqlcmd`-verified); a real 3-recipient send to a ContactGroup
wrote exactly 3 `SmsLog` rows with genuinely different merge-resolved text and decremented the
ledger by exactly 3, not a page-subtotal; every new Admin-only key (`Crm.Sms.Send`,
`Crm.SmsCreditLedger.Adjust`, `Crm.SmsTemplate.Manage`) 403s a real Member naming its own exact key.
Frontend (Angular) was delegated to a background agent, then merged in and live-verified against
the real API/DB/browser by this session: Contact Personnel/Documents/Activity tabs, all 4 quick
actions (bound-value-verified, not just label-verified), and the full SMS module all work correctly
through the real UI. The agent also found a second real bug independently: `ListSalesOrdersQuery`
was missing `IRequirePermission`/`IOrganizationScoped` — investigating further surfaced the same gap
in 8 sibling List queries across Sales/Purchasing/Payments (a pre-existing, codebase-wide
cross-tenant data leak predating this phase). Fixed the one this phase's own new Sales Order UI
exposes; the other 8 are flagged as an urgent separate `spawn_task` rather than fixed silently
alongside, since they span modules outside this phase's remit. Sales Order itself (list + detail,
Draft→Approve→Void, mirroring Quotation) was verified end-to-end live: quick-action prefill →
Draft → Approve (real sequential number assigned) → appears correctly in the Sales Order list.

## Decisions

### Decision #1 — `IFileStorage` shape and local-disk layout

Minimal interface: `SaveAsync(Stream, fileName) -> key`, `OpenReadAsync(key) -> Stream`,
`DeleteAsync(key)`. No provider-shaped parameters (no bucket/container/tier) leak into the
interface, so a future cloud implementation is a drop-in `IFileStorage` with zero caller changes.
There is deliberately no "resolve to a public URL" method — every download goes through
`AttachmentsEndpoints`' permission-checked, org-scoped stream (`GetAttachmentForDownloadQuery` then
`IFileStorage.OpenReadAsync`), never a raw static path; `Program.cs` never calls
`UseStaticFiles()` at all, so nothing under the content root is web-servable regardless, but the
storage root still lives under `App_Data/attachments` (the traditional ASP.NET "non-web-servable
data" convention) as belt-and-suspenders. `RootPath` is a plain `appsettings.json`-settable option
(not user-secrets — a local disk path carries no credential, unlike `ConnectionStrings`/`Jwt`/
`Email`), defaulting to `App_Data/attachments` resolved against `IHostEnvironment.ContentRootPath`.
Keys are opaque `Guid.NewGuid()`-named files — the original file name is never used as the on-disk
name (stored separately as `Attachment.FileName`), sidestepping path-traversal and collision
concerns without sanitizing an arbitrary user-supplied name. Validation (`AttachmentValidation`):
max 10 MB, extension allow-list (pdf, png/jpg/jpeg/gif, doc/docx, xls/xlsx, csv, txt) — reasonable
Nepali-SME defaults, fixed constants not a per-tenant setting. No virus/malware scanning (explicitly
out of scope per the kickoff brief).

### Decision #2 — Attachment's polymorphic parent-type: separate `AttachmentParentType`, not `TaskParentType`

Confirmed live against both the Tigg reference product's Contact "Documents" tab and its Workflow
"Document" tab: they are visually and functionally distinct screens, not one feature reused twice.
Contact Documents is a flat, plain attachment list — drag-and-drop upload ("Drop your files or Click
to upload new document"), no extraction/conversion state, no thumbnails, no labels. Workflow
Document is a completely different AI-extraction inbox — Pending/Done status tabs, a "+ ADD AS"
menu converting an uploaded item directly into any of 16 transaction types (Quick Receipt/Customer
Payment/Invoice✨/etc.), thumbnail previews, per-row Label chips. Conflating the two into one enum
now would force Phase 22's future `UploadedDocument` (extraction status, `ConvertToTransaction`,
linked transaction) into the same shape as a plain Contact file attachment — the same kind of
awkward-fit Phase 13 avoided by not reusing `Task`/`TaskStatus` for `WorkTask`. `AttachmentParentType`
starts with just `Contact`, an additive future seam, not a speculative broader set. `Attachment`
itself still lives in `Domain.Workflow` (same bounded context as `WorkTask`, architecture-spec.md
§4.9's "cross-cutting, its own context" framing fits both) — only the enum is kept separate.

### Decision #3 — Activity log: real `Audit`-backed feed, Comments/Activities/SMS History built, Email Logs explicitly scoped out

Live-confirmed the Activity tab has exactly 4 sub-tabs: Comments, Activities, SMS History, Email
Logs. Activities is a **real, auto-generated event log**, not a stub — live-confirmed by opening a
seeded Contact's own Activity tab and seeing "demo@tiggapp.com created Customer aaaaa" rendered with
a real timestamp. This maps directly onto Phase 16d's `Audit`/`AuditBehavior` infrastructure, whose
own doc comment explicitly anticipated exactly this reuse ("this same behavior also backs the future
Contact/Organization/Product 'Activity' tab, filtered by DocumentId alone") — Phase 18 is the first
caller. `ListActivitiesQuery` filters `Audit` by `DocumentType=Contact, DocumentId=contactId`.
`CreateContactCommand`/`UpdateContactCommand` didn't previously implement `IAuditableRequest` at all
(a pre-existing gap — Contact create/update was never audited) — both now do, closing that gap
directly rather than working around it. `CreateContactPersonnelCommand`/`UpdateContactPersonnelCommand`
are named with the `Create`/`Update` prefix specifically so `AuditBehavior`'s prefix-based
`ResolveAction()` picks them up automatically, redirecting `AuditDocumentId` to the parent
`ContactId` (not the Personnel row's own new Id) via `IAuditableRequestWithId`. Attachment/Comment
commands use domain-accurate verbs (`Upload`/`Delete`/`Add`) instead and are deliberately **not**
audited — their own tabs already show the row directly, so a redundant Activities entry isn't
needed. Explicitly does **not** include WorkTask-completed or Deal-stage-changed events — both are
out of scope to modify this phase (the kickoff brief's own "Don't touch Tasks or Deals" scope
guard), so that's a known, stated limitation, not a silent drop. Email Logs: scoped out entirely —
there is no underlying capability anywhere in this codebase (the only existing `Email:*` config is
transactional auth email, not CRM outbound mail) — the sub-tab renders with an explicit "not
available yet" empty state rather than a UI that implies a working feature. Flagged via `spawn_task`
as a future-phase gap (real outbound email logging).

### Decision #4 — Contact Personnel: standalone entity referencing `ContactId`, not Contact's encapsulated child collection

The kickoff brief's own framing assumed Personnel would need the Phase 4-style full-collection-
replace treatment (`Contact.AddPersonnel`/snapshot-diff on Update, mirroring `Deal.Assignees`).
Live-confirming the actual Tigg "Add Contact Personnel" dialog showed this doesn't apply: each row is
added/edited/removed independently via its own dialog, one row at a time — never a bulk list submit.
Given that, `ContactPersonnel` is modeled exactly like `WorkTask`/`Deal` — a directly-addressable
entity with its own `CreateContactPersonnelCommand`/`UpdateContactPersonnelCommand`/
`RemoveContactPersonnelCommand`, each hitting `db.ContactPersonnel` directly with its own
`SaveChanges` — **not** an encapsulated `List<ContactPersonnel>` on `Contact` at all. This sidesteps
the Phase 4 full-collection-replace gotcha entirely, by design rather than mitigation, and matches
this codebase's existing precedent (`WorkTask`, `Deal` are both standalone aggregates referencing
their parent by Id, not owned collections on `Contact`) more closely than the kickoff brief assumed.
Field shape confirmed live against the real dialog: Name* (required), Address, Code, Phone Number,
Group (ContactGroup), Email, Organization Title (free-text role/designation, e.g. "Manager"). The
dialog's own "Select Organization" field is just the parent Contact itself (read-only in context,
since Personnel is always added from within one Contact's own detail page) — not modeled as a
separate field here.

### Decision #5 — Quick-action prefill: query-param routing to existing (and one new) Create routes

Confirmed live: "Create Invoice" from a Contact's OPTION menu is a real route navigation
(`#/sales/invoices/add?form_data={...}`), not an in-place modal — the URL genuinely changes. Tigg's
own implementation passes a JSON-encoded `form_data` query param carrying a full denormalized
Contact snapshot; this codebase instead passes a plain `?contactId={guid}`, since every target form
already has its own Contact-lookup/autocomplete mechanism (reusing `ContactsService.getContact`) —
simpler than duplicating a Contact shape into the URL and avoids staleness. Each target component
reads `route.queryParamMap` reactively (an Observable, subscribed alongside the existing
`route.paramMap` subscription), not `route.snapshot`, per the Phase 3 route-reuse gotcha, even
though `.../new` isn't normally reused — read it reactively anyway for consistency and safety.
Routing + prefill only, no new commands, no new "quick" variant components (unlike Quick
Payment/Receipt, which needed its own component because the existing Payment form's approve-gate
logic was wrong for it — that constraint doesn't apply here, these are ordinary Creates). Confirmed
live OPTION menu order: Edit, Make Inactive, Send SMS, Record Payment, Create Invoice, Create
Quotation, Create Sales Order.

**Scope expansion, user-approved:** Sales Order has never had an Angular UI at all — a deliberate
deferral recorded in `phase-5-status.md` and reconfirmed still true in `phase-16b-status.md`. Since
"Create Sales Order" is one of FR-4.6's four named quick actions and the backend (`CreateSalesOrderCommand`,
`SalesEndpoints.MapSalesOrderEndpoints`, `PermissionKeys.SalesOrder*`) has been fully built and
sitting unused since Phase 5, the user was asked whether to build a minimal Sales Order Angular page
now (closing the gap) or skip that one quick action and flag the gap separately. They chose to build
it — a minimal list/detail page pair mirroring Quotation's shape exactly (same Draft→Approve
lifecycle, same line-item editor), not a gold-plated module.

### Decision #6 — SMS: provider/credit-ledger scope, audience modes, merge fields

`ISmsSender` — log-to-console dev implementation (`ConsoleSmsSender`), per roadmap wording, no real
gateway this phase. Credit ledger (`SmsCreditLedgerEntry`, append-only — balance is `SUM(ChangeAmount)`
over every row, mirroring how `OpeningBalanceLine`/`GlLine` derive a running balance rather than a
separately-updated counter, which is also what makes `SendSmsCommand`'s atomicity trivial to
guarantee) tracks usage/decrement only; credit purchase/billing is out of scope — live-confirmed
Tigg's own "Add SMS Credit" is a static "call us at [phone]/email [address]" tooltip, not an
in-app purchase flow, so this codebase's `AdjustSmsCreditCommand` (Admin-only) is the same
"settable starting number, not a payment flow" shape as Phase 17's Opening Balances. Flagged via
`spawn_task` as a future-phase gap (real credit purchase/billing) rather than building a fake
purchase UI.

Audience mode: implements product-requirements.md FR-4.8's literal three modes (All / ContactGroup /
Custom) rather than replicating Tigg's own live mechanic (Type checkboxes for
Customer/Suppliers/Leads/Contact Persons, narrowed via a per-contact override table showing Group
for reference) — simpler, matches the written spec, and Contact Persons as a distinct SMS-audience
source is deferred (personnel don't carry their own SMS history/credit tracking this phase).

Merge-field syntax confirmed live against Tigg's own Templates screen and its "New Template" dialog
hint text: exactly `$[name]$`, `$[balance]$`, `$[balance_date]$` (not the scan's guessed `$[x]$`
shorthand). `$[balance]$` reuses `ContactStatement`'s existing `ContactLedgerReader` (internal, same
assembly) — `contact.OpeningBalance + events.Sum(SignedAmount)` as of today, the exact formula
`ContactOverviewQueryHandler` already uses for its own closing balance. Deliberate improvement over
Tigg's own limitation ("Merge tags will only work when sending SMS from the contact detail page" —
its own dialog's stated caveat): this codebase resolves merge fields for every recipient on every
send, including bulk sends, not just single-contact sends.

Cost model: flat 1 credit per recipient, not Tigg's real-gateway character-segment pricing — there's
no real gateway to bill against here, and a flat model keeps the atomic-decrement behavior simple
and deterministic to test.

Atomicity (the phase's own stated testing bar: "a mid-batch failure must leave zero partial SmsLog
rows and an unchanged ledger balance") is achieved by construction, not an explicit database
transaction: every recipient's `ISmsSender.SendAsync` call happens first, purely against the
external channel, before a single `db.SmsLogs.AddRange` + `db.SmsCreditLedgerEntries.Add` + one
`SaveChangesAsync` call at the very end. A failure partway through the send loop exits the method
before anything has been added to the `DbContext` at all — no rollback machinery needed. Credit
sufficiency is checked before the loop even starts, so "insufficient credit" fails the same way
(nothing written).

### Decision #7 — Permission-key derivation

Contact Personnel / Attachments / Comments — and a Contact's own "SMS History" activity sub-tab
(live-confirmed reachable only from within a Contact's own detail page) — all ride on the existing
`Contacts.Contact.View`/`.Manage` pair rather than new keys: live-confirmed against the Tigg
reference product, none of these sub-tabs has its own permission screen or gating distinct from the
parent Contact. SMS gets its own standalone key set (it's a distinct nav module — CRM > SMS — not a
Contact-detail sub-tab):
- `Crm.Sms.Send` — **Admin-only**, the one deliberate exception in this feature set: sending
  consumes paid credits and reaches external contacts directly, the same "flat/sensitive action" bar
  that made `Tenancy.Role.*`/`Tenancy.Organization.LockDateManage` Admin-only.
- `Crm.SmsTemplate.View`/`.Manage` — routine View/Manage pair, same shape as `Crm.LeadSource.*`/
  `Crm.DealStage.*`.
- `Crm.SmsCreditLedger.View` (Admin+Member — routine "how many credits are left" visibility, same
  Phase 8a/17-style reasoning as `OpeningBalanceView`) split from `Crm.SmsCreditLedger.Adjust`
  (Admin-only — manually crediting/correcting the balance, same sensitivity as `OpeningBalanceEdit`).
- `Crm.SmsLog.View` (Admin+Member) gates the standalone SMS module's own org-wide "SMS History" tab
  — one `ListSmsLogsQuery` serves both that tab and the per-Contact sub-tab (both Admin+Member, same
  as `ContactView`, so splitting the query by caller context would add complexity without changing
  who can see what).

All 6 new keys seeded through `RolePermissionConfiguration.HasData` continuing the GUID-tail
convention (`...02-00000000010d` through `...0118`), not a hand-written migration.

## What shipped

**Backend** (`ErpApp.Application`/`ErpApp.Domain`/`ErpApp.Infrastructure`/`ErpApp.Api`):
`IFileStorage` + `LocalDiskFileStorage` + `AttachmentValidation`; `Attachment`/`AttachmentParentType`
(`Domain.Workflow`); `ContactPersonnel`/`Comment` (`Domain.Contacts`) with
Create/Update/Remove(Personnel) and Add(Comment) commands + List queries; `ListActivitiesQuery`
reusing `Audit`; `SmsTemplate`/`SmsLog`/`SmsCreditLedgerEntry`/`SmsAudienceMode`/
`SmsCreditLedgerEntryType` (`Domain.Crm`) with full CRUD + `SendSmsCommand` +
`AdjustSmsCreditCommand` + `ListSmsCreditLedgerQuery`/`ListSmsLogsQuery`/`ListSmsTemplatesQuery`;
`ISmsSender`/`ConsoleSmsSender`; 6 new permission keys seeded Admin/Member; one EF Core migration
(`Phase18CrmCompletion` — 6 new tables, pure additive, no drops/retypes, reviewed before applying);
`AttachmentsEndpoints.cs` (new file) + extensions to `ContactsEndpoints.cs`/`CrmEndpoints.cs`.
`CreateContactCommand`/`UpdateContactCommand` retrofitted with `IAuditableRequest[WithId]` (closing
a pre-existing "Contact writes were never audited" gap).

**Tests**: Domain.UnitTests 125 (was 112) — `ContactPersonnelTests`, `CommentTests`,
`AttachmentTests`, `SmsTemplateTests`, `SmsLogTests`, `SmsCreditLedgerEntryTests`.
Application.UnitTests 242 (was 231) — `SendSmsCommandHandlerTests` (7 tests: All/ContactGroup/Custom
audience filtering, no-phone-number exclusion, per-recipient merge-field distinctness, insufficient-
credit rejection with zero writes, mid-batch-failure rollback via a `FakeSmsSender` that throws on
the Nth call) and `AttachmentCommandHandlerTests` (4 tests: parent-existence validation, upload→
download byte round-trip via a `FakeFileStorage`, cross-organization download returns `NotFound`,
delete removes both the DB row and the stored file).

**Frontend** (`web/`): Contact Personnel/Attachments("Documents")/Activity tabs on
`contact-detail-page` — new child components `contact-personnel-list`, `attachment-list`
(drag-and-drop upload + client-side extension/size gate + download/delete), `activity-panel`
(Comments/Activities/SMS History/Email Logs sub-tabs, Email Logs rendering the "isn't available yet"
empty state per Decision #3). SMS module: `crm.service.ts`/`crm.models.ts` extended; a shared
`send-sms-form` component (locked-contact mode reused by the Contact quick action, unlocked mode
used standalone) — the one deliberate simplification versus a separate "Quick SMS" component, since
nothing about Payment's own approve-gate-logic constraint (the reason Quick Payment/Receipt needed
its own component in Phase 17) applies here; `sms-shell-page` with the 4 confirmed-live tabs, routed
at `organizations/:id/sms`. 4 quick actions added to the Contact OPTION menu, each reading
`route.queryParamMap` reactively for `?contactId=` in the target component. New Sales Order module
(`sales-order-list-page`/`sales-order-detail-page`, cloned from Quotation's exact shape) closing the
Phase 5 UI gap, per Decision #5's user-approved scope expansion.

## Bugs hit and fixed

1. **Minimal API `IFormFile` parameter + no antiforgery middleware → every upload 500s.** ASP.NET
   Core auto-attaches antiforgery metadata to any Minimal API endpoint that binds `IFormFile`, even
   though nothing about the endpoint asked for it. This app has no `app.UseAntiforgery()` anywhere
   (`Program.cs`) — its CSRF mitigation is the explicit CORS origin allow-list (`Cors:AllowedOrigins`)
   plus the httpOnly JWT cookie, not antiforgery tokens, and no other endpoint in this codebase uses
   them. The failure mode is a generic `InvalidOperationException`/500 with no obvious connection to
   file upload in the error message unless you read the server's own console log — caught only by
   manual E2E against the real server (an InMemory-provider unit test never touches real Minimal API
   endpoint metadata, so nothing else would have caught this). Fixed with `.DisableAntiforgery()` on
   the one upload endpoint.
2. **`ListSalesOrdersQuery` missing `IRequirePermission`/`IOrganizationScoped` — a real cross-tenant
   data leak, pre-existing, not introduced this phase.** Found by the frontend agent while wiring the
   new Sales Order list page. Investigating further found the identical gap in 8 sibling List
   queries (`ListQuotations`, `ListInvoices`, `ListCreditNotes`, `ListPurchaseOrders`,
   `ListPurchaseBills`, `ListDebitNotes`, `ListExpenses`, `ListPayments`) — every one of these skips
   `AuthorizationBehavior`'s org-membership check entirely (the manual `Where(x.OrganizationId ==
   ...)` in each handler only controls which rows come back, not whether the caller has any
   relationship to that org at all), meaning any authenticated user can currently list another
   organization's real transactional data by guessing/enumerating its `OrganizationId`. Fixed
   `ListSalesOrdersQuery` here (the only one Phase 18 gives a real caller); the other 8 are flagged
   as an urgent separate `spawn_task` rather than fixed silently alongside, since Sales/Purchasing/
   Payments are modules outside this phase's remit.

## Manual E2E (backend, curl + cookie jar + sqlcmd against a fresh Organization)

- Contact Personnel: create → list shows it; Activities feed correctly shows both the `CreateContact`
  and `CreateContactPersonnel` audit rows scoped to the right `DocumentId`.
- Attachments: upload → confirmed the file exists on local disk at the expected `App_Data/attachments`
  path (not just a DB row) → download returns byte-identical content → delete removes both the DB row
  and the on-disk file, zero orphans. Cross-tenant: a user with no membership in the attachment's org
  gets 403 naming `Contacts.Contact.View`; a user who *is* a member of a different org gets 404 (the
  real cross-tenant proof point) — neither path returns 200 with file bytes.
- SMS: a send against 0 credit balance is rejected (409) with zero `SmsLog` rows and an unchanged
  ledger balance, `sqlcmd`-verified. A real 3-recipient send to a `ContactGroup` wrote exactly 3
  `SmsLog` rows with three genuinely different merge-resolved `Content` values and decremented the
  ledger by exactly 3 (`sqlcmd`-verified, not the API's own claimed number). Every new Admin-only key
  (`Crm.Sms.Send`, `Crm.SmsCreditLedger.Adjust`, `Crm.SmsTemplate.Manage`) 403s a real invited Member
  naming its own exact key; the Admin+Member keys (`Crm.SmsTemplate.View`, `Crm.SmsCreditLedger.View`)
  return real data for that same Member.

## Manual E2E (frontend, live browser against the same seeded data)

- Contact Personnel: added a row live, reloaded the full page (not SPA state) — persisted correctly.
  Edited it live, reload persisted the edit. Delete's own confirm() couldn't be exercised in this
  browser sandbox (native JS dialogs are disabled there) — verified via the same DELETE endpoint
  directly instead, already proven safe by the backend E2E pass above.
- Attachments: list/download/delete UI all wired to the real endpoints (network request confirmed
  200 on download); upload's native OS file-picker can't be automated in this sandbox either — the
  underlying command was already fully verified (upload→download→delete round trip) in the backend
  pass.
- Comments/Activity: posted a comment live, it appeared at the top of the feed immediately with the
  correct author/timestamp. Activities sub-tab rendered all 4 real audit-derived rows ("Phase16c
  Tester Created this contact" ×3, "Updated this contact" ×1) with no manual comment triggering them.
  Email Logs correctly shows "Email logging isn't available yet."
- Quick actions: all 4 (Send SMS, Record Payment, Create Invoice, Create Quotation, Create Sales
  Order) launched from the Contact's OPTION menu land on the correct target route with `?contactId=`
  in the URL, and in every case `document.querySelector('select').value` was read directly to confirm
  the bound form-control value is the real `contactId` GUID, not just a matching visible label.
  Create Sales Order was carried through to completion: filled a line, Saved as Draft, Approved (real
  sequential number `0001` assigned at Approve, per this codebase's numbering convention), and the
  Sales Orders list page renders it correctly.
- SMS module: all 4 tabs (Overview/SMS History/Templates/Credit History) render real seeded data,
  including the exact merge-field hint text. Created a template with `$[name]$`/`$[balance]$` live,
  sent it to a real Contact through the UI's own audience picker — credit balance decremented by
  exactly 1 in the UI, and SMS History shows the fully resolved text ("Hello Acme Traders, thanks for
  bein...") rather than the raw placeholder. Credit History's ledger rows and running balance matched
  the backend pass's `sqlcmd`-verified numbers exactly.

## Known limitations (not fixed this phase, not a regression)

- Activities feed doesn't include Task-completed or Deal-stage-changed events (Decision #3) — both
  commands are out of scope to modify this phase.
- Email Logs sub-tab has no backing capability at all (Decision #3) — flagged via `spawn_task`.
- SMS credit purchase/billing has no UI (Decision #6) — flagged via `spawn_task`.
- SMS audience modes don't include a "Contact Persons" source (Decision #6) — personnel don't carry
  SMS history/credit tracking this phase.
- 8 pre-existing List queries across Sales/Purchasing/Payments have the same missing-authorization
  bug `ListSalesOrdersQuery` had (Bug #2) — flagged as an urgent separate `spawn_task`, not fixed
  this phase (out of scope, spans modules Phase 18 didn't otherwise touch).
