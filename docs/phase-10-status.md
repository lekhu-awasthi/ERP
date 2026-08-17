# Phase 10 status — Contact Overview

**Status: COMPLETE.** `ContactOverviewQuery` (`Application.Contacts.Queries.ContactOverview`) backs the
Contact detail page's Overview tab financial summary — Opening Balance, Closing Balance with a DR/CR
suffix, a bounded Recent Transactions feed, and a "View Full Statement" link into Phase 9's Statement
report — the follow-up Phase 9 deliberately deferred (see `docs/phase-9-status.md`'s scope decision
#13) rather than scope-creeping that phase past its four named report screens. No new commands,
aggregates, or schema tables — not even a permission-seed migration, since this phase reuses Phase 3's
existing `Contacts.Contact.View` permission key rather than minting a new one (see scope decision #2
below).

Shape was already confirmed live going in — `erp-module-scan.md` line 91: "Contact detail tabs:
Overview (Opening Balance, DR/CR, Closing Balance, Recent Transactions, 'View Full Statement')" — so
no `AskUserQuestion` was needed on what to build; the open calls were the sizing/wiring/permission
decisions below, each made and documented rather than defaulted to a precedent silently.

Confirmed by hand end-to-end against the real API/DB/browser: a fresh Admin set up a Chart of Accounts
(AR/VAT Payable/Sales Revenue), a Warehouse, a Service Product, and a Customer (PAN, Opening Balance
1,000), then approved a 500 Invoice dated 5 days before "today". `GET /contacts/{id}/overview` and the
Contact detail page's new Account Summary card both showed Opening Balance 1,000.00 DR, Closing
Balance 1,500.00 DR, and the one Invoice as a Recent Transaction — clicking "View Full Statement"
landed on the existing Customer Statement page with the Customer pre-selected via a `contactId` query
param and the page's own already-existing first-of-month-to-today date default applied, showing the
identical 1,500.00 DR closing figure computed by the same shared code path. A second user invited as
Member hit the same Contact detail page and saw the identical Account Summary card (no 403 — confirming
the `Contacts.Contact.View` permission choice), then clicked "View Full Statement" and got the real
API's `403` naming `Reports.CustomerStatement.View`, rendered in the Statement page's own existing error
banner — proving Overview's bounded summary and Statement's full ledger are gated at genuinely
different levels, on purpose.

## Roadmap/brief exit criteria — final status

- [x] `ContactOverviewQuery(OrganizationId, ContactId)` under `Application.Contacts.Queries.ContactOverview`
      — one handler answers Customer/Supplier/Lead alike (Lead naturally produces an empty ledger, no
      special-case branch needed — see scope decision #4)
- [x] Thin read reusing Phase 9's running-balance engine — event loading and the signed-delta
      computation extracted into `ContactLedgerReader` (used by both `ContactStatementQueryHandler`
      and the new `ContactOverviewQueryHandler`), not duplicated (scope decision #1)
- [x] Recent Transactions window: last 10 transactions by date, a fixed count not a date range (scope
      decision #3)
- [x] Permission key: reuses `Contacts.Contact.View` (Phase 3, Admin+Member) rather than a new
      `Reports.*.View` key (scope decision #2)
- [x] Placement: `Application.Contacts.Queries.ContactOverview`, matching architecture-spec.md §4.2's
      own naming right next to `ContactStatementQuery` (scope decision #5)
- [x] "View Full Statement" wired as a real router link into Phase 9's `customer-statement`/
      `supplier-statement` pages, pre-filled with this Contact via a `contactId` query param; date
      range left to the Statement page's own existing first-of-month-to-today default rather than a
      new convention (scope decision #6)
- [x] Closing Balance inherits Statement's standalone-reversal-included behavior, not re-derived
      differently (scope decision #7)
- [x] Extend the existing Overview tab in place — no new tab, no new page
- [x] Unit tests (`ContactOverviewQueryHandlerTests`, 6): Closing Balance matches what a Statement
      query would independently compute for the same Customer, Supplier polarity net of TDS, the
      10-transaction cap with most-recent-first ordering while all events still fold into Closing
      Balance, a Contact with zero activity (Opening Balance only, empty Recent Transactions), a Lead
      with no financial activity, and not-found for an unknown Contact
- [x] `dotnet build`/`dotnet test` (Domain.UnitTests 67 unchanged; Application.UnitTests 140 — 6 new +
      134 pre-existing, all green; Api.IntegrationTests 4, run with Docker Desktop running this
      session — all green) and `ng build`/`ng test` (7 pre-existing specs green, no new Angular specs)
      all pass
- [x] Manual E2E against the real API/DB/browser (see summary above), including the permission-gate
      contrast confirmed both via direct API call and through each page's own UI

## Scope decisions

1. **`ContactOverviewQueryHandler` calls into `ContactLedgerReader`, a shared static helper extracted
   from `ContactStatementQueryHandler`, rather than duplicating the event-loading logic.** The brief
   asked to make this call explicitly rather than default either way. `ContactStatementQueryHandler`'s
   own event-loading block (five `LoadCustomerEventsAsync`/`LoadSupplierEventsAsync` concrete-Where
   queries, ~90 lines) is exactly what Overview needs too — not a handful of lines worth duplicating,
   and Overview's own correctness bar is "Closing Balance must match what Statement would independently
   compute for the same inputs" (see scope decision #7), which a duplicated-and-possibly-drifted copy
   couldn't guarantee as cheaply as a shared call. Extracted as static functions over an explicit
   parameter list (not a DI service) since there's no state to hold — the smallest thing that satisfies
   two callers. `ContactStatementQueryHandler` itself shrank in the process (no behavior change, its own
   3-test suite from Phase 9 still passes unmodified against the refactored handler).
2. **`ContactOverviewQuery.PermissionKey` reuses `Contacts.Contact.View`, not a new `Reports.*.View`
   key.** The brief required weighing this explicitly against Phase 9's Admin-only precedent rather than
   defaulting to either. Phase 9's Admin-only case for Statement/Ageing rested on two factors: (1) a
   full, unbounded per-transaction ledger for one Contact — every Rate/amount they were ever billed or
   paid; (2) a cross-Contact list surfacing every Contact's identity/PAN next to a balance. Overview has
   neither: it's capped at 10 recent rows, for the exact one Contact a Member already has `ContactView`
   access to on this same page (including that Contact's PAN, already visible in the plain Overview
   form Phase 3 shipped), not a list of *other* Contacts' identities. Gating a summary widget embedded
   in a page Member already opens daily behind a brand-new Admin-only key would block routine viewing
   with no matching increase in exposure — so no new permission key was minted at all, and no
   permission-seed migration was needed this phase (the first Phase 8+ report-style phase not to need
   one).
3. **Recent Transactions is capped at a fixed count (10), not a date window.** No count was confirmed
   live — `erp-module-scan.md` only names the widget, not a row count or day range. A fixed count keeps
   the widget's size predictable regardless of how active or dormant a given Contact is: a slow-moving
   Contact wouldn't render an empty widget under a "last 30 days" rule, and a very active one wouldn't
   flood the page under a wide window. 10 mirrors a typical "recent activity" feed size and fits a
   detail-page sidebar without its own pagination.
4. **`ContactOverviewQuery` takes `ContactId` alone and looks the Contact's own `Type` up itself, rather
   than a route-hardcoded `ContactType` the way Statement/Ageing do.** Statement/Ageing sit behind two
   separate routes (`/reports/customer-statement`, `/reports/supplier-statement`) specifically to keep a
   bad/Lead `ContactType` value impossible without a validator (Phase 9's own reasoning, mirroring
   `CreatePaymentCommand`'s `Direction`). Overview instead lives on the single Contact detail page that
   already handles all three Types (Customer/Supplier/Lead) on one route — there is no natural second
   route to hardcode a type onto. A Lead passed through `ContactLedgerReader.LoadEventsAsync` falls into
   the Supplier-branch loader (the ternary's `else`), which is safe *only* because no document type in
   this codebase can ever target a Lead-typed Contact — enforced by `SalesValidation`/
   `PurchasingValidation` at Create time, verified by reading those validators, not assumed — so the
   branch always resolves to an empty event list for a Lead. Only the DR/CR polarity needed one explicit
   fallback in the handler (Customer-style, arbitrary but harmless since no real AR/AP subledger exists
   for a Lead) rather than relying on `ContactLedgerReader.BalanceType`'s own Customer/Supplier-only
   ternary.
5. **Lives under `Application.Contacts.Queries.ContactOverview`**, matching architecture-spec.md §4.2's
   own placement (it names `ContactOverviewQuery` right next to `ContactStatementQuery` under Contacts,
   the one bounded context in the whole Phase 8+ sequence where the spec dictates placement directly —
   see phase-9-status.md's scope decision #2 for the same reasoning applied to Statement/Ageing).
6. **"View Full Statement" passes only a `contactId` query param, not `fromDate`/`toDate`.** The brief
   asked for a documented "sensible default" date range rather than a new convention. Both
   `customer-statement-page.ts`/`supplier-statement-page.ts` already default `fromDate`/`toDate` to
   first-of-month-and-today unconditionally on load (Phase 9's own established default) — reusing that
   rather than re-encoding the same default as URL params keeps the two pages' "no date supplied" and
   "linked from Overview" paths behave identically, and avoids a second place that could drift from the
   first if the default ever changes. Both Statement pages were extended to read an optional `contactId`
   from `route.snapshot.queryParamMap` at construction time and auto-`load()` when present — a plain,
   always-fresh route navigation (not the create/edit-same-route case CLAUDE.md's route-reuse gotcha
   describes), so a one-time `snapshot` read is safe here, unlike `contact-detail-page.ts`'s own
   `:contactId` param which does need the live `paramMap` subscription for exactly the reason that
   gotcha documents.
7. **Closing Balance is computed by literally reusing `ContactLedgerReader`'s signed-delta sum — the
   same formula and the same event set `ContactStatementQueryHandler` uses for its own running
   balance — not re-derived independently.** This means Overview inherits Statement's own
   standalone-reversal-included behavior (an unlinked CreditNote/DebitNote counts toward both, unlike
   Ageing — phase-9-status.md's scope decision #9) automatically, by construction, rather than by a
   second implementation that happens to agree. `ContactOverviewQueryHandlerTests`' first test asserts
   this equivalence directly: an Invoice seeded exactly as `ContactStatementQueryHandlerTests` would
   produces the identical 1500/DR the Statement suite already established as correct for the same
   inputs.
8. **`AsOfDate` is hardcoded to `DateOnly.FromDateTime(DateTime.UtcNow)` server-side — no date param on
   the query at all.** The live Overview tab has no date-range picker (unlike Statement's From/To
   pickers or Ageing's As-Of picker) — it's a live "as of now" snapshot. This mirrors the same
   "as of now" pattern every `GetXConversionTemplateQuery` handler already uses for its own default
   `Date`, not a new convention.
9. **No per-row running Balance column in `ContactOverviewTransactionDto`, unlike
   `ContactStatementRowDto`.** This is a bounded recent-activity feed, not a ledger — the running
   balance per transaction is exactly what "View Full Statement" is for. Keeping the DTO smaller here
   also avoids a second place that could imply (incorrectly) that these 10 rows are a complete history.

## Bugs hit and fixed along the way

None. This was a genuinely clean "thin read over an already-proven engine" phase — the only design risk
(Closing Balance silently drifting from Statement's own number) was closed by construction via shared
code, not by a bug found and fixed after the fact, and confirmed directly in
`Handle_computes_closing_balance_matching_what_a_statement_query_would_return_for_the_same_customer`.

## What's next

`erp-module-scan.md` line 91 also names the rest of the Contact detail page's tab list — Contact
Personnel, Tasks, Deals, Documents, Activity (Comments/Activities/SMS History/Email Logs) — all
`Workflow`/`CRM` bounded-context features explicitly deferred since Phase 3 (see
`docs/phase-3-status.md`), still out of scope. Beyond that, `roadmap.md`'s Phase 8+ section should be
consulted for what's next in the broader Reports/statutory sequence.
