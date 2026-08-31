# Phase 20d — Printing Templates / Custom Templates (FR-11.2/11.3)

## TL;DR

Confirm-live against the real Tigg tenant found Printing Templates is a genuine visual
template-authoring surface (a toggle/canvas editor for Custom Fields/Organization/Date-System
placement), not a fixed-catalog picker — building that editor was judged out of scope for this
sub-phase (user-confirmed descope, see "Scope decision" below). Shipped instead: `PrintingTemplate`
as a **metadata-only** lookup (Name + one `IsDefault` flag per `(OrganizationId, DocumentType)`,
no layout-definition field at all) and `CustomTemplate` (merge-field text, one `IsDefault` per
`(OrganizationId, Type)` across the four confirmed types). Both get full CRUD + a `SetDefault`
action mirroring the reference product's "click a thumbnail, the checkmark moves" gallery
behavior, plus new Angular admin screens. The real deliverable this phase closes — Phase 16c's
deferred print-formatted output — is a new **print-to-PDF pipeline**: a generic
`GET /api/organizations/{id}/print/{documentType}/{documentId}` endpoint, wired for 6 representative
document types across Sales/Purchasing/Accounting (Invoice, Quotation, SalesOrder, PurchaseOrder,
PurchaseBill, JournalVoucher), rendering via **QuestPDF** (chosen over a headless-browser
HTML-to-PDF pipeline — no Chromium to install/deploy, this codebase's first PDF output). Exactly
two shared layouts (line-item family, ledger family) — not one per document type or per
PrintingTemplate — since the visual-differentiation feature itself was descoped; the org's default
PrintingTemplate name is surfaced as a footer label to prove the metadata is genuinely read, not
vestigial. Domain 143 tests (+9), Application 306 tests (+18), Angular 7 specs (unchanged),
`dotnet build`/`ng build`/`tsc --noEmit` clean. Manual E2E via curl + cookie jar + sqlcmd + live
browser against a fresh Organization: full CRUD + SetDefault round-trip for both lookups verified
via `sqlcmd`; a real Invoice and JournalVoucher created/approved end-to-end and printed to actual
PDF (visually inspected — org header, line/ledger table, grand total, "Template: Modern" footer
proving the PrintingTemplate wire-up); four negative-permission proofs as a genuine Member-role
user (`Configuration.PrintingTemplate.View`/`.Manage`, `Configuration.CustomTemplate.View`, each
named exactly, the last one against a nonexistent id so it can't be confused with a 404); an
org-membership negative proof (403 naming `Sales.Invoice.View` against an org the caller isn't a
member of); a live-browser create-through-the-real-form round-trip for Custom Templates.

## Step 1 — confirm-live findings (Tigg UAT tenant)

Logged into `moonbeamtradingandsuppliers.tigguat.com` (user signed in themselves), Configurations >
Printing Templates and > Custom Templates, plus the Print action on a Sales Order and a Journal
Voucher.

1. **Printing Templates is a real visual template-authoring surface, not a catalog picker.** The
   gallery view shows ~20 pre-built layout thumbnails per document type (Standard/Modern/Minimal/
   Retail/Classic/Traditional, plus "Copy" variants), one marked default via a green checkmark — but
   "Add Template" opens a genuine editor: a left panel of toggle switches for **Custom Fields**
   (every tenant custom field, individually toggleable), **Date System**, and **Organization**
   fields (Address/Phone/Email/PAN/Website, each with its own editable placeholder text), next to a
   canvas/preview pane. This is the exact risk the kickoff brief flagged and the trigger for the
   scope-reduction decision below.
2. **Print is universal across document types sampled, not gated.** Confirmed "View Print Preview"
   present on both a Sales Order (Sales context) and a Journal Voucher (pure Accounting context, no
   natural "layout" richness beyond a debit/credit table) — unlike Phase 20b's Invoice-lacking-a-
   feature surprise, nothing here suggested Print is selectively available. It opens an overlay with
   Send Email / Download / Print buttons, rendering a document via a backend call (the UAT tenant's
   own endpoint was erroring — `{"message":"Something went wrong.","error":true}` — an environment
   issue, not a design signal, and unrelated to this codebase).
3. **Custom Templates matches the hypothesized shape.** Confirmed 4 accordion sections (Customer
   Balance Confirmation, Supplier Balance Confirmation, Terms and Conditions, Email), each expanding
   to a list of many named templates with one marked default per section. Synthetic clicks couldn't
   open a row's editor reliably on this tenant (custom click handlers didn't respond to automation),
   so the exact merge-field syntax wasn't re-confirmed live on this specific screen — Phase 18
   already established `$[placeholder]$` elsewhere in the same product (SMS Templates), reused here
   by documentation convention only, with no live validation enforcing it (the kickoff only asked
   for validation "if the real product enforces one").
4. **`Product.PrintProfileId`** — not re-confirmed to do anything observable; stays out of scope,
   consistent with Phase 3's original decision (docs/phase-3-status.md decision #1).

## Scope decision (asked, not assumed)

Per the kickoff's own guard ("if Step 1 reveals a genuine visual-authoring surface, stop and
propose a reduced scope"), the user was asked directly. Chose: **metadata-only templates** —
`PrintingTemplate` stores Name + `IsDefault` per DocumentType with **no layout-definition field**;
the Print action renders **one shared layout per document "family"** (line-item vs. ledger)
regardless of which row is marked default. This satisfies FR-11.2's literal text ("a library...
selecting one as the tenant's default") and proves the mechanism end-to-end without attempting the
toggle/canvas editor, which would have been a materially larger feature than this sub-phase was
sized for.

## Architecture decisions

- **PDF rendering engine: QuestPDF, not a headless-browser HTML-to-PDF pipeline.** This codebase's
  first PDF output (ClosedXML, Phase 16c's first binary export, is spreadsheet-only). QuestPDF is a
  pure in-process C# library (Community license, free at this project's size) — no Chromium/
  Puppeteer/wkhtmltopdf process to install, deploy, or keep patched, matching this codebase's
  standing bias against adding infra it doesn't strictly need. `QuestPDF.Settings.License` is set
  once in `Program.cs` (a static assignment, not config-dependent, so it's safe before `Build()`
  despite the config-read-before-Build gotcha applying to actual `IConfiguration` reads).
- **Exactly two shared PDF layouts, not one per document type.** `DocumentPdfRenderer.Render`
  picks its layout by which of `PrintableDocumentDto`'s `Lines`/`GlLines` is populated, not by
  `DocumentType` — a future document type needs only a new `PrintDocumentQueryHandler` case (a
  DTO-building projection), never a new layout. Application-layer handler returns a plain DTO;
  QuestPDF itself lives at the Api layer (`src/Api/Printing/DocumentPdfRenderer.cs`), the same
  Application/Api split `ReportSpreadsheetExporter` uses for ClosedXML.
- **Print rides on the target document's own View permission — no new PermissionKeys.\* entry.**
  `PrintDocumentPermissions.ViewPermissionFor(DocumentType)` mirrors Phase 20b's
  `CustomStatusPermissions.EditPermissionFor` exactly: a small switch, Admin+Member inherit whatever
  that document type's own `.View` grant already is, and an unwired `DocumentType` throws
  `ArgumentOutOfRangeException` (surfaces as a bare 500 today, same as 20b's identical pattern —
  not a regression, just an existing gap neither phase's UI can trigger since the frontend only ever
  sends a wired type).
- **PrintingTemplate/CustomTemplate permission keys: Admin-only for both View and Manage**, a
  judgment call rather than the CreditTerm/PaymentMode/CostTerm Member-View-by-default norm —
  neither table ever populates a Member-facing picker (Print itself doesn't read them for
  authorization), so there's no routine-daily-use reason for a Member to see either list; this is
  pure admin curation of a control-plane gallery/text library, the same bar Phase 14's
  `Tenancy.Role.*` set. Not itself re-confirmed live against the reference tenant's Member-role
  gating (Step 1 covered screen shape, not the permission boundary) — flag to revisit if it proves
  wrong in practice.
- **Only 6 of the ~15 printable document types get a real `PrintDocumentQueryHandler` case this
  phase** (Invoice, Quotation, SalesOrder, PurchaseOrder, PurchaseBill from the line-item family;
  JournalVoucher from the ledger family) — CreditNote, DebitNote, Expense, Payment, CashTransfer,
  WarehouseTransfer, InventoryAdjustment, ProductionOrder, ProductionJournal are explicit mechanical
  follow-up: each needs only a new case in `PrintDocumentQueryHandler.Handle`'s switch (reusing
  `BuildLineItemDocumentAsync`/`BuildLedgerDocumentAsync`) plus a `PrintDocumentPermissions` case and
  a frontend Print button, no new layout work. `PrintingTemplate.DocumentType` itself accepts any of
  the 15 (validated only by `IsInEnum()`, same as `CustomStatus`'s own DocumentType field) — the
  gallery/CRUD is defined broadly even though rendering is wired narrowly, the same "define broadly,
  wire narrowly" split Phase 20b's `CustomStatusPermissions` established.

## What was built

- **Domain**: `PrintingTemplate`, `CustomTemplate`, `CustomTemplateType` (Configuration namespace).
- **Application**: `Create`/`Update`/`SetDefault` commands for both (mirroring `CreateCostTerm`/
  `UpdateCostTerm`'s shape, plus a new `SetDefault*` pair that clears any other default in the same
  group); both reuse the generic `ListLookupsQuery<TLookup>`/`DeleteLookupCommand<TLookup>` pair.
  New `Application.Printing.Queries.PrintDocument` feature: `PrintDocumentQuery` →
  `PrintableDocumentDto` (header + either `Lines` or `GlLines`), built by two shared private helpers
  in the handler (`BuildLineItemDocumentAsync`/`BuildLedgerDocumentAsync`) that resolve Contact/
  Product/Account names via a plain join, not a second round-trip DTO.
- **Infrastructure**: EF configs + two migrations (`Phase20dPrintingAndCustomTemplates` for the
  tables; `Phase20dPrintingCustomTemplatePermissions` for the `RolePermissionConfiguration.HasData`
  seed rows — kept separate since the permission-seed change was made after the first migration was
  already applied, per the "one migration per `migrations add` invocation" discipline).
- **Api**: `DocumentPdfRenderer` (QuestPDF), `PrintingEndpoints.MapPrintingEndpoints` (the one
  generic print route), plus the two lookups' CRUD+SetDefault routes added to
  `ConfigurationEndpoints`.
- **Angular**: `PrintingTemplateListPage` (one flat table sorted by DocumentType, not 15 sections —
  most tenants will only populate a handful of the 15 offered types) and `CustomTemplateListPage`
  (4 sections, mirroring `CostTermListPage`'s 2-section split); both wired into the Configurations
  shell and routes. A shared `PrintingService`/`openBlankTabForPrint`+`openBlobInNewTab` pair (the
  two-step "open a blank tab synchronously in the click handler, navigate it once the blob arrives"
  pattern — most browsers block a `window.open()` called from an async HTTP-response callback as an
  unrequested popup, since it's outside the original click's call stack) and a "Print" button added
  to all 6 wired document types' detail pages, visible only once `Approved`.

## Known limitations / deferred (explicit, not silent)

- The real visual template-authoring editor (toggle/canvas layout builder) is not built — see
  "Scope decision." `PrintingTemplate.IsDefault`'s Name is surfaced only as a PDF footer label.
- 9 of the ~15 printable document types have no `PrintDocumentQueryHandler` case yet (see
  "Architecture decisions" for the exact list and why it's mechanical, not a new design).
- Custom Templates' merge-field body has no live syntax validation and nothing in this codebase
  actually *consumes* a Custom Template's body yet (no balance-confirmation-letter or reminder-email
  feature reads it) — same "lookup lands a phase or more before its consumer" precedent `CostTerm`
  set in Phase 20c.
- `Product.PrintProfileId` stays unbuilt (see finding #4).
- An unwired `DocumentType` passed to the print endpoint 500s rather than 400ing (see "Architecture
  decisions" — matches Phase 20b's identical `ArgumentOutOfRangeException` gap, not a new one).

## Manual E2E (fresh `Phase20d Test Org`)

1. **Printing Templates**: created `Standard` for Invoice (first one, `isDefault:true` in the
   response) → created `Modern` for Invoice (`isDefault:false`) → `PUT .../default` on `Modern` →
   `sqlcmd` confirmed the flag moved (`Modern=1`, `Standard=0`) in one query.
2. **Custom Templates**: created `Standard Letter` (CustomerBalanceConfirmation, first one →
   default). Live-browser round-trip: filled the real "New Custom Template" form (name/type/body)
   and clicked Add — `Welcome Email` (Email type) appeared under the Email section marked Default;
   `sqlcmd` confirmed both rows and both bodies persisted verbatim.
3. **Print pipeline**: seeded a Contact, Product Category, UoM, two Products (Goods and Service —
   the Goods one hit the Phase 8c stock-consumption 409 as expected for an unstocked warehouse, so
   the proof document uses the Service product instead), a Warehouse, an AccountGroup+Account chain
   (Sales/AR/Inventory/COGS) and set them as Accounting Defaults. Created and approved a real
   Invoice (code `0002`) and a real JournalVoucher (code `0001`, balanced Salary Expense/Cash lines).
   `GET .../print/Invoice/{id}` → `200`, `Content-Type: application/pdf`, a genuine 1-page PDF
   (visually confirmed: org header with Address/Phone/Email/PAN, "Invoice — 0002", Bill To/Date/
   Reference, the one line at Qty 2 × Rate 100 = Amount 200, Grand Total 200.00, footer
   "Template: Modern" — proving the previously-set default PrintingTemplate name is genuinely read,
   not hardcoded). `GET .../print/JournalVoucher/{id}` → `200`, a genuine 1-page PDF with the ledger
   table and "Template: Default" footer (no PrintingTemplate row existed for JournalVoucher,
   proving the fallback path).
4. **Negative permission proofs, as a genuine Member-role user** (registered fresh, verified via a
   `sqlcmd`-read verification code, invited into the org with `RoleId` = the well-known Member
   Guid, accepted):
   - `GET /printing-templates` → `403` naming `Configuration.PrintingTemplate.View`.
   - `POST /printing-templates` → `403` naming `Configuration.PrintingTemplate.Manage`.
   - `PUT /printing-templates/{nonexistent guid}/default` → `403` (not `404`) naming
     `Configuration.PrintingTemplate.Manage` — proves `AuthorizationBehavior` fires before the
     handler could even attempt the lookup.
   - `GET /custom-templates` → `403` naming `Configuration.CustomTemplate.View`.
5. **Org-membership negative proof** (mirroring Phase 20c's precedent, same admin user): `GET
   .../print/Invoice/{...}` against an org id the caller isn't a member of → `403` naming
   `Sales.Invoice.View` (not `404`). Unauthenticated `GET` (no cookie) → `401`.
6. **Note**: printing itself could not be shown to 403 for the Member test user under default role
   grants — `Sales.Invoice.View`/`Sales.Quotation.View`/etc. are all Member-granted by default in
   this codebase (routine daily-use documents), so the Member used for proof #4 above legitimately
   *can* print every one of the 6 wired document types. This is expected given the "ride on the
   existing View key" design, not a gap in the proof.
7. `dotnet build`/`dotnet test` (Domain 143, Application 306) clean; `ng build`/`tsc --noEmit`/
   `ng test --watch=false` (7 specs) clean. `Api.IntegrationTests` not run (Docker Desktop was not
   running — CLAUDE.md's standing carve-out).

## Next up

Phase 20f (tenant feature-flag enforcement), then Phase 20e (Alert Scheduler) — see
`docs/roadmap.md`'s Phase 20 section for the full ordering reasoning.
