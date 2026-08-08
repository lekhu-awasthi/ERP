# Build Roadmap — Phases & Task Breakdown

Companion to `architecture-spec.md`. That doc says *what* to build (bounded contexts, aggregates, engines); this doc says *in what order*, broken down small enough to actually pick up and work.

**Yes — Identity & Tenancy is correctly first.** Everything else (every aggregate in every other bounded context) carries `OrganizationId` and depends on a logged-in user belonging to an Organization. There's one thing that has to exist even before that, though: a running solution skeleton to put the code in. So the real order is **Phase 0 (scaffold) → Phase 1 (Identity/Org)**, then the rest.

Guiding rule for phase sizing: each phase should end with something *runnable and demonstrable* (an API you can hit in Swagger, a screen you can click through), not just "code exists." Each phase below is further broken into tasks small enough to be a single sitting's work (roughly half a day to two days each for one person).

---

## Phase 0 — Solution scaffolding
**Goal:** empty-but-real Clean Architecture solution, builds, runs, deploys to a dev SQL Server, CI green. No business logic yet.

1. Create the .NET solution: `Domain`, `Application`, `Infrastructure`, `Api` projects with correct project references (per `architecture-spec.md` §1); add `.editorconfig` + solution-wide analyzers/nullable-enable.
2. Wire MediatR + FluentValidation into `Application`; add the pipeline behavior stubs (`ValidationBehavior`, `LoggingBehavior`) — no-op bodies for now, just prove the pipeline fires.
3. Add EF Core + SQL Server provider to `Infrastructure`; create an empty `AppDbContext`, connection string via `appsettings`/user-secrets, first no-op migration, confirm `dotnet ef database update` works against a local/dev SQL Server (or a Docker container).
4. Stand up `Api` as minimal-API or thin controllers, wire DI (MediatR, DbContext, validators) in `Program.cs`, add Swagger, confirm `GET /health` returns 200.
5. Create the Angular workspace (latest LTS), default routing shell, environment config pointing at the API's Swagger-documented base URL, confirm `ng serve` renders a blank shell that successfully calls `/health`.
6. Set up test projects (`Domain.UnitTests`, `Application.UnitTests`, `Api.IntegrationTests` with `WebApplicationFactory` + Testcontainers SQL Server) — one trivial passing test in each, just to prove the harness works.
7. CI pipeline (GitHub Actions or whatever you're using): build + test on push, for both the .NET solution and the Angular workspace.

*Exit criteria: `dotnet build`, `dotnet test`, `ng build` all green in CI; API and Angular shell both run locally against a real SQL Server.*

**Status: COMPLETE as of 2026-08-08** — see `phase-0-status.md` for the full history (bugs hit, fixes applied, CI now green at `https://github.com/lekhu-awasthi/ERP`).

---

## Phase 1 — Identity & Tenancy
**Goal:** a user can register, verify their email, log in, create an Organization through the 3-step wizard, and land on an Organization's dashboard shell. This is the thinnest possible vertical slice through the whole stack — auth, a real aggregate, EF Core persistence, and an Angular flow — and it's the pattern every later phase will repeat.

### 1a. User & auth (Identity context)
1. `User` entity + EF Core mapping (schema `identity`); `RegisterUserCommand` (Full Name, Email, Phone, Password) → hash password (ASP.NET Core Identity's hasher, or a custom one if you're not pulling in full Identity), create row, status `EmailUnverified`.
2. Email verification: `VerificationCode` value/table (code, expiry, userId), `RequestVerificationCodeCommand` (sends email — stub the email sender behind `IEmailSender` for now, log-to-console implementation), `VerifyEmailCommand` (checks code, flips user to `Active`).
3. `LoginCommand` (email+password) → issue JWT (or cookie, decide based on how Angular will consume it — JWT in an httpOnly cookie is the safer default for a SPA). `ForgotPasswordCommand` / `ResetPasswordCommand`.
4. Angular: Register page, email-verification-code entry page, Login page, route guards for authenticated routes. Skip the Cloudflare Turnstile bot-check for now — flag it as a Phase-1.5/later hardening item, not a blocker for a working vertical slice.

### 1b. Organization & membership (Tenancy context)
5. `Organization` aggregate + EF Core mapping (schema `tenancy`): Name, Industry, Address, AccountingStartDate, IsVatRegistered, WorkspaceName (unique), LockDate?.
6. `CheckWorkspaceNameAvailabilityQuery` — the debounced-availability-check endpoint (confirmed live UX pattern from the scan); simple `EXISTS` query, but build it as its own query now since Angular will call it on every keystroke (debounced client-side).
7. `CreateOrganizationCommand` — the single command backing the whole 3-step wizard (per architecture-spec.md §4.1): creates Organization, seeds a default `TenantSettings` row, seeds a default `TenantSubscription` (15-day trial, per the scan), records the Accounting Features checkbox selections as entitlement flags. This is the "set several things in one shot" command flagged in the spec — resist the urge to split it into 3 separate commands per wizard step; the wizard is client-side pagination over one command's input.
8. `OrganizationMembership` join entity: creating an Organization auto-creates a membership row for the creator with an Admin-equivalent role (role catalog itself can be a stub/hardcoded "Admin" role until Phase 1c).
9. `InviteUserCommand` (email + roleId) → creates a pending `OrganizationMembership` + sends an invite email (same `IEmailSender` stub); `AcceptInvitationCommand` / `AcceptRequestCommand`.
10. `MyOrganizationsQuery` — powers the "Your Organization / Requests / Invitation" 3-tab landing page.
11. Angular: Organization List (3 tabs), New Organization wizard (3 steps, calling #6 live and #7 on final submit), post-creation "Welcome" state, company switcher shell (even if it only ever has one org to switch between at this point).

### 1c. Minimal role/permission stub (just enough to unblock later phases)
12. `Role` + `RolePermission` tables per architecture-spec.md §3.7, but **seed only 1–2 hardcoded roles for now** (Admin = everything, Member = read-only) rather than building the full Role Reference editor UI — that editor is real work and belongs in its own later phase once there are enough document types with real permissions to make it meaningful. The goal here is just: every command has *somewhere* to check a permission, even if the permission set is trivial.
13. `IAuthorizationBehavior` pipeline wiring (checks a hardcoded-for-now permission key), so every command handler built from Phase 2 onward already goes through the real authorization pipeline instead of it being bolted on later.

*Exit criteria: a fresh user can register → verify email → log in → create an Organization via the 3-step wizard → land on an (empty) Organization dashboard. Second user can be invited and accept. All of it backed by real EF Core/SQL Server persistence, no mocked data.*

---

## Phase 2 — Configuration foundation
**Goal:** the tenant-wide control-plane lookups that almost every later document type will reference, built once so Sales/Purchase/Accounting don't each reinvent "a tenant-editable named list."

1. Generic `LookupList<T>` CRUD pattern (Application-layer generic command/handler pair) + first concrete lookups: `CreditTerm`, `PaymentMode`, `CustomStatus` (per document type), `ReportingTagCategory`/`Option`.
2. `TenantSettings` singleton-per-tenant aggregate (Suggest Selling Price mode, Product Price Basis, Inventory Tracking mode, Negative Cash/Stock Balance actions) — stub sensible defaults; the UI to edit it can come later.
3. `DocumentNumberingRule` + `IDocumentNumberGenerator` service (architecture-spec.md §3.1) — build and unit-test this now even though nothing uses it yet, since every transactional aggregate from Phase 4 onward needs it on `Approve()`.
4. `CustomFieldDefinition` + generic `CustomFieldValue` (EAV) — build the definition CRUD; the "render custom fields inline on a form" Angular component can wait until a real document type exists to attach it to (Phase 4+).
5. Angular: a simple Configurations shell with the lookup-list screens wired to #1–#2.

*Exit criteria: Configurations screens work end-to-end for at least CreditTerm and PaymentMode; DocumentNumberingRule and IDocumentNumberGenerator are unit-tested and ready to be called by real documents.*

---

## Phase 3 — Contacts & Catalog
**Goal:** Customer/Supplier/Lead and Product master data exist — the two things every Sales/Purchase document needs to reference.

1. `Contact` aggregate (Customer/Supplier/Lead), CRUD commands, `ContactGroup` tree.
2. `Product` aggregate (Goods/Service), `ProductCategory` tree, `UnitOfMeasurement`, `ProductSecondaryUnit`.
3. Angular: Contacts list/detail, Products list/detail — establishes the reusable list-page and record-detail-page chrome patterns (from the scan doc's UI-pattern notes) that every later module's screens will reuse.

*(Variant Products/Attributes can be their own later task inside this phase or deferred to a later phase — they're not on the critical path to a working Sales/Purchase chain.)*

---

## Phase 4 — Accounting core
**Goal:** Chart of Accounts exists, and the GL posting engine (architecture-spec.md §3.4) is real and tested — this has to exist *before* Sales/Purchase, since Invoice/PurchaseBill approval posts to it.

1. `AccountGroup` (tree, 5 root types) + `Account` (leaf).
2. `JournalVoucher` aggregate — first real `ApprovableTransaction`, first real use of `IDocumentNumberGenerator` (Phase 2) and the balanced-GL invariant (`sum(debit)==sum(credit)`).
3. `IGlPostingRule<T>` abstraction + the shared `GlJournalEntry.Post()` factory (architecture-spec.md §3.4) — built and tested against JournalVoucher now, ready for Invoice/PurchaseBill to plug into later.
4. `CashTransfer` (simplified UI over JournalVoucher, fan-out to multiple destination accounts).
5. Angular: Journal Voucher create/list/detail (this becomes the template every later transactional screen clones).

*Exit criteria: a Journal Voucher can be created, saved as Draft, approved (assigned a real number, GL-posted), and viewed with its GL Transactions section — the full Draft→Approve→GL-post pattern proven once, cleanly, before Sales/Purchase repeat it.*

---

## Phase 5 — Sales chain
**Goal:** Quotation → Invoice → Customer Payment, live, matching the hands-on pass documented in `erp-module-scan.md`.

1. `Quotation` aggregate + CRUD/Approve.
2. `Invoice` aggregate + CRUD/Approve — first real use of `IGlPostingRule` for a non-JournalVoucher type, first `WarehouseId` requirement, first Negative Stock Balance policy check (stub `StockAvailabilityPolicy` returning `Ok` always until Phase 7's real stock ledger exists — sequence this deliberately, see note below).
3. `GetInvoiceConversionTemplate(quotationId)` query + Angular "Convert to Invoice" flow (architecture-spec.md §3.3's server-computed pre-fill, replacing Tigg's client-side URL-payload trick).
4. `Payment` aggregate (Direction=Received) + `PaymentAllocation` — Customer Payment, with the live GL-preview-before-approve behavior confirmed in the hands-on pass.
5. `SalesOrder` (standalone, not a conversion target) and `CreditNote` (conversion target of Invoice) — same patterns as above, can be split into their own sub-tasks.
6. Angular: Quotation/Invoice/Customer Payment create/list/detail, reusing Phase 4's template.

**Sequencing note:** Invoice's stock decrement (and the real Negative Stock Balance check) genuinely depends on Phase 7's FIFO stock ledger existing. Two honest options: (a) stub stock behavior in Phase 5 and wire it for real once Phase 7 lands, or (b) pull a minimal slice of Phase 7 (just enough StockLedgerEntry to support decrement+availability-check) forward into Phase 5. Recommend (a) — ship the Sales chain's accounting/document behavior first, backfill stock enforcement once Inventory is built, rather than blocking the whole chain on Inventory being done.

*Exit criteria: reproduces the hands-on pass end to end — Quotation approved, converted to Invoice, Invoice approved (GL posted), Payment recorded and approved (GL posted, allocation applied) — all through the real UI, not just API calls.*

---

## Phase 6 — Purchase chain
**Goal:** Purchase Order → Purchase Bill → Supplier Payment — structurally identical to Phase 5, so this phase should be fast (clone the pattern) plus the purchase-specific additions.

1. `PurchaseOrder` + `PurchaseBill` (adds: Supplier Invoice Reference, Is Import/Import Details, TDS fields + TDS calculation, `ExpenditureClassification` per the Annex-13 open item).
2. `Payment` reused with Direction=Paid (Supplier Payment) — should require near-zero new code if Phase 5's Payment aggregate was built generically.
3. `Expense` (account-based lines, no Product) + `DebitNote`.
4. Angular: mirrors Phase 5's screens.

*Exit criteria: reproduces the Purchase-side hands-on pass end to end, same bar as Phase 5.*

---

## Phase 7 — Inventory & stock ledger
**Goal:** real FIFO costing — retrofits Phase 5/6's stubbed stock behavior into the genuine article.

1. `StockLedgerEntry` (FIFO layer) model, scoped `(ProductId, WarehouseId)`.
2. `StockAvailabilityPolicy` (Reject/Warn/DoNothing per `TenantSettings`) — replaces Phase 5's stub; wire the `overrideWarning` flag through the Invoice-approve command.
3. Wire real decrement into `InvoicePostingRule`'s approval path, real increment into `PurchaseBillPostingRule`'s.
4. `WarehouseTransfer`, `InventoryAdjustment`.
5. Angular: Inventory Ledger / stock position views.

---

## Phase 8+ — later phases (sequence TBD once Phase 1–7 are underway)
Roughly in priority order, but worth revisiting once earlier phases surface real constraints:
- **Workflow**: Tasks (polymorphic), Transaction Approval queue (now meaningful — Phases 4–7 produced real Draft-status documents to approve), Document inbox (AI-extraction can be a stretch goal, not a blocker).
- **Reports**: Trial Balance / Balance Sheet / Income Statement first (pure GL queries, no new writes needed), then Sales/Purchase Master Reports, then the Nepal-specific statutory ones (VAT Summary, TDS Report, Annex 13/5).
- **CRM**: Deals, SMS — lower dependency risk, can slot in almost anywhere once Contacts (Phase 3) exists.
- **Role Reference full editor**: upgrade Phase 1c's hardcoded-role stub into the real per-document-type permission-matrix editor, once enough document types (Phases 4–7) exist to make the matrix meaningful.
- **Manufacturing** (BOM/Production Order/Production Journal): confirm scope with the user before committing — flagged in the architecture spec as a phase-2-or-later candidate, not v1-critical.
- **Multi-currency, multi-warehouse depth, Cheque Register, Alert Scheduler, Printing/Custom Templates**: polish/breadth items, sequence opportunistically.

---

*This roadmap should be treated as a living doc — re-order/re-scope phases as real constraints surface during Phase 0/1. Once Phase 1 is underway, the next planning pass should turn Phase 1's numbered tasks above into actual tracked work items (issues/tickets) rather than staying as prose here.*
