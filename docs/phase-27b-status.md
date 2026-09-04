# Phase 27b — Output (print/PDF everywhere, BS in server-rendered output, the last pagers, Turnstile on the wizard, a feature-flag route guard)

## TL;DR

The five roadmap items shipped, plus `CustomTemplate`'s first two real consumers. **Print is now
wired for all 15 transactional document types**, not 6 — and the confirm-live pass that opened every
unwired type on the reference tenant changed the design: the real product prints one page *frame*
with a **varying number of titled tables** (Production Journal 3, Cash Transfer 2, Payment 2), which
phase-20d's "`Lines` XOR `GlLines`" DTO cannot express. So `PrintableDocumentDto` became an ordered
list of `PrintableSectionDto`, `DocumentPdfRenderer` went from **two layouts to one generic one that
switches on no `DocumentType` at all**, and the nine new types are cases in a handler rather than new
layouts.

**Phase-23 Decision A's carried limitation is closed.** The client now sends its calendar preference
as an `X-Calendar` header (one `HttpInterceptor`, so every existing and future download route gets it
free); `RequestCalendar` (Application) parks it for the request; PDFs and every `.xlsx` export render
business dates in Bikram Sambat when asked, with a `-BS` file-name marker and a PDF footer saying so.
Audit timestamps stay AD — exactly phase-23's own boundary.

**Terms and Conditions is on five document types, not the roadmap's two** (Quotation, Sales Order,
Invoice, Credit Note, **Purchase Order**; live-confirmed absent from Purchase Bill, Expense, Debit
Note) — a scope correction in the same shape as 27a's Custom Fields count. It stores free text on the
document, seeded from a `TermsAndConditions` `CustomTemplate` through a shared `app-terms-editor`.
`CustomerBalanceConfirmation`/`SupplierBalanceConfirmation` render as a PDF letter from the Contact
statement, agreeing with it by construction because both read `ContactLedgerReader`.

Also: the three missing pagers (Email Logs, import history, export history — all three were UI-only,
their endpoints paginated since the day they shipped); Turnstile on the New Organization wizard; and
a **feature-flag route guard** which turned out to be genuinely buildable — phase-20f found only two
flags with a real surface, but Phase 25 shipped Manufacturing, so there are three.

Tests: Domain 323 (unchanged), Application.UnitTests 722 (+16), Api.IntegrationTests 18 (unchanged),
Angular 174 (+9). `dotnet build` / `dotnet test` / `ng build` / `ng test` / `tsc --noEmit` all clean.
Manual E2E against a fresh Organization seeded by curl, with a browser pass over the new UI and
`sqlcmd` verification of every new stored field.

---

## Step 1 — confirm-live findings (Tigg UAT tenant, 2026-09-03)

The user signed in; the pass was read-only apart from expanding one collapsed block on an unsaved
form. Five questions were open; three were answerable from the repo and prior status docs, and the
two that genuinely needed the live tenant both changed the plan.

**1. Print is universal — all 9 unwired types have "View Print Preview", including both production
documents.** Every one of Credit Note, Customer Payment, Expense, Debit Note, Cash Transfer,
Warehouse Transfer, Inventory Adjustment, Production Order and Production Journal was opened and
probed. Phase 20d had sampled two types and inferred non-gating; that inference held. **Send Email is
the narrower action** — present only on Invoice, Credit Note and Customer Payment — and it is Phase
30's concern, not this one's.

Incidental finding: the reference product files Bills of Materials, Production Order and Production
Journal under its **Inventory** menu, not a Manufacturing one. Recorded, not acted on — this codebase's
own nav has carried a Manufacturing group since Phase 25 and renaming it is cosmetic churn.

**2. The print layout is one frame with N sections.** Reading the actual print output for three very
different documents side by side:

| Document | Sections printed |
|---|---|
| Production Journal | Finished Goods summary, **Raw Materials (Input)**, **Byproduct (Output)**, **Production Expenses (Input)**, then a 6-line cost summary |
| Cash Transfer | **Transferred From**, **Transferred To** + Total Transfer |
| Customer Payment | **Payment Details** (+ Net Debit), **Payment For** |

The frame never varies: organization block (name/address/phone/email/PAN, logo where set), an
optional party block, a right-aligned document title with a short label/value list, then the tables,
then a summary, NOTES, and **APPROVED BY / PREPARED BY** signature lines. This is the finding that
justified generalizing the DTO instead of writing layouts three and four — see Decision A.

**3. Terms and Conditions is a template picker over an editable body, on five types.** Expanding
"+ Add Terms and Conditions" on the Invoice add form revealed a **Select Template** dropdown listing
the tenant's Terms and Conditions templates, above a **rich-text editor** that pre-fills with the
chosen template's body and stays freely editable. Probing all eight line-item add forms:

| Carries the block | Does not |
|---|---|
| Quotation, Sales Order, Invoice, Credit Note, **Purchase Order** | Purchase Bill, Expense, Debit Note |

The dividing line is what the document *is*: the five are offers and agreements this organization
issues; the three are records of something already agreed elsewhere. The roadmap said
"Quotation/Invoice" — that was reasoning from a sample, and this is a count of real screens.

**4/5. The three questions answered without the live tenant.** Import/export history: this codebase
already has one combined Configurations > Import / Export screen with both grids (Phase 21a/21b), and
both status docs record the pager as deliberately-deferred UI work over an already-paginated endpoint
— nothing to confirm. Email Logs: same, recorded in phase-20e's own follow-up list. Wizard Turnstile:
`erp-module-scan.md` already records a check on step 1 and another on step 3 — see Decision D for why
that becomes one server-side check here.

---

## Decision A — one generic layout, not four bespoke ones

**What changed.** `PrintableDocumentDto` had `Lines` and `GlLines`, exactly one of which was
populated, and `DocumentPdfRenderer.Render` picked a layout by which. It now carries
`IReadOnlyList<PrintableSectionDto> Sections` — each a title, a column definition, rows, and an
optional bold total row — plus a header field list, a summary field list, Notes and Terms.

**Why, and what the alternative cost.** The obvious cheap move was to add a third layout for the
stock documents and a fourth for production, keeping the switch. That fails on its own terms: the
live pass showed the difference between document types is *how many tables and which columns*, not
what the page looks like. Four layouts would have meant a fifth and sixth for Phase 28's documents
and a renderer that has to know about `DocumentType` forever. The generic version is a smaller file
than the two it replaced, and **`DocumentPdfRenderer` now contains no reference to `DocumentType`
whatsoever** — that property is the whole point, and it is what a later phase inherits.

**The cost, stated.** Section rows are `IReadOnlyList<string>` — positionally matched to the column
list, with no compile-time guarantee that a row has the right number of cells. The renderer pads a
short row with blanks rather than throwing, because QuestPDF assigns table cells positionally and a
short row would otherwise shift every following row one column left and produce a plausible-looking,
wrong document. A typed-per-section alternative would need one type per section shape, which is the
per-document-type sprawl this decision exists to avoid.

**Values are pre-formatted strings.** This is a print DTO whose only consumer is a PDF renderer, so
presentation is its purpose — and it is what lets the handler render every business date through
`RequestCalendar` in one place rather than at the renderer's every call site.

## Decision B — how the calendar preference reaches the server, and what converts

Phase 23 kept the AD/BS choice in browser storage and converted at the client edge, and stated the
cost in its Decision A: server-rendered PDFs and `.xlsx` exports "remain AD regardless of the user's
setting". Three ways to close that were on the table.

| Option | Verdict |
|---|---|
| A query parameter per export route | Rejected: ~40 endpoints and ~40 call sites to thread one value that is constant for the whole request |
| Persist the preference server-side (a `User` column or `UserPreference` table) | Rejected: phase-23 already weighed and declined that for the preference itself; this phase should not smuggle it in as a side effect |
| **A request header, parked in ambient request context** | **Chosen** |

One `HttpInterceptor` adds `X-Calendar` to every request to this app's own API (and to nothing else —
a preference is still information about a person). `CalendarPreferenceMiddleware` parses it into
`RequestCalendar.Current`, an `AsyncLocal<CalendarFormat>` in `Application.Common.Formatting`.

**The ambient value is the one deliberate exception to constructor injection in this codebase, and
it is worth naming.** The consumers are `ReportSpreadsheetExporter` and its Phase 26c partial —
static classes with ~40 public methods — and the workbook is actually built inside a
`Results.Stream` callback that runs after the endpoint has returned and has no `HttpContext`. This is
the `CultureInfo.CurrentCulture` category: ambient formatting context, one writer (the middleware),
read-only everywhere else, and trivially settable in a test.

**What converts, and what deliberately does not.** Business dates convert — a `DateOnly` answering
"what date does this document bear". Audit timestamps (`CreatedAt`, `ApprovedAt`, `OccurredAt`) carry
a time of day, answer "when did this happen", and stay AD. That is phase-23 Decision A's own boundary,
restated rather than re-litigated. **Download file names keep AD dates too**, so two exports of the
same report still sort together on disk; a `-BS` marker before the extension says which calendar is
inside, and PDFs carry a "Dates shown in Bikram Sambat (BS)" footer. Out-of-range dates fall back to
the AD rendering rather than guessing, matching `NepaliDatePipe`.

## Decision C — Terms and Conditions is free text on the document, not a template reference

The stored value is the document's own text, seeded from a template. **Not** a `CustomTemplateId`:
the live editor lets the user change the text after picking a template, and a document must keep the
words it was actually issued with even after that template is edited or deleted. `SetTerms` is
Draft-only (unlike `SetCustomStatus`) because terms are part of what the document *says*, so they
follow the same rule as every other header field.

**One divergence, stated.** The reference product's editor is rich text (bold, lists, tables,
images); this is a plain `<textarea>`, because `CustomTemplate.Body` has been plain text since Phase
20d and a WYSIWYG editor is the same kind of scope 20d declined when it descoped the visual template
designer. The mechanism is identical and `app-terms-editor` is the single seam to upgrade.

**Conversions do not carry terms forward.** A Quotation → Invoice conversion starts with empty terms.
The four conversion-template DTOs would each need the field and the reference product's behaviour here
was not confirmed; the target form's template dropdown restores them in one click. Stated as a
limitation, not hidden.

## Decision D — one Turnstile check on the wizard, not three

The scan records a Cloudflare Turnstile check on wizard step 1 and another on step 3. **There is
exactly one server call behind all three steps** (`CreateOrganizationCommand` — the wizard is
client-side pagination over a single command, by Phase 1b's design), so there is exactly one token to
verify. Two widgets guarding one write would be two chances to fail and no extra protection. The
widget sits on step 3, the step that submits.

`TurnstileToken` is optional and trailing on the command so no existing caller changed, but the
handler treats a missing token exactly as a bad one and rejects **before any read** — a bot must not
be able to probe workspace-name availability through this command's failure mode. The validator adds
a `NotEmpty` rule so the common case is a 400 naming the field rather than a generic rejection.

## Decision E — the feature-flag route guard is buildable, and 20f is why that needed checking

Phase 20f found only 2 of 7 flags had any surface to gate and explicitly warned against inventing
generality for the rest. That warning is why this was checked rather than assumed: `grep` for
`TenantFeature.` now returns **24 declarations for `Manufacturing`**, because Phase 25 shipped the
BOM/Production context 20f had listed as unbuildable. So there are three real cases —
`TrackInventory` (6 routes), `Manufacturing` (6), `MultipleWarehouses` (1, plus the two Warehouse
Transfer routes which need both, matching `GetWarehouseTransferQuery.RequiredFeatures`) — 13 routes
in all. The other four flags still have no surface and no route pretends otherwise.

The guard **redirects to the organization dashboard rather than returning `false`**, which would
leave the user on a blank page with the old URL in the bar, and **fails closed** on an unreadable
subscription. It caches one in-flight request per organization: a guard is the one place in an app
where an extra round trip is felt directly as a slow navigation.

## Decision F — no new permission keys for either Custom Template consumer

Both new surfaces ride keys that already exist:

- **Print** rides each document type's own `View` key (`PrintDocumentPermissions.ViewPermissionFor`),
  extended from 6 types to 15. Printing must never widen what a role may see.
- **The balance-confirmation letter** rides `Reports.CustomerStatement.View` /
  `Reports.SupplierStatement.View`. It states one figure next to a contact's name and PAN — precisely
  what the statement already shows. The standing rule ("anything exposing PAN or contact identity is
  Admin-only") is already satisfied by those two keys, which are Admin-only; a new key could only
  have widened access or duplicated an existing decision.
- **Terms and Conditions** is a field on documents the caller can already create and update, so it is
  covered by those commands' existing keys.

## Decision G — the balance-confirmation figure comes from the shared reader

`PrintBalanceConfirmationQueryHandler` computes the balance from `ContactLedgerReader` — the same
reader Contact Statement, Contact Overview and Contact Balance Summary read — rather than calling the
statement query and taking its `ClosingBalance` (which would mean running a paginated report to read
one number). **A confirmation letter and the statement it confirms therefore cannot disagree**, which
is the entire point of the document. Phase-26b's shared-reader lesson, applied where it matters most,
and pinned by a test that asserts the two are equal.

A tenant with no `CustomTemplate` gets a built-in default letter with the same `$[placeholder]$`
merge fields substituted, rather than a blank page.

---

## What shipped

**Print / PDF**
- `PrintableDocumentDto` generalized to titled sections; `DocumentPdfRenderer` reduced to one layout.
- All 15 transactional types wired in `PrintDocumentQueryHandler`, `PrintDocumentPermissions` and
  `DocumentMechanisms.Printable`.
- A Print action on the 10 remaining detail pages (Payment has two components over one aggregate).

**Bikram Sambat in server-rendered output**
- `RequestCalendar` + `CalendarFormat` (Application), `CalendarPreferenceMiddleware` (Api),
  `calendarInterceptor` (Angular).
- Wired through `ReportSpreadsheetExporter`'s `SetCellValue` chokepoint, its six inline date sites,
  Phase 26c's `IsoDate` chokepoint, the three file-name helpers, and the print handler.

**Custom Templates' first consumers**
- `Terms` on Quotation, Sales Order, Invoice, Credit Note, Purchase Order (domain field + `SetTerms`,
  EF config, one additive migration, commands, queries, Api DTOs, client models).
- `app-terms-editor` on those five forms; `DocumentMechanisms.TermsAndConditions` classifies them.
- `PrintBalanceConfirmationQuery` + `BalanceConfirmationPdfRenderer` + two endpoints, reached from a
  Balance Confirmation button on the Customer/Supplier Statement report screens.

**The rest**
- Pagers on Email Logs, import history and export history.
- Turnstile on `CreateOrganizationCommand` and the wizard's final step.
- `featureGuard` on 13 routes.

**Guards** — `DocumentMechanismSweepGuardTests` gained six facts (print completeness, the print
permission map, the five terms types by name, and reflection over the aggregates proving each has a
`Terms` property *and* that no type outside the list grew one). `document-mechanism-sweep-guard.spec.ts`
gained two: every detail page has a print action, and the terms editor appears on exactly the five
confirmed pages and no others.

---

## Bugs and surprises

**1. The `Terms` field reached the command but not the wire — caught only by the manual E2E.**
`Terms` was added to `CreateInvoiceCommand` and its handler, the domain, EF, the migration and the
Angular model, and `dotnet build`, `dotnet test`, `tsc --noEmit` and `ng build` were all clean. The
Api-layer request records (`InvoiceRequest`, `QuotationRequest`, `SalesOrderRequest`,
`CreditNoteRequest`, `PurchaseOrderRequest`) are separate types that the endpoint maps onto the
command by hand, and they had no `Terms` — so the field silently bound to its default `null` on every
request. `GET` after `POST` returned `terms: null`, which is what surfaced it. **A trailing optional
parameter on a command is exactly the shape that hides this**: nothing fails to compile, and no
handler test can see it because handler tests construct the command directly. The lesson generalizes
past this phase and is now in `known-gotchas.md`.

**2. Two Python edit scripts damaged files in ways the compiler could not see.** One rewrote a guard
spec by slicing from an inserted block to the *end of the enclosing `describe`*, deleting four tests;
`ng test` still passed, reporting 163 instead of the expected 167. Only comparing the count against
the previous run caught it. The other converted CRLF to LF across every file it touched — invisible
in `git diff` because `core.autocrlf` normalizes it, but a real change on disk. **A test suite that
passes with fewer tests than before is a failure**, and a script that rewrites a file should be
checked by counting what it produced, not by whether the build is green.

**3. `dotnet run --no-launch-profile` starts in Production, so user-secrets do not load.** The API
died at startup with `Missing 'Turnstile:SecretKey'` — a validated-on-start option whose value is in
user-secrets, which are only added in Development. `ASPNETCORE_ENVIRONMENT=Development` is required
alongside `--no-launch-profile`. (And per the standing gotcha, Production would also have flipped
`ThrowOnBadRequest`.)

**4. The dev database's second user needed `request-verification-code` before it existed.**
Registering a user does not create a `VerificationCode` row; the code is issued by the separate
`POST /api/auth/request-verification-code` call. Worth knowing for any future E2E that needs a
Member-role identity, which is the only way to prove a document-scoped 403 as this codebase's
permissions are seeded.

---

## Manual E2E

Fresh Organization (`phase27b-e2e`), master data seeded by curl + cookie jar with every status code
printed, per phase-26c's lesson. A second Organization (`phase27b-nofeatures`) with Track Inventory,
Manufacturing and Multiple Warehouses **off**, for the route guard.

**Server-side, via curl:**

| Proof | Result |
|---|---|
| Create Organization with a Turnstile token | `201` |
| Create Organization **without** one | `400` — `{"TurnstileToken":["'Turnstile Token' must not be empty."]}` |
| Invoice created with Terms → `sqlcmd` | `sales.Invoices.Terms` = the exact text |
| Invoice PDF (AD) | `Date: 2026-08-01`, a Terms and Conditions section, `Template: Default` |
| Invoice PDF with `X-Calendar: BS` | `Date: 2083-04-16` + `Dates shown in Bikram Sambat (BS)` |
| **Cash Transfer** PDF (newly wired) | Two titled sections — Transferred From / Transferred To + Total Transfer |
| **Customer Payment** PDF (newly wired) | Payment Details (Net Debit) + Payment For (`Invoice 0002`, `2026-08-01`, `339.00`) |
| Customer Statement `.xlsx`, AD | period `2026-01-01 to 2026-12-31`, row `2026-08-01` |
| Customer Statement `.xlsx`, BS | period `2082-09-17 to 2083-09-16`, row `2083-04-16`, file name `…-BS.xlsx` |
| Balance confirmation PDF, no template | default letter, merge fields substituted, `1,678.00 DR` |
| Balance confirmation PDF, tenant template + BS | `Namaste Acme Traders (0001) - our books show 1,678.00 DR as at 2083-09-16.`, `Template: Year-end letter` |
| Statement closing balance | `1678.0 DR` — equal to the letter's figure, live |

**The negative path.** A second user was registered, verified and invited as **Member** (Member has
`Reports.CustomerStatement.View` seeded `granted: false`). Calling the balance-confirmation endpoint
as that user **against a nonexistent contact id**:

```
403 {"title":"You do not have permission to perform this action (Reports.CustomerStatement.View).","status":403}
```

and the same call as Admin against the same nonexistent id returns `404 Customer not found.` — so the
403 is genuinely authorization firing before the handler, not a masked 404. Print as the Member
against nonexistent ids of four newly-wired types (ProductionJournal, CashTransfer,
InventoryAdjustment, Payment) returns `404`, which is the positive half: those types resolve real
View keys a Member holds.

**Browser pass** (the `erp-web-ssl` profile, curl's `erp_auth` cookie transplanted via
`document.cookie` — phase-25 Step 3's recipe; the cookie is `Secure`, which is why the SSL profile
and port 4200 are both required, the API's CORS allowlist naming only that port):

- Invoice detail: Print button present; the Terms editor renders the persisted text and is
  **disabled** because the invoice is Approved.
- New Quotation: "+ Add Terms and Conditions" expands to a Select Template dropdown listing
  `Standard Sales Terms (default)`; choosing it pre-fills the textarea and the dropdown resets —
  the live behaviour exactly. Saving the draft and reading `sales.Quotations.Terms` with `sqlcmd`
  returns the full three-clause text: **a complete round-trip through the real UI**.
- Cash Transfer detail: clicking Print fires
  `GET …/print/CashTransfer/… → 200` with no error surfaced.
- Feature-flag guard: on the feature-off Organization, `/inventory/stock-position`,
  `/manufacturing/production-orders` and `/warehouses` all redirect to `/home`; on the feature-on
  Organization the same inventory route opens.
- Configurations > Import / Export: the export-history pager renders `Showing 1–1 of 1`.

**Not proven live, and why:** the import-history and Email Logs pagers were not exercised with data —
a fresh Organization has no import jobs and no alert sends, and both grids render their empty state
without a pager (the pre-existing convention). They are the same component wired the same way as the
export pager that was proven, and the sweep spec covers the wiring. The Turnstile widget itself was
not rendered in the browser (its script is a third-party load); the server gate it exists to satisfy
is proven in both directions above.

---

## Follow-ups this phase deliberately did not take

- **A rich-text terms editor** — Decision C. The seam is `app-terms-editor`.
- **Terms carried through document conversions** — Decision C.
- **`Send Email`** on Invoice/Credit Note/Payment, live-confirmed present. Phase 30's `Email`
  `CustomTemplate` type is the other half; the two belong together.
- **A logo in the printed header.** The reference product prints the organization logo; this codebase
  stores no organization logo (the wizard's upload was never built). Out of scope, and it is a
  Phase 1b gap rather than a print gap.
- **Renaming the Manufacturing nav group to sit under Inventory**, matching the reference product's
  own menu. Cosmetic; recorded in Step 1.
