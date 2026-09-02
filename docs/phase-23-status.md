# Phase 23 — Nepali localization & parity odds-and-ends

## TL;DR

All five items shipped. NFR-1.1 (BS calendar), NFR-1.2 (lakh/crore grouping), the SalesOrder link
fix, a Home dashboard, and FR-5.8 (export sale on Invoice).

**What is stored versus what is displayed — the one line to remember:** every date in this system is
stored in **AD**, always. Bikram Sambat is a presentation and entry format, converted at exactly one
edge (`web/src/app/shared/formatting/`). No column, no DTO field, no report window and no migration
changed meaning. Money is likewise stored unchanged; only its rendering moved.

**Supported BS range: BS 2000-01-01 … 2092-12-31, i.e. AD 1943-04-14 … 2036-04-13.** Outside it the
conversion returns null — never a guess, never a clamp. Display falls back to the AD date; entry is
refused.

**Both sweeps are complete, and that claim is mechanically checked**, not asserted in prose:
`sweep-guard.spec.ts` reads every template in the app off disk at test time and fails the build on a
new inline `.toFixed(2)` or a new raw `<input type="date">`. 324 money renders across 40 files and
66 date inputs across 42 files were converted; the guard is what stops Phase 24 quietly regressing
either.

Tests: Domain **208** (+6), Application.UnitTests **495** (+18), Api.IntegrationTests **18** (unchanged),
Angular **105** (+58). `dotnet build` / `dotnet test` / `ng build` / `ng test` / `tsc --noEmit` all clean.

**One new permission key**, `Workflow.RecentTransaction.View` (Decision G), and it grants visibility
of nothing on its own -- there is a test for exactly that.

---

## Step 2 — what the live reference product actually does

A live session against the Tigg UAT tenant answered all three questions the kickoff flagged. The user
logged in themselves; no credentials were entered or recorded.

**1. The AD/BS switch is a global per-user setting, not a per-field toggle.** It lives in the user
profile menu as a radio pair labelled "Calender format: AD | BS", and shows **one calendar at a time** —
never both side by side. Flipping it re-renders every date in the product. Both calendars use the same
`DD-MM-YYYY` display order. This shaped the component more than anything else in the phase: a global
signal plus an impure pipe, rather than a dual-rendering widget.

**The BS entry control** is a text box with placeholder `DD-MM-YYYY (BS)` plus a calendar button. Its
popup header shows the BS month and year with the **AD span underneath** (`भदौ २०८३` over
`Aug/Sep 2026`), and every day cell carries the BS day large with the AD day small beside it. This
implementation reproduces that shape in Latin numerals rather than Devanagari, since the app is
English-language throughout — a deliberate, stated deviation.

**Unexpected bonus: the live product validated the conversion table.** Flipping the toggle on a
Journal Voucher grid gave 12 same-row AD/BS pairs, and every one matched what this phase's table
computes — including `01-09-2026 → 16-05-2083` and the Shrawan/Bhadra month boundary. Those pairs are
now assertions in `bs-date.spec.ts`.

**2. Export sale on the Invoice form.** A single checkbox **"This is export sales"** sits in the
header block, below Currency/Warehouse and above the line grid. Ticking it reveals exactly three
fields: **Country**, **Date**, **Document No**. None of the three carries the red required asterisk
that Customer, Invoice Date, Due Date and Warehouse do — so they are **optional even when the flag is
set**, which is where FR-5.8 diverges from `PurchaseBill`'s import block.

**The tax treatment was confirmed in the DOM, not inferred.** With export ticked, the per-line Tax
selector is **disabled** (`ant-select-disabled`) and pinned to **`0 Vat`**. Unticking it and setting a
line to 13% then re-ticking showed the same. The line-tax dropdown offers exactly `No Vat / 0 Vat /
13%`, matching this codebase's `VatRate` enum member-for-member.

**3. The Home dashboard.** Quick Links tray (personalisable, "Edit Links"); a date sub-filter offering
Today / Last 7 / 15 / 30 / 45 / 60 days / This Fiscal Year to Date / Fiscal Year / Custom Range; a KPI
card row; a **Bank and Cash Balance** panel listing every account with a **Total Balance** row; and a
**Transactions** feed with All/Sales/Purchase/Payment/Receipt tabs.

**Two incidental findings worth recording.**

- The fiscal year is a **BS** year starting **Shrawan 1** (the "This Fiscal Year to Date" preset
  resolved to `01-04-2083`), and it appears in document codes as `JV0031/83-84`.
- **The reference product is internally inconsistent about digit grouping.** Its Journal Voucher grid
  groups lakh/crore (`4,00,000.00`), while its Home dashboard balance panel groups Western
  (`-1,378,340.43`, `12,967,060.5`). NFR-1.2 says lakh/crore "wherever displayed", so this app follows
  the PRD rather than copying the inconsistency. Recorded because a future reader comparing the two
  screens will otherwise think one of them is a bug here.

**Still not done, and not this phase's scope:** the browser passes on `Configurations > Import /
Export`, `Organization > Developer Mode` and `Organization > Documents`, carried since 21b/21c/22.

---

## Decision A — what "supports BS" means, and what is stored

**Every date is stored in AD. Always.** BS is presentation and entry only, converted at the client
edge. The alternative — persisting BS — would give every existing `DateOnly` column, every report
window, every `NepalTime` computation and every past migration two possible meanings, and there is no
way to tell which one a given row used. Stated explicitly because the next reader will otherwise go
looking for a BS column.

**Conversion happens client-side**, in `web/src/app/shared/formatting/`. That keeps the API unchanged
and keeps one source of truth. **The cost, stated rather than discovered:** Phase 20d's print/PDF
pipeline and Phase 16c/21b's `.xlsx` exports both render dates **server-side**, so **they remain AD
regardless of the user's setting**. Closing that gap means either a second conversion table in C# or
sending the preference to the server; both are real work and neither is in this phase. It is a known
limitation, not an oversight.

**Instants versus business dates.** The sweep converted **business dates** — `DateOnly` values on the
wire, rendered bare (`item.date`, `row.entryDate`, `dueDate`, `chequeDate`, `occurrenceDate`,
`accountingStartDate`, and FR-5.8's `exportDeclarationDate`): 40 renders across 35 templates.
**Audit timestamps stay AD**: `approvedAt`, `createdAt`, `uploadedAt`, `sentAt`, `linkedAt`,
`extractionAttemptedAt` all render through Angular's own `| date: 'medium'` pipe and carry a
time-of-day. They answer *when did this happen*, not *what date does this document bear*, and
converting them would mean reimplementing datetime formatting as well. A deliberate boundary, not an
omission.

**A calendar is not a time zone.** `Domain/Common/NepalTime` (UTC+05:45) is untouched and unrelated.
Where this phase needed "today" — the picker's highlight, the dashboard's default range — it computes
the Nepal wall-clock day the same way, because between 18:15 and 24:00 UTC the Nepal date is already
tomorrow. BS conversion is bolted onto nothing.

---

## Decision B — where the conversion data comes from

**A table embedded in this repo, cross-checked across four independent implementations.** Not an npm
package (it would be this app's first non-framework runtime dependency, for ~1 KB of data that never
changes), and not a server endpoint (a network round-trip to render a date).

BS month lengths are **not computable** — they vary per year and come from the published Panchanga —
so the table is the risk, and transcribing it from any single source is how a one-day error gets in.
Four sources were decoded and diffed:

| Source | Encoding | Real data through |
|---|---|---|
| `bikram-sambat` (medic) | 2-bit-packed month lengths | BS 2083 |
| `nepali-date-converter` (subeshb1) | named-month object map | BS 2086 |
| `nepali-datetime` (opensource-nepal) | `[lengths, yearTotal]` pairs | BS 2099 |
| `nepali_utils` (sarbagyastha, Dart) | `[yearTotal, ...lengths]` rows | BS 2250 |

**All four agree on every year of BS 2000–2083.** The first two then emit filler rows whose last three
months are always `30/30/30` — the giveaway that their real data has run out — while the two carrying
genuine data agree with each other through **BS 2092** and first diverge at BS 2093.

**The supported range is the unanimous one: BS 2000-01-01 … 2092-12-31 ≡ AD 1943-04-14 … 2036-04-13.**
Taking the four-way agreement literally would have capped it at BS 2083 — about seven months of
forward headroom from today, which is not a shippable range for an accounting system. Distinguishing
"disagreement" from "this source stopped having data" is what bought ten years instead.

**At the edges, the functions return `null`.** They never extrapolate and never clamp; a
plausible-looking wrong date is the single outcome the module exists to prevent. `NepaliDatePipe`
falls back to rendering the AD date (visibly not-BS); `BsDateInput` refuses the entry with a message.
`bs-date.spec.ts` pins both boundary dates, both one-day-outside cases, and the two range constants,
so widening the range stays a deliberate act.

**Extending it:** append BS 2093+ once two independent sources agree, and bump `LAST_BS_YEAR`.

---

## Decision C — where the per-user AD/BS preference lives

**Browser `localStorage`**, behind `DatePreferenceService`. Nothing in this codebase stored a per-user
preference before now: `Domain/Identity/User` has no settings at all, and `TenantSettings` is
per-*organization*, so it is the wrong home for a choice one user makes for themselves.

The alternatives and their real costs: a column on `User` (a migration, an endpoint, and a pure
display concern pushed into the Identity aggregate); a general `UserPreference` entity (a table, a
configuration, a command, a query and permission plumbing, for one boolean).

**What this explicitly does not support**, so nobody has to discover it:

- The preference **does not follow the user across devices or browsers**. NFR-1.1 asks for "switchable
  per user preference" and does not ask for synchronisation. `DatePreferenceService` is the single
  seam to move behind an endpoint if that changes.
- **Server-rendered output cannot read it** — see Decision A's note on PDFs and `.xlsx`.

The value is a signal, so flipping it re-renders every date immediately with no reload and no
per-page subscription.

---

## Decision D — how a 324-call and a 66-input sweep get done without leaving the app half-converted

**Both sweeps are complete, not partial.** A partially-swept app is worse than an unstarted one: a
user cannot tell which dates are BS and which are AD, and a mis-read date in an accounting system is a
filed tax return with wrong numbers in it. So there is no "which screens are done" rule to explain —
the answer is all of them.

**How a reader verifies that claim, mechanically:** `web/src/app/shared/formatting/sweep-guard.spec.ts`
globs every `.html` under `src/app` at test time and fails on any inline `.toFixed(2)` or any raw
`<input type="date">`. It carries a named allow-list — currently one entry, `bs-date-input.html`,
which *is* the replacement and legitimately wraps the last native date input — where each exemption
must state its reason. Three details make it a real guard rather than a decorative one:

- it asserts the glob matched a plausible number of templates first, so a broken glob cannot make
  every other assertion pass vacuously;
- it checks each allow-list entry still points at a file that exists, so a rename cannot silently
  turn an exemption into a hole;
- it prints the offending paths in the failure message.

**The sweeps themselves were script-driven and then reviewed**, not hand-edited 390 times. The money
transform scans backwards from each `.toFixed(2)` with bracket balancing and wraps the result as
`(EXPR | amount)` — parenthesised, which is what makes it safe inside a ternary, since Angular's
parser calls `parsePipe()` for a parenthesised primary but not for a bare ternary branch. The date
transform recognised three binding shapes and refused to convert anything it could not parse
confidently, reporting them instead; the reported cases were then handled explicitly rather than
force-fitted.

**The rounding contract changed slightly, knowingly.** `.toFixed(2)` nominally rounds half away from
zero but is subject to binary-float artifacts: `(1.005).toFixed(2)` is `"1.00"`. `AmountPipe` uses
`Intl.NumberFormat`, which gives `"1.01"`. That is strictly more correct and is pinned by a test so it
is not later rediscovered as a bug. `Intl` is native ECMA-402, so no `registerLocaleData` and no
locale bundle was needed — unlike Angular's own `DecimalPipe`.

**One non-obvious implementation note.** `NepaliDatePipe` is **impure**. A pure pipe caches on its
argument, and the ISO string does not change when the user flips the calendar — so a pure pipe would
keep serving the AD rendering forever while the rest of the app switched. That is the same shape as
CLAUDE.md's zoneless-`computed()`-over-`FormControl` gotcha: a value with no tracked dependency,
silently stale, with nothing in `tsc` or `ng build` to catch it. Impure means `transform` runs every
change-detection pass, so the conversions are memoized in a module-level map.

---

## Decision E — FR-5.8's field shape and tax treatment

**Fields on `Invoice`**, mirroring `PurchaseBill`'s existing import block: `IsExport` plus nullable
`ExportCountry` / `ExportDeclarationNo` / `ExportDeclarationDate`. They map one-for-one onto the four
columns `SalesRegisterQuery` has carried since Phase 19 and which `SalesRegisterQueryHandler` passed
as `0`/`null` in both branches because Invoice had no flag. Same column lengths as the import block,
so the two read alike. Phase 21c's `MigratedSalesRegisterEntry` already had these columns, so a tenant
could *import* a pre-cutover export sale but not *record* a new one; that asymmetry is now closed.

**Required-when-flagged: no.** This is the one place FR-5.8 deliberately differs from the import
block, and it is live-confirmed (Step 2, finding 2) rather than assumed by analogy.

**The tax treatment is the point of the requirement, and it lives in the aggregate.** An export sale
is zero-rated, so `Invoice.AddLine` coerces every line's `VatRate` to `ZeroVat` while `IsExport` is
set, and `SetExport(true, …)` **re-rates lines already entered**. Both orderings matter: a user who
adds 13% lines and *then* ticks the box must not be able to bank VAT on an export sale. Putting it in
the Domain rather than a validator or the Angular form means no entry path — API, import, conversion —
can bypass it.

**`ZeroVat`, not `NoVat`.** Both compute zero VAT, so an implementation reaching for `NoVat` would
pass every total assertion while filing the sale under the wrong statutory heading. There is a test
whose only job is that distinction.

**The blast radius was traced, not assumed.** Because the lines are `ZeroVat`, the Sales Register's
existing "`VatAmount == 0` ⇒ tax-exempt" split already keeps an export sale out of Taxable Sales, and
the VAT Summary's per-rate bucketing already files it under Zero-rated. Neither report needed new
logic — but both halves are asserted, per the testing bar, because Phase 6's bug #3 is the standing
reminder that a report change can look balanced and still put a number in the wrong column. **Annex 5
was checked and does not consume export data** (its only "Export" hit is the `ExportAll` pagination
flag), so the blast radius really is Invoice + Sales Register + VAT Summary.

**A CreditNote against an export invoice carries no export block** — the aggregate has no flag and the
live product offers none — so those columns stay empty on CreditNote rows rather than being derived
from the invoice being reversed.

---

## Decision F -- how far the Home dashboard goes

**The starting rule: every figure comes from a query handler that already existed.** A dashboard is
the classic place to accidentally write five new report queries, so the KPI cards were held to
existing, date-ranged, server-totalled queries and nothing was added to the Application layer for
them:

| Card | Existing query |
|---|---|
| Sales | `SalesRegisterQuery.TotalValue` |
| Purchase | `PurchaseRegisterQuery`'s own totals |
| Receipt | `CashFlowSummaryQuery.ReceivedFromCustomerBalance` |
| Payment | `CashFlowSummaryQuery.PaidToSupplierBalance` |

plus the **Bank and Cash Balance** panel with its Total row (`ListBankAccountsQuery`) and the date
sub-filter with Today / Last 7 / 15 / 30 day presets.

**% change vs prior period** runs the same queries again over the window of equal length immediately
preceding the selected one. With no prior data it renders "No prior-period data" rather than a
number, because a change from zero is not a percentage.

**The recent-activity feed was initially left out under that rule, and then built anyway -- on
purpose.** The reason first given for omitting it does not survive scrutiny: phase-16c's bug #1 is
about a *footer total* silently becoming a page subtotal, and a feed has no footer, so that hazard
never applied. What genuinely applied was narrower -- no existing query returns a mixed
recent-transaction stream, so the feed needs a new aggregation. That is a cost, not a prohibition,
and the module scan lists the feed as the Home Tab's third section. `RecentTransactionsQuery` is
therefore **the one new Application-layer query Phase 23 wrote**, and it is recorded here as a
deliberate override of this decision's own rule rather than as drift.

**Why it could not be composed client-side.** The obvious cheap alternative -- call five existing
per-type list endpoints and merge in the browser -- cannot page a merged stream. Page 2 of a
date-ordered mix of Invoices and Purchase Bills is not derivable from page 2 of each type
separately. Server-side merge, order and page is the only correct shape, and it is verified live:
with `pageSize=2` over three documents, page 1 returns the two most recent (both Invoices) and page 2
returns the older Purchase Bill.

**What the feed covers, and why that is the scope.** The live tab list *is* the scope: Sales =
Invoice + CreditNote, Purchase = PurchaseBill + DebitNote + Expense, and Payment/Receipt are the two
Directions of the one Payment aggregate. JournalVoucher, CashTransfer, WarehouseTransfer and
InventoryAdjustment are absent because the live product offers no tab for them; Quotation, SalesOrder
and PurchaseOrder are absent because nothing has happened financially until they convert.
**Approved only** -- Drafts belong to the Transaction Approval queue, which is its own screen, and
Void documents have been reversed, so listing them as recent activity would misstate what happened
(the same Approved-only rule every register report follows, FR-9.10).

**Two implementation notes worth keeping.** The handler resolves line sums and contact names for
**the returned page only**, not for every document in the range -- the ordering pass needs just
Id/Date/CreatedAt, so a wide date range does not drag every line in the tenant through memory.
And each document type gets its own concrete `Where` block rather than one generic
`Func`-parameterised helper, for the reason CLAUDE.md and phase-9's bug #1 both record: a captured
delegate inside `.Where()` compiles and then fails to translate against real SQL Server.

**Still not built:** the personalisable Quick Links tray. The existing `organization-dashboard-page`
already *is* a 55-link nav tray, and per-user link storage is a backend feature this phase has no
mandate for -- Decision C having just declined to build per-user server storage for a single boolean.

**The Total Balance row is suppressed, not approximated, when not every account was loaded.** The
panel requests the 200-row maximum; if `totalCount` exceeds what came back it hides the total and
says why. A footer total must cover the whole filtered set or not exist. (This is where phase-16c's
bug #1 genuinely does apply.)

**The new page is a sibling of the launcher, not a replacement** -- `/organizations/:id/home`, linked
from the launcher's header. The launcher is load-bearing navigation for every module.

## Decision G -- permission keys

**One new key: `Workflow.RecentTransaction.View`, Admin+Member.** Everything else in the phase needed
none, and the derivation is per feature as CLAUDE.md requires:

- **Formatting and the date widget are not permissioned.** A pipe and an input component expose no
  data; they re-render what the user could already see.
- **FR-5.8's fields ride `Invoice`'s existing keys.** They are columns on an aggregate whose
  Create/Edit/View keys already gate every path that touches them. A separate key would imply an
  export sale is a different kind of document, which it is not.
- **The KPI cards and the balance panel have no key of their own; each rides the key of the query
  behind it** -- `SalesRegisterView`, `PurchaseRegisterView`, `CashFlowSummaryView`,
  `BankAccountView`. Each read is issued independently and a failure dims that one card ("No access")
  instead of emptying the screen, so **a Member with few grants sees a smaller dashboard, not a
  broken one**. The alternative -- one `Dashboard.View` key -- would have let a user open a screen
  every card on which they were forbidden to populate.

**The feed is the one place that needed a key, and its shape copies `TransactionApprovalQuery`'s
exactly.** The key is *blanket* in the same sense that one is: its primary job is that
`AuthorizationBehavior` -- the only mechanism in this codebase that verifies the acting user actually
belongs to `OrganizationId` -- runs for the request at all. It is deliberately **not** the gate on
what the feed shows.

**The real gating is per document type, inside the handler**, against that type's own `*.View` grant:
Invoice/CreditNote for Sales, PurchaseBill/DebitNote/Expense for Purchase, Payment for both Payment
and Receipt. The consequences are asserted rather than merely described:

- a user holding `PurchaseBillView` but not `InvoiceView` gets a feed of Purchase Bills -- **not a
  403, and not a leak**;
- **holding the blanket key and nothing else shows an empty feed**, which is the test that makes the
  key safe to grant Member by default;
- a member of a *different* organization sees nothing, which is the org-scoping the blanket key
  exists to enforce.

Member is granted because a recent-activity list is routine daily-use working data and every row is
already Member-visible under its own key. The flat-register argument for Admin-only (one screen
exposing every party's PAN at once) does not transfer: this feed carries no PAN and is a rolling
window rather than a register over tenant history.

The key was added to `RolePermissionConfiguration.HasData` **before** scaffolding, per CLAUDE.md's
rule; migration `SeedRecentTransactionViewPermission` is two `InsertData` rows and nothing else.

## What was built

**Shared formatting module** — `web/src/app/shared/formatting/`, the app's first shared pipes:

- `bs-date.ts` — the conversion table and functions. The phase's risk, isolated from any widget.
- `amount-pipe.ts` — `| amount`, lakh/crore grouping at 2 dp (NFR-1.2).
- `nepali-date-pipe.ts` — `| nepaliDate`, `DD-MM-YYYY` in the active calendar (NFR-1.1, display).
- `bs-date-input.ts/.html/.scss` — `<app-bs-date-input>`, the single replacement for all 66 native
  date inputs. Works as both a `[value]`/`(valueChange)` component and a `ControlValueAccessor`, since
  60 sites are signal-based and 6 are Reactive Forms. Renders a **real native date input in AD mode**
  (the browser's picker, keyboard and mobile handling are better than a reimplementation) and the
  BS text box plus popup grid in BS mode.
- `date-preference.ts` — the global AD/BS signal (Decision C).
- `calendar-toggle.ts` — the AD/BS control, in the launcher and dashboard headers.
- `sweep-guard.spec.ts` — Decision D's mechanical completeness check.

**FR-5.8** — `Invoice` gains the export block and the zero-rating invariant; EF configuration;
migration `AddInvoiceExportSaleFields` (four pure `AddColumn`s, `IsExport` defaulting false, read
before applying and needing no reordering); Create/Update commands, `InvoiceDetailDto`, the API
request record, the Angular model, and the invoice form's checkbox-plus-three-fields block with the
per-line Tax selector disabled while export is set.

**SalesOrder links** — both `detailRoute` switches now resolve SalesOrder to the page Phase 18 built,
and both stale comments are replaced with the actual history.

**Home dashboard** — `features/organizations/home-dashboard-page/`, routed at
`/organizations/:id/home`.

---

## Testing

Beyond the standard gate, three things are worth calling out.

**The conversion table is what is tested, not the widget.** `bs-date.spec.ts` asserts 12 AD/BS pairs
read off the live reference product; all 14 published Nepali New Year dates for BS 2070–2083
(including the four that fall on April 13 rather than 14, which is exactly where an off-by-one
surfaces); five dates in months whose length differs from their neighbouring years; both directions;
both range boundaries; four out-of-range cases; and **an exhaustive round trip over all 33,969 days of
the supported range**. Exhaustive rather than sampled, because a table typo affects one month of one
year and sampling is how such a typo survives a suite.

**The exit criterion is literally a test.** `bs-date-input.spec.ts` enters a date in BS, asserts the
committed value is the AD ISO string, flips the app to AD, asserts the same value renders as the AD
date, flips back, and asserts no drift — through both binding shapes.

**Lakh/crore is asserted on values that expose it.** A number below 100,000 formats identically under
both conventions, so every grouping assertion is at or above one lakh, and there is a companion
assertion that the output is *not* the Western form.

**Manual E2E** against a fresh Organization on real SQL Server, seeded via curl + cookie jar. The
feed was exercised live across its tabs -- All returned three rows most-recent-first (two Invoices
and a Purchase Bill at its gross 339,000.00), Sales returned the two Invoices, Purchase the one bill,
and `pageSize=2` split the merged stream correctly across two pages. An
invoice was posted through the real API asking for `ThirteenPercentVat` with `isExport: true` and came
back — verified with `sqlcmd` against `sales.Invoices`/`sales.InvoiceLines` — stored as `ZeroVat` with
`VatAmount = 0`, alongside `IsExport=1, India, EXP-2083-001, 2026-09-01`. The Sales Register showed
`Export Value 10,00,000.00` with `Taxable 0.00`, the control (domestic) invoice showed
`Taxable 5,00,000.00 / VAT 65,000.00` with empty export columns, the VAT Summary filed the export sale
under **ZeroVat** with `TotalOutputVat` unaffected and the exempt bucket empty, and the Trial Balance
balanced at `15,65,000.00`.

**The feed's tests are about permissions, not layout.** `RecentTransactionsQueryHandlerTests` covers
the three cases that make a blanket key safe (partial grant yields a partial feed; the blanket key
alone yields nothing; another organization's member yields nothing) alongside ordering, the date
window, Draft exclusion, each tab, and paging over the merged stream.

**What no unit test here can prove:** how a date *looks*. That is why the browser pass was budgeted
rather than treated as a formality — and it earned its keep again (below).

---

## Bugs and findings

**1. The live Sales Register page never rendered the four export columns.** Found in the browser pass.
The DTO has carried `ExportValue`/`ExportCountry`/`ExportDeclarationNo`/`ExportDeclarationDate` since
Phase 19, the handler now fills them, the API returns them, every automated check was green — and the
Angular table simply had no `<th>`/`<td>` for them, because they were always empty when the page was
written. The migrated-register page *does* render them, which is what made the omission visible by
comparison. FR-5.8 would have shipped with its data invisible to the user. Fixed; the footer gained a
`colspan` so the totals row still spans the table, and the export columns have no footer total because
the DTO computes none server-side (rather than summing the current page — phase-16c bug #1 again).

**2. `AmountPipe` rendered a tiny negative as `-0.00`.** Caught by its own spec. Normalising on the
formatted string rather than pre-rounding the input was the fix, since pre-rounding would have quietly
changed the documented half-away-from-zero contract.

**3. Two assertions in `bs-date.spec.ts` were wrong about the table**, not the code — I had claimed
Poush 2082 has 29 days when the table says 30. Corrected to a genuine adjacent-year pair (Poush is 30
in BS 2083 and 29 in BS 2084). Worth recording as a reminder that the table is the authority and
memory is not.

**4. A trailing comma in a multi-line `imports:` array** produced `SendSmsForm,, AmountPipe` in one
component during the scripted sweep — caught by `ng build`, fixed, and a reminder that a scripted
edit needs its build run before its diff is trusted.

**Pre-existing, not introduced:** `ng build` warns that the initial bundle exceeds its 500 kB budget.
Verified against a stashed clean tree — the baseline is 605.64 kB and this phase's is 605.65 kB.

---

## New gotchas for CLAUDE.md

- An **impure** pipe is required for anything that renders from a global signal but takes an unchanging
  argument; a pure pipe caches past the signal change with nothing to catch it.
- Angular's parser accepts a pipe inside parentheses (`cond ? (a | pipe) : b`) but not bare in a
  ternary branch — which is what makes a scripted `EXPR.toFixed(2)` → `(EXPR | amount)` sweep safe.
- A component test asserting an uppercase label fails when the uppercasing is CSS `text-uppercase`;
  `textContent` carries the source casing.
- A report page can gain real data in its DTO and still show the user nothing, because the columns
  were never rendered while the data was always empty.
