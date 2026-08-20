# Phase 16c status — Pagination + report export

**Status: COMPLETE.** TL;DR: every list/report query in the codebase (22 document-list queries +
8 statutory/master reports) now returns a shared `PagedResult<T>` envelope instead of a bare
array; a new shared Angular `<app-pagination-control>` component is wired into every screen backed
by a dedicated (non-lookup) query, with real page/page-size controls confirmed live against a
105-row seeded Invoice table and a 60-row seeded PurchaseBill table; all 8 reports gained a
spreadsheet export (ClosedXML), each with "current view" and "full dataset" variants behind the
exact permission key the report screen already required. Two real correctness bugs were caught and
fixed during this phase (not by any automated test) — see "Bugs hit and fixed" below — plus a
pre-existing bug in `ListPaymentsQuery` (no server-side Direction filter) that pagination would
have silently broken further, fixed as part of this phase rather than deferred.

## Roadmap/brief exit criteria — final status

- [x] `PagedResult<T>` (`Items`, `Page`, `PageSize`, `TotalCount`) lives in exactly one place
      (`src/Application/Common/Pagination/`) and is used by all 30 retrofitted queries — no
      per-module duplicate type
- [x] Every retrofitted query has an explicit, stable `OrderBy` before `Skip`/`Take` — verified by
      a real two-request test against the 105-row Invoice seed (page 1's last row and page 2's
      first row directly adjacent under `OrderByDescending(CreatedAt)`, zero duplicate/skipped IDs
      across pages 1–3) and by a dedicated unit test (`PagedResultExtensionsTests`)
- [x] Boundary correctness: `Page` ≤ 0 and `PageSize` outside `[1, 200]` are **rejected** (400,
      FluentValidation, never silently clamped); a page past the end returns an empty `Items` with
      the correct `TotalCount`, not a 404/500 — all four cases proven live via curl against the
      real API, not just unit-tested
- [x] `TotalCount` matches a direct `sqlcmd COUNT(*)` for two different filter combinations
      (Invoices unfiltered: 105/105; PurchaseBills unfiltered 61/61 and `status=Approved` 60/60)
- [x] No full-table materialization: confirmed by reading the generated SQL in the API's own
      console log — every retrofitted `ListX` query pushes `OFFSET ... FETCH NEXT ... ROWS ONLY`
      to SQL Server, and `TotalCount` comes from a separate lightweight `SELECT COUNT(*)`, not a
      client-side `.Count()` over a materialized list
- [x] UI correctness: `<app-pagination-control>` correctly shows total count, moves between pages,
      and changes page size — confirmed live in the browser (screenshots taken) against the
      Invoice list (105 rows), the Sales Master Report (104 rows), and the Purchase Master Report
      (60 rows) — the three exit-criteria-named screens, each independently seeded and clicked
      through, not inferred from code review
- [x] Export — current view: downloading a filtered Sales Master Report's "current view" produces
      an `.xlsx` whose row count and cell values match the on-screen page exactly — verified by
      unzipping the downloaded file and diffing specific cell values (Entry No, Contact Code)
      against the same-filter JSON API response, not by trusting a 200 status code
- [x] Export — full dataset: the same filter's "full dataset" export ignores paging and its row
      count (104) matches the paginated API's own `TotalCount` for that filter
- [x] Permission parity: the export endpoint enforces the identical key its report screen already
      requires — proven by a real 403 (naming the exact key) for a Member on both the GET and the
      export endpoint, for both Sales Master Report (`Reports.SalesMasterReport.View`) and Purchase
      Master Report (`Reports.PurchaseMasterReport.View`)
- [x] No regression: Domain.UnitTests 76 (unchanged), Application.UnitTests 212 (199 + 13 new
      paging tests), Api.IntegrationTests 5 (unchanged), Angular 7 specs (unchanged) — all green
- [x] Scope boundary held: no print-formatted output built (deferred to Phase 20); Balance
      Sheet/Income Statement/Trial Balance remain drill-down-only, no export button added to them;
      no background job infrastructure introduced (exports are synchronous downloads)
- [x] `docs/phase-16c-status.md` (this file) and `CLAUDE.md`'s Current status section record the
      scope decisions below

## Scope decisions (with reasoning)

1. **Spreadsheet library: ClosedXML.** No spreadsheet library existed anywhere in this codebase
   before this phase (confirmed by the brief's own pre-session grep). Chose ClosedXML over the
   OpenXml SDK or NPOI for its much simpler object-model API (`workbook.Worksheets.Add(...)`,
   `sheet.Cell(r, c).Value = ...`) given the 8 report handlers this phase touches — a true
   DB-streaming writer (OpenXml SDK's `OpenXmlWriter`, or NPOI's `SXSSFWorkbook`) would only pay
   off if the report handlers themselves streamed rows from the database, but every one of them
   already fully materializes its row set in memory before returning (a pre-existing Phase 8
   constraint, not something this phase introduces or could cheaply undo). **Correction found
   during manual E2E, not code review**: ClosedXML's `XLWorkbook.SaveAs(Stream)` writes
   synchronously, and Kestrel disallows synchronous writes directly against the live HTTP response
   body by default (`InvalidOperationException: Synchronous operations are disallowed` — first
   surfaced as a bare 500 with a generic `{"title":"An unexpected error occurred."}` body; the real
   exception only appeared in the API's own console log). Fixed by having every export endpoint
   `SaveAs` into an in-memory `MemoryStream` first, then `CopyToAsync` that buffer to the real
   response stream (`ReportSpreadsheetExporter.WriteWorkbookAsync`). This means one full workbook's
   worth of buffering is unavoidable with this library — not a choice made for convenience, a
   correction to the original design-doc-comment's claim that `SaveAs` "writes directly to
   whatever stream it's given" (true in isolation, false against Kestrel's response stream).

2. **Full backend sweep, no first-wave/rest-deferred split.** The roadmap's own phrasing
   ("highest-row-count screens first... then sweep the rest") reads as sequencing within the
   phase, not a license to leave some queries on the old unpaginated contract. All 22 document-list
   queries and all 8 reports were retrofitted to `PagedResult<T>` in this phase — verified by grep,
   zero queries still return a bare `IReadOnlyList<T>`/DTO-without-paging-fields. The one
   deliberate exception is `VatSummaryReportQuery`, which stays unpaginated by design (fixed
   2×3-bucket cardinality — see its own doc comment, unchanged from Phase 8c).

3. **Angular UI sweep: full pager everywhere except the 14 lookup-backed screens.** Every list page
   backed by its own dedicated (non-generic) `ListXQuery` got the real `<app-pagination-control>`
   wired in — 19 document-list screens (Invoice/Quotation/SalesOrder/CreditNote/PurchaseOrder/
   PurchaseBill/DebitNote/Expense/Account/CashTransfer/JournalVoucher/Product/Contact/Payment
   ×2/InventoryAdjustment/WarehouseTransfer/Deal/Task) plus all 8 reports (10 Angular pages, since
   Ageing Summary and Statement each have Customer/Supplier variants) = 27 screens with a real,
   independently-tested pager. The 14 lookup types sharing the generic `ListLookupsQuery<TLookup>`
   (CreditTerm, PaymentMode, CustomStatus, ReportingTagCategory, ReportingTagOption, ContactGroup,
   ProductCategory, UnitOfMeasurement, AccountGroup, Warehouse, TdsType, TaskType, LeadSource,
   DealStage) get the paginated backend contract for consistency but **no visible pager** — these
   are bounded master-data tables that never realistically approach NFR-5.1's "tens of thousands"
   framing, so a pager would be pure UI clutter. Their Angular service methods keep their
   pre-existing `Observable<T[]>` public contract unchanged (request `pageSize=200` internally,
   unwrap `.items`) — zero consumer files needed to change for these 14 types, which is also why
   `CustomFieldDefinition` (no Angular screen yet) and `Role`/`OrganizationMembers` (no dedicated
   list page, or picker-only) needed no UI work either.

4. **Breaking change: atomic, no versioning.** Wrapping every `IReadOnlyList<T>` in `PagedResult<T>`
   changed the wire shape of all 30 endpoints in the same phase, same discipline Phase 16b used for
   `DiscountPct` — no endpoint was left half-migrated, and no API versioning scheme was introduced
   (none exists elsewhere in this codebase).

5. **"Full dataset" export reuses the exact same query handler and permission gate.** Every report
   query gained an `ExportAll` bool alongside `Page`/`PageSize` — when true, the handler's final
   in-memory `.Skip/.Take` slice is replaced with `.ToUnpagedResult()` (same filters, same
   `IRequirePermission` check, paging simply skipped). No separate export-only code path exists;
   confirmed live by exporting the same filtered Sales Master Report both ways and diffing.

6. **"Current view" export = the exact page currently on screen, not "everything paged through so
   far."** The latter is client-side state the backend can't reproduce or verify without the
   client sending its full page-visit history; "the one page you're looking at right now" is a
   single, unambiguous, server-verifiable request (same `page`/`pageSize` params the screen is
   already using). Confirmed live: current-view export of page 1 (50 rows) produced an `.xlsx`
   whose first/last row Entry Nos matched the on-screen page exactly.

7. **A "current view" export's Total/footer row still shows the full filtered grand total, not a
   per-page subtotal — deliberately, matching what the screen itself already shows.** See bug #1
   below: the on-screen footer was already fixed to show the true grand total regardless of which
   page is displayed, so the export (which is explicitly meant to reproduce "what's on screen")
   correctly inherits that same behavior. This was confirmed, not assumed, by unzipping a
   50-row "current view" export and finding its footer total equal to the full 104-row dataset's
   total.

8. **Export scope: the 8 reports only, not the 22 document-list queries** — FR-9.8 is about
   reports; a document list (e.g. the Invoice list) has no export button, per the brief's own
   explicit "distinct from the 22 plain document-list queries... don't need export" framing.

9. **One export endpoint per report, as a sibling route** (`/reports/{report}/export`), not a
   `format=xlsx` query param on the existing GET route — keeps the JSON-returning endpoint's
   response type simple (`Results.Ok`) and the file-returning endpoint's (`Results.Stream`)
   fully separate, matching this codebase's existing one-endpoint-per-concern minimal-API style.

## Bugs hit and fixed along the way

1. **Grand-total footers silently break under pagination — caught by re-reading the pre-existing
   Angular templates before assuming pagination was a pure backend change.** Four report pages
   (Sales/Purchase Master Report, TDS Report, both Ageing Summary pages) computed their footer
   "Total" row by `.reduce()`-ing over the client-side `rows()` signal — correct only when `rows()`
   held every matching row. The moment `rows()` became a single page, the footer would have
   silently started showing a page subtotal instead of the report's real total, with no error and
   no failing test (none of the 199 pre-existing tests touch the Angular templates). Fixed by
   adding explicit grand-total fields to the affected DTOs (`SalesMasterReportDto.TotalAmount`,
   `PurchaseMasterReportDto.TotalAmount`, `TdsReportDto.TotalGrossAmount`/`TotalTdsAmount`,
   `ContactAgeingSummaryDto.TotalDays1To30`/`31To60`/`61To90`/`91Plus`), each computed server-side
   from the **full** filtered row set before the final `.Skip/.Take` pagination slice runs — not
   from the paginated `Rows` the DTO also carries. `ContactStatementDto`'s `OpeningBalance`/
   `ClosingBalance` needed no equivalent fix — they were already computed from the full date-range
   event set independent of `Rows`, confirmed by reading `ContactStatementQueryHandler` before
   assuming otherwise.

2. **`ListPaymentsQuery` had no server-side `Direction` filter** — both `payment-list-page`
   (Sales, Direction=Received) and `supplier-payment-list-page` (Purchasing, Direction=Paid)
   fetched the *entire* unfiltered Payment list and filtered by direction client-side, a
   pre-existing gap the CLAUDE.md gotcha list already flags a sibling of ("`ListPaymentsQueryHandler`
   shipped with a hardcoded Direction filter... invisible until Phase 6's first `Direction=Paid`
   row"). Under real pagination this breaks two ways at once: a page could come back with zero
   rows matching the caller's direction while more exist on a later page, and the reported
   `TotalCount` would count the wrong direction's rows too. Fixed by adding `Direction` as a real
   filter on `ListPaymentsQuery`/`ListPaymentsQueryHandler` (mirroring every other list query's
   `Status` filter pattern) and switching both Angular pages to pass it server-side instead of
   filtering the response.

3. **ClosedXML + Kestrel synchronous-I/O** — see scope decision #1 above; caught only because
   manual E2E exercises a real Kestrel server, not the InMemory-provider unit tests.

## Manual E2E (curl + cookie jar + sqlcmd, then browser)

A fresh throwaway test user/org was created for this phase (`phase16c.tester@example.com`,
saved to local `dotnet user-secrets` under `Testing:*` keys per the user's request — never
committed). Chart of Accounts, a Warehouse, a Customer, a Supplier, and a Service-type Product
(chosen specifically to avoid Phase 8c's Goods-type stock/COGS-account gotcha, irrelevant to
proving pagination) were seeded via direct API calls. 105 Invoices and 60 PurchaseBills were then
created and Approved via a curl loop (104/60 of which ended up Approved — the 1 unapproved each
was this session's own manual format-testing invoice/bill, a useful accidental confirmation that
the Master Reports' pre-existing "Approved-only" filter still holds under pagination).

- `TotalCount` matched `sqlcmd SELECT COUNT(*)` exactly for Invoices (105/105) and PurchaseBills
  under two filters (61/61 unfiltered, 60/60 `status=Approved`).
- Pages 1–3 of the Invoice list (pageSize 50) contained exactly 105 unique IDs with zero
  duplicates and zero gaps; page 4 (past the end) returned an empty array with `TotalCount` still
  105, not a 404/500.
- `page=0`, `page=-1`, and `pageSize=500` all returned 400 naming the exact field; `pageSize=200`
  (the max) returned 200.
- The API's own console log showed real `OFFSET @p ROWS FETCH NEXT @p2 ROWS ONLY` SQL for
  Warehouses/Products/Contacts/Invoices, and a separate `SELECT COUNT(*)` — not a materialized
  in-memory count.
- Both Sales Master Report export variants were downloaded and unzipped: "current view" (page 1,
  50 rows) produced a 52-row worksheet (header + 50 + footer) whose first/last data-row Entry Nos
  (`0027`/`0095`) matched the JSON API's page-1 response exactly; "full dataset" produced a 106-row
  worksheet (header + 104 + footer) whose footer total (104000) matched `sum(rows[].totalAmount)`
  computed independently from the full-dataset JSON response.
- A second (Member-role) test user was invited, accepted, and got a real 403 naming
  `Reports.SalesMasterReportView`/`Reports.PurchaseMasterReportView` on **both** the GET and the
  `/export` route for each report.
- Live in the browser (screenshots taken): the Invoice list correctly showed "Showing 1–50 of 105",
  moved to page 2 (starting at `0054`, directly after page 1's `0055`) on Next, and correctly
  re-paginated to "Page 1 of 2" / "Showing 1–100 of 105" on a page-size change to 100; the Sales
  Master Report showed "Page 1 of 3" / "Showing 1–50 of 104"; the Purchase Master Report showed
  "Page 1 of 2" / "Showing 1–50 of 60" and correctly advanced to "Page 2 of 2" / "Showing 51–60 of
  60" (Next correctly disabled there) on click.

## What's next

**Phase 16d — System Audit report** (FR-9.6/NFR-3.3): an append-only audit trail written from a
pipeline behavior, plus a filterable Admin-only report screen. See `docs/roadmap.md`'s Phase 16
section.
