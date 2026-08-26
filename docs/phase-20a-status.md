# Phase 20a status — Custom Fields reach the forms

**TL;DR:** Built the deferred write-side half of Phase 2's EAV Custom Fields system
(`SetCustomFieldValuesCommand`/`GetCustomFieldValuesQuery`, riding on the document's own Edit/View
permission like Phase 19's Reporting Tags) plus a `ChoiceOptions` field `CustomFieldDefinition` never
had. A shared `app-custom-fields-editor` Angular component renders a document type's applicable
fields inline in its create/edit form — live-confirmed against the real Tigg tenant to be structurally
different from Reporting Tags: inline in the main form (not gated behind an "Add/Edit" action), no
Required flag exists at all in the reference product, and editability isn't locked by Draft/Approved
status. Wired into Quotation and Invoice only, per the roadmap's own scope guard. Domain 126 tests
(+1), Application 269 tests (+12), Angular 7 specs (unchanged), `ng build`/`tsc --noEmit` clean.
Manual E2E via curl + sqlcmd + live browser against a fresh Organization: full create-time and
edit-time round-trips proven through the real UI for Text/Number/Choices fields on both document
types, a 400 for an invalid Choices value, and a 403 naming `Sales.Quotation.Edit` proven against a
nonexistent document id with a custom Role. Both outstanding Phase 19 flag-and-abandon items
(Reporting Tags admin screen, Purchase/COGS double-expense) re-flagged via `spawn_task` a second time,
not fixed inline — see "Known limitations" below for reasoning.

## Confirm-live decisions (step 2 of the kickoff prompt)

All confirmed against the real Tigg UAT tenant (`moonbeamtradingandsuppliers.tigguat.com`),
Configurations > Custom Fields and a real "Add New Invoice" form, per the Phase 8f confirm-live
discipline — nothing here was defaulted from the scan alone.

1. **Field layout: inline in the main form, not a collapsible section.** A plain "Custom Fields"
   heading with a 3-column grid of inputs sits between the Lines table and the "TDS is applicable"
   toggle on the real Invoice create form — no expand/collapse control, no separate card action.
   This codebase places the equivalent section between the header-details card and the Lines card
   instead (matching `ReportingTagsEditor`'s existing insertion point, per the kickoff prompt's own
   instruction to mirror that pattern) — a deliberate divergence from Tigg's exact visual position,
   justified by consistency with this codebase's own established layout, not a missed confirmation.
2. **Field type rendering:** Text and Number render as plain inputs (`type="text"` /
   `type="number"`); Choices renders as a real `<select>` fed by the field's own configured option
   list (confirmed via the "+ADD NEW FIELD" form's "Choices" selection revealing an "Option 1 / +Add"
   list editor — a control `CustomFieldDefinition` never had a field for, see Bug #1 below). This
   codebase renders Description as a `<textarea>` — not directly observed live (no Description-type
   field existed on the one document type opened), a defensible reading of the type name that
   doesn't contradict anything confirmed.
3. **No Required flag exists anywhere in the reference product's Custom Fields feature** — the
   "+ADD NEW FIELD" form has only a Field Name, a Type dropdown, and per-document-type checkboxes.
   No required-field enforcement was built; Approve is never blocked by a missing custom field value.
4. **Values save together with the document's own Save action** — confirmed by Custom Fields
   appearing on the *create* form itself (unlike Reporting Tags, which are explicitly a
   post-creation-only action per Phase 19 decision #1) and by there being exactly one "Save" button
   on the page, no independent per-field or per-section save. Backend-wise this still needs its own
   command (`SetCustomFieldValuesCommand`), since `DocumentId` doesn't exist until Create returns —
   the Angular editor calls it right after the parent page's own Create/Update succeeds, invisible to
   the user as a second network call under the same click.
5. **Draft-vs-Approved editability is NOT restricted** — confirmed by opening an Approved Invoice's
   "Edit" action in the live tenant: the Custom Fields section renders and is fully editable, same as
   on a Draft document. This is deliberately different from this codebase's own header/line fields,
   which *are* locked once Approved (`[disabled]="!isDraft()"` throughout `invoice-detail-page.html`)
   — Custom Fields carry no GL/financial weight, so this phase does not lock them, matching the
   reference product's specific behavior for this one section rather than this codebase's general
   Approve-locks-everything convention.

## Permission-key derivation

`SetCustomFieldValuesCommand`/`GetCustomFieldValuesQuery` ride on the target document's own
Edit/View permission (`Sales.Quotation.Edit`/`Sales.Quotation.View`, `Sales.Invoice.Edit`/
`Sales.Invoice.View`) via a `CustomFieldValuePermissions` static class — the exact same reasoning
Phase 19 used for `TransactionReportingTagPermissions`: setting/reading custom field values is a
detail-page/form edit action on an existing document type, not a distinct capability that needs its
own key, and Admin/Member already have the right split via that document type's existing grants.
**No new `PermissionKeys` constants and no new `RolePermissionConfiguration.HasData` rows were
needed** for the value-write side. `CustomFieldDefinition`'s own admin CRUD keeps its pre-existing
Phase 2 keys (`Configuration.CustomFieldDefinition.View`/`Manage`, Member View-only/Admin-write) —
unchanged by this phase, since no admin UI was built for it (see Scope guard below).

Only Quotation and Invoice are supported by `CustomFieldValuePermissions`; any other `DocumentType`
throws `ArgumentOutOfRangeException`, mirroring `TransactionReportingTagPermissions`'s exact shape —
rolling out to the other 15 applicable document types is explicitly deferred (see Scope guard).

## What shipped

- **Domain:** `CustomFieldDefinition.ChoiceOptions` (`IReadOnlyList<string>`), required by
  `Create`/`Update`. EF-configured as a delimited string using the ASCII Unit Separator (U+001F)
  rather than a comma — unlike `ApplicableDocumentTypes` (enum names, safe to comma-join), option
  text is tenant free-text and could itself contain a comma.
- **Migration:** `AddCustomFieldDefinitionChoiceOptions` — single `AddColumn` with an empty-string
  default backfill for every pre-existing row. Reviewed by hand (no drops, no reordering needed) and
  applied to the local dev database.
- **`SetCustomFieldValuesCommand`** (`Configuration.Commands.SetCustomFieldValues`) — replace-the-
  whole-set for one `(OrganizationId, DocumentType, DocumentId)`, same delete-then-insert shape as
  `SetTransactionReportingTagsCommand`. Validates: the field definition exists and belongs to the
  organization (`NotFoundException`, 404), the field actually applies to the target document type
  (`FluentValidation.ValidationException`, 400 — thrown directly from the handler, the same exception
  type `ValidationBehavior` throws, already specially handled by `ExceptionHandling.cs`), and a
  Choices-type value is one of the field's own `ChoiceOptions` (same 400). A blank value is not
  stored (skipped, not written as an empty-string row).
- **`GetCustomFieldValuesQuery`** — a minimal `(FieldDefinitionId, Value)` list for one document;
  the Angular editor separately loads the *definitions* (name/type/options) via the existing
  `ListCustomFieldDefinitions` admin query (already Member-View-granted since Phase 2) and merges
  the two client-side — no new "give me the definitions applicable to my role" query was needed.
- **`CreateCustomFieldDefinitionCommand`/`UpdateCustomFieldDefinitionCommand`** extended with
  `ChoiceOptions` (required non-empty when `Type == Choices`, validated per-option non-empty/max
  100 chars) — necessary so `curl` (this phase's only way to seed a Choices-type definition, see
  Scope guard) can actually populate the option list `SetCustomFieldValuesCommand` validates against.
- **Api:** `GET`/`PUT /api/organizations/{id}/configuration/custom-field-values/{documentType}/
  {documentId}`, alongside the existing reporting-tags routes in `ConfigurationEndpoints.cs`.
- **Angular:** `app-custom-fields-editor` (`web/src/app/shared/custom-fields/`) — loads applicable,
  active definitions for the given `(organizationId, documentType)`, loads existing values when a
  `documentId` is already known, renders inline inputs (native `<select>` per definition 3.1's
  `[selected]`-not-`[value]` gotcha for Choices), and exposes a public `commitTo(documentId):
  Observable<void>` the parent page calls right after its own Create/Update succeeds. Wired into
  `quotation-detail-page`/`invoice-detail-page` between the header card and the Lines card (the
  `ReportingTagsEditor` insertion slot), rendered unconditionally (including on `.../new`, unlike
  `ReportingTagsEditor`) via `viewChild(CustomFieldsEditor)` + `?.commitTo(id).subscribe(...)` in
  both the create and update branches of `saveDraft()`.

## Scope guard

Per the kickoff prompt's own instruction: only Quotation and Invoice got the shared component wired
into their Angular pages and their own `EnsureDocumentExistsAsync`/permission-mapping support in the
backend. `CustomFieldDefinition` itself already applies to all 17 document types (unchanged, still
admin-manageable via `curl`/the API) — extending `CustomFieldValuePermissions`,
`SetCustomFieldValuesCommandHandler.EnsureDocumentExistsAsync`, and the Angular wiring to the
remaining 15 document types is explicit, mechanical follow-up work, not attempted here. No admin
Angular screen for managing `CustomFieldDefinition` itself (create/edit field definitions with their
choice options) was built this phase either — that gap already existed since Phase 2
(`configuration-shell.ts`'s own doc comment lists it as a known missing screen) and this sub-phase's
mandate was the value-*write* side reaching documents, not closing that pre-existing definition-CRUD
UI gap. Manual E2E seeded all three field definitions via `curl` instead, per the Testing bar.

## Bugs hit and fixed along the way

1. **`CustomFieldDefinition` had no field for a Choices-type field's own option list at all** —
   `ChoiceOptions` didn't exist anywhere in the Domain/Application/Api layers before this phase, even
   though the live reference product's "+ADD NEW FIELD" form reveals a real "Option 1 / +Add" list
   editor the moment "Choices" is selected. This would have been silently missed without the
   confirm-live pass — `docs/erp-module-scan.md`'s own scan-era data model comment
   (`CustomFieldDefinition { id, name, type, choiceOptions[]?, applicableDocumentTypes[] }`) already
   named the field, but Phase 2's actual implementation never added it. Caught only by opening the
   live "+ADD NEW FIELD" form and picking "Choices", not by re-reading the scan or the existing code.
2. **A C# char literal for the ASCII Unit Separator (`'\u001F'`) round-tripped through the Edit tool
   as the literal invisible byte rather than the six-character escape sequence**, making later
   string-based edits to the same line silently fail to match (the tool's `old_string` and the file's
   actual bytes differed at a level invisible in any terminal or diff view). Worked around with a
   direct Python byte-level `bytes.replace()` pass instead of text-based tools once the divergence was
   diagnosed via `xxd`. Not a codebase bug — a tooling gotcha worth remembering for any future
   non-printable-character literal.

## Known limitations

- **Custom fields are wired to Quotation and Invoice only.** The remaining 15 applicable document
  types (Sales Order, Credit Note, Customer Payment, Quick Receipt, Purchase Order, Purchase Bill,
  Expense, Debit Note, Supplier Payment, Quick Payment, Journal Voucher, Cash Transfer, Production
  Order, Production Journal) need the same three-line change per page (import `CustomFieldsEditor`,
  add the `viewChild` + `commitTo` calls, insert the template tag) plus one line each in
  `CustomFieldValuePermissions`/`EnsureDocumentExistsAsync`. Purely mechanical, explicitly deferred
  per the roadmap's own framing.
- **No Angular admin screen exists for managing `CustomFieldDefinition` itself** (creating fields,
  setting their choice options, toggling active/inactive) — a Phase 2 gap this sub-phase did not
  close, since its mandate was the value-write side. `curl` remains the only way to define a field
  in this codebase today.
- **Reporting Tags admin screen (Configurations > Reporting Tags category/option management)** —
  re-flagged via `spawn_task` again this session. This is the **second** flag-and-abandon cycle (first
  was during Phase 19). Not fixed inline: building it properly means also wiring category creation,
  option creation scoped to a category, and the two-step delete-confirmation pattern Phase 2 already
  established (`docs/phase-2-status.md` decision #11) — a full page, not a quick addition, and this
  session's budget went to the sub-phase's actual mandate instead.
- **Purchase/COGS double-expense in Income Statement's Net Profit** — re-flagged via `spawn_task`
  again this session. This is also the **second** flag-and-abandon cycle (first was during Phase 19).
  Not investigated further here; unrelated to Custom Fields.

## Manual E2E (fresh Organization, curl + sqlcmd + live browser)

All against a freshly created Organization (`Phase20a Test Org`), the reusable Admin test login
(`Testing:*` user-secrets), and a second real registered-and-DB-activated user for the negative
permission proof (same pattern as Phase 19's report-permission proof) — no test credentials
committed.

1. Seeded via `curl`: a Customer contact, a Quotation, three `CustomFieldDefinition`s (Text "Batch
   No", Number "Warranty Months", Choices "Color" with options Red/Blue/Green) applicable to both
   Quotation and Invoice, a Warehouse, a Product Category, a Unit of Measurement, and a Service-type
   Product (needed only so the real UI's Save button had a valid line to submit against).
2. `PUT custom-field-values/Quotation/{id}` with all three fields set → `204`; `sqlcmd` against
   `configuration.CustomFieldValues` confirmed all three rows with correct `Value`s.
3. `PUT` an invalid Choices value (`"Purple"`) → `400` with body naming the field and the rejected
   value (`"'Purple' is not a valid option for 'Color'."`) — not a silent accept.
4. `GET` with no auth cookie → `401`.
5. **Negative permission proof:** created a custom Role (`NoQuotationEdit`) granting
   `Sales.Quotation.View=true` but explicitly denying `Sales.Quotation.Edit=false`; registered a
   second user, DB-activated their email via `sqlcmd`, invited them with that role, accepted the
   invitation, logged in as them. `PUT custom-field-values/Quotation/{a nonexistent guid}` →
   **`403`** naming the exact key (`"...(Sales.Quotation.Edit)."`) — proves `AuthorizationBehavior`
   fired before the handler could 404 on the fake id. `GET` against the *real* Quotation id as the
   same restricted user → `200`, confirming the View/Edit split is genuinely granular, not a blanket
   membership check.
6. **Live browser, real UI, both document types:**
   - Reloaded the seeded Quotation's detail page: Custom Fields section rendered between Reporting
     Tags and Lines, pre-filled with `BATCH-001`/`Red`/`12` exactly as seeded — the Choices
     `<select>` correctly listed Red/Blue/Green with Red selected.
   - Edited the fields through real clicks/typing (`BATCH-002`, `Blue`) and clicked the page's own
     "Save Draft" — network trace confirmed the real `PUT custom-field-values/...` fired (`204`)
     alongside the Quotation's own save call; `sqlcmd` confirmed `BATCH-002`/`Blue`/`12` persisted.
   - Opened `.../invoices/new` (a real Create form): Custom Fields section rendered immediately
     (unlike Reporting Tags, correctly absent until the document exists), filled in Text/Choices/
     Number values, filled Customer/Warehouse/one Line, clicked Save Draft. The page navigated to the
     newly created Invoice's own detail page and the Custom Fields section reloaded showing
     `INV-BATCH-A`/`Red`/`6` — the exact create-time round-trip this sub-phase's design decision #4
     depends on.
   - No browser console errors on either page at any point.
7. `dotnet build`/`ng build`/`tsc --noEmit` all clean; `dotnet test` on Domain.UnitTests (126, +1)
   and Application.UnitTests (269, +12) both green; `ng test --watch=false` (7 specs, unchanged)
   green. `Api.IntegrationTests` not run this session — Docker Desktop was not running (see
   CLAUDE.md's own noted carve-out for that suite).

## Exit criteria — final status

1. ✅ Text, Number, and Choices custom fields defined for Invoice all appear on a real Invoice's
   detail page, in the confirmed-live shape (inline, no Required gate, no Draft/Approved lock).
2. ✅ Saving round-trips: reloaded the page and re-fetched via `curl`/`sqlcmd`, values persisted.
3. ✅ A Choices field rejects a value outside its own option list — `400`, not a silent accept.
4. ✅ The same 3 field types work correctly on Quotation too, proving the shared component
   generalizes across document types.
5. ✅ Permission-key derivation recorded above with reasoning.
6. ✅ Manual E2E fully green per the Testing bar (positive, negative-validation, and
   negative-permission paths all proven; persisted data verified via `sqlcmd`, not just the API's own
   response).
7. ✅ Zero regressions: clicked through both Quotation and Invoice forms with the new Custom Fields
   editor and the existing (Phase 19) Reporting Tags editor both present — no conflicts, no console
   errors, existing Save/Approve/Void/Convert actions untouched.
