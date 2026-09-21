# Phase 55 — bank statement import

## TL;DR

**The phase's first act was to falsify its own premise.** The kickoff and the roadmap both named
`/config/import-statement` as the reference product's bank statement importer, on the strength of
phase 53's glance at it (`POST ENTRIES` over a `Total rows / Valid / Errors` counter). It is not.
It is a **generic CSV staging editor** whose three hardcoded column sets are Delivery Note, Goods
Received Note and Inventory Adjustment — "statement" there means *statement of rows* — and it is
reached as `/config/import-statement?id=<importId>&collection=<type>` from a "Recent imports"
drawer. The Delivery Note filter row phase 53 recorded as "stale state leaking in from a sibling
screen" is neither stale nor a leak: it is the component's literal default column constant when no
`collection` is supplied, and it renders identically on both tenants.

**The real feature is `/accounting/bank-accounts/:id/import`,** and this pass read all of it —
including driving twelve probe files through its live validate endpoint. See "The live read" below.

**Six decisions, all argued.** **A — it is an ordinary importer**, and the kickoff's reason for
doubting that does not survive phase 21c. **B — the amount is one signed `StatementAmount`**, not
the vendor's `dr_amount`/`cr_amount` pair. **C — Status is derived and has no column**, and phase 56
adds the foreign key it derives from. **D — deletion is by row id, plus an "undo this import"** the
reference product does not offer. **E — two new Admin+Member permission keys**, derived from what
the vendor gates the screen on. **F — one template, not the vendor's two.**

**The one thing that is genuinely new in the machinery** is that a bank statement row does not name
its own account, so the account is context for the **run** rather than data on the row. It lives on
`ImportJob.BankAccountId`, required for this type and refused for the other nine, asserted in both
directions and confirmed across the whole database (22 jobs of the other nine types, none naming an
account).

**Two defects in the reference product, found by probing and deliberately not reproduced**: its
parser matches columns by **position** and ignores the header text entirely (a file headed
"Txn Date" imports fine; a reordered file fails every row), and it accepts a **negative Deposit**
verbatim as `dr_amount: -40`, producing a line whose displayed direction contradicts its sign.

**Two kickoff claims corrected besides the premise**: a bank account is **not** location-scoped here
(`Account` carries no `LocationId`, so phase 32b has nothing to bind to), and **phase 56 is
unblocked** — its start condition was wrong.

**The sweep guard found a real hole.** `SortSweepGuardTests` built a `default(StatementAmount)` by
reflection, and the failure surfaced on the way back *out* of the database, in the EF value
converter — i.e. as a list that would not load rather than as a bad write. C# hands every caller
`default(T)` however private the constructor is, so the aggregate now refuses one.

Tests: Domain **722 (+19)**, Application.UnitTests **1320 (+34)**, Infrastructure.UnitTests 12,
Angular **605 (+14)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean; `ng build`
does not warn and the bundle is **643.85 kB** against the 680 kB budget. One migration, purely
additive: one nullable AddColumn, one CreateTable, two convention CreateIndexes, four seed rows.

---

## Step 1 — the live read, and the premise it destroyed

Phase 53 opened `/config/import-statement` and recorded three things: a `POST ENTRIES` control, a
`Total rows / Valid / Errors` counter, and "a stale Delivery Note column filter row leaking in from
a sibling screen, noted as a vendor defect". The kickoff built this phase's framing on all three.

Driving the screen settled each differently:

| Phase 53 recorded | Actually |
| --- | --- |
| The bank statement importer | A **generic CSV staging editor**. Its column sets are constants in `14.*.chunk.js`: Delivery Note, Goods Received Note, Inventory Adjustment. |
| A stale Delivery Note filter row, a vendor defect | The **default** column set. The route takes `?id=&collection=`, and with no `collection` it renders the first entry. Identical on both tenants. |
| `Total rows / Valid / Errors` as a counter | An **ant radio group** — the three counts are a *filter* over the staged rows, alongside a free-text search. |

The registry, verbatim from the bundle:

```js
{ DeliveryNote:        { name: "Delivery Note",       mappingRoute: "/sales/delivery-notes/import-mapping" },
  GoodsReceivedNote:   { name: "Goods Received Note", mappingRoute: "/purchases/goods-received-notes/import-mapping" },
  WarehouseAdjustment: { name: "Inventory Adjustment", mappingRoute: "/inventory/inventory-adjustment/import-mapping" } }
```

Two of those three are the document types this codebase deliberately lacks (the
`InventoryTrackingMode` deferral). The screen also belongs to a pipeline we do not have and did not
scope: upload → parse client-side with ExcelJS → **a column-mapping step**
(`/config/import-export/csv-mapping`) → the staging grid → POST ENTRIES. Recorded as read and out of
scope rather than quietly conflated with this phase's subject.

**The method that mattered.** `/config/import-statement` is a route, and a route is not a feature
(phase 53's own lesson). What settled it was reading the *endpoint map* the client is built on:

```js
reconciliation: { url: {
  bank_statement: "/bank-statements",          tigg_statement: "/transactions",
  tigg_gl_statement: "/gl-transactions",       upload_statement: "/import-bank-statement",
  reconciliation: "/reconciliations",          balance_history: "/balance-history/:id",
  statement_action: "/bank-statements-delete", recon_detail: "/bank-reconciliations/:id",
  fetch_statements: "/fetch-statements",       account_recon_overview: "/reconciliation-report?account_id=",
  reconciliation_report_export: "bank-reconciliation-export",
  "validate-account": "/validate-account", "validate-otp": "/validate-otp",
  "disconnect-bank-feed": "/disconnect-bank" } }
```

`/bank-statements` and `/bank-statements-matched` both answer **200** on a live tenant.
`/import-bank-statement` is POST-only. And `validate-account` / `validate-otp` / `disconnect-bank`
settle the kickoff's second question outright: the **bank feed is a separate mechanism** — a
per-account connection with an OTP handshake, whose `fetch_statements` call posts
`{account_id, account_number, bank_name, from_date, to_date}`. The account payload carries
`bank_connected: "not_connected"` and `rapid_connected: false` to match. **Out of scope, and now
recorded against something observed rather than inferred from an endpoint name.**

### The real screen

`/accounting/bank-accounts/:id/import`, gated on **`bank-edit`** (literally
`<Permission permission="bank-edit">` in its JSX), in two legs:

1. `POST /import-bank-statement`, multipart `{file, account_id}` → `{statements: [...], errors: [...]}`.
   **Writes nothing.** A server-side dry run, with the parsed rows returned to the client.
2. `POST /bank-statements` with `{account_id, statements}` → commits. Confirm Upload is disabled at
   zero valid rows; errors never block a partial commit; and the user may **delete individual valid
   rows in the review** before confirming, so the apply is "what you kept", not "what validated".

Its statement row: `{date, description, description1..3, dr_amount, cr_amount, batch_code,
posting_date, transaction_id, reconciliation_id, account_suggestions, is_suggestion_completed}`.
**No balance, no reference, no cheque number.**

Its list screen's columns are Date (DATE_RANGE filter, sortable), Description, Amount
(`dr_amount - cr_amount`, AMOUNT_RANGE filter, rendered with the account's currency symbol), and
Status — a filter with exactly two options, `true → Reconciled` and `false → Pending`, rendered off
whether `reconciliation_id` is set.

### The two templates, decoded

Both were downloaded and their XML read:

| | Columns (row 1, from **column B**) | Convention |
| --- | --- | --- |
| `bank_statement_sample1.xlsx` "Single Amount Column" | Date, Amount, Description | *"Deposit Amount: Positive Amount / Withdrawal Amount: Negative Amount"* |
| `bank_statement_sample2.xlsx` "Two Amount Column" | Date, Deposit, Withdrawal, Description | one column per direction |

Both say *"Do not change Column Header and their position"* and *"Date Format: Only AD Dates are
accepted"*.

### The probe matrix

Twelve files built and posted to the live validate endpoint (which writes nothing, so nothing was
committed to the reference tenant at any point in this phase):

| Probe | Result |
| --- | --- |
| clean, both templates | 200, `errors: null` |
| text date — ISO, `dd-mm-yyyy`, `dd/mm/yyyy` | all `Row [N]: invalid date` — **only a real Excel date cell parses** |
| missing date | `Row [N]: invalid date` |
| no amount | `Row [N]: both deposit and withdrawal cannot be zero` |
| both amounts | `Row [N]: amount must be either deposit or withdrawal` |
| missing description | accepted |
| **negative deposit** | **accepted verbatim as `dr_amount: -40`** |
| renamed header ("Txn Date") | accepted — columns match by **position** |
| reordered columns | every row `invalid date` |
| no header row | 0 rows, no error — row 1 is always discarded |
| empty file | 0 rows, no error, HTTP 200 |
| duplicate rows | both accepted, silently |
| extra column ("Balance") | ignored |

`errors` is a flat array of `Row [N]: message` strings, and a rejected row is **dropped** from
`statements` — the two sets are disjoint.

---

## Decision A — it is an ordinary importer, and the kickoff's doubt does not survive phase 21c

The kickoff put this as the phase's real question: *"what does phase 38's machinery do when a row
resolves into no command? A bank statement line resolves into no command — it is a raw row awaiting
a match, and its only destination is a table."*

**The premise is false, and phase 21c is the counter-example already in the tree.** A
`MigratedSalesRegisterEntry` is a raw row awaiting a report; it posts nothing, numbers nothing,
approves nothing, has no lifecycle, and is read by exactly two screens. It has had
`CreateMigratedSalesRegisterEntryCommand` and a place in the `ImportEntityType` list since that
phase. Nothing in `ImportRowPlan` asks a command to post to the GL or carry a status; it asks that
the command **exist before it is sent**, which is the entire mechanism by which the dry run and the
apply pass share one resolution.

Phase 38's precedent for the other answer — the landed-cost grid, which is *neither* an
`ImportTemplateDefinition` nor an `ImportJob` — turns on something specific and absent here: its
template is generated **from the document in front of you**, nothing is written, and there is
nothing to resume. A 5,000-line bank statement is precisely what the row ledger, the heartbeat and
the resumable claim exist for.

So: a tenth `ImportEntityType`, an ordinary `CreateBankStatementLineCommand`, an ordinary
`PlanAsync`. The payoff is that the row ledger, per-row error reporting, the pre-commit review,
cancellation, resume-after-crash, artifact retention and **per-row permission re-checking** all
arrive without a line of new runner code — demonstrated rather than asserted in
`BankStatementImportTests`.

### The one genuinely new thing: per-run context

Every other importer's rows are self-describing. A statement row is not: *"01-09-2026, 1500, salary"*
says nothing about which account received it. The reference product agrees — it takes the account
from the route and posts it beside the file.

So `ImportJob` gains a nullable `BankAccountId`: **context for the run, not data on the row.** A
column per row would store one value 5,000 times and admit a file whose rows disagree with each
other.

A field meaningful to one of ten enum members is a smell. The alternative — a second job table — is
the shape phase 21c already rejected when it put the two migrated registers on this aggregate
rather than beside it, and for the same reason: the job, its row ledger, its runner, its cancel, its
heartbeat and its retention are all the same job. The smell is paid for with a rule asserted in
**both** directions (`BankStatementImportSweepGuardTests`, driven from the enum so a tenth type
cannot silently join the wrong side), and the E2E proved both live: a 400 for a statement import
with no account, and a 400 for a *Product* import that names one.

`ImportJob.Create`'s new parameter is **required, not trailing-optional**. Phase 51's lesson is that
an optional parameter ends a sweep, and phase 54 needed `AddLine`'s parameters to be required before
the compiler would walk back through the third Void carrying the bug. It enumerated the Api endpoint
and five test call sites; nothing was missed.

---

## Decision B — one signed amount, not the vendor's two columns

`StatementAmount` is a `readonly record struct` over one signed decimal: positive into the account,
negative out. No implicit conversion from `decimal`, no public constructor — a caller says
`Deposit(x)` or `Withdrawal(x)`, both taking a **positive magnitude**, and the compiler error is the
feature.

**Why a type.** A statement amount is signed from the *bank account's* point of view; every other
amount in this codebase is signed from a document's or a posting rule's. Those agree until they do
not, and phase 56 will put them side by side in a two-pane matcher. This is `PrimaryQuantity`'s
argument (phase 52) in a second place, and it is here because phase 54 found the same class of bug a
third time in a third Void.

**Why one column rather than the vendor's `dr_amount`/`cr_amount`.** Two columns of which exactly one
may be non-zero is *redundant state guarded by a rule* — and this codebase has ruled against that
shape twice: `ProductBatch` stores no quantity (phase 51), `PrimaryQuantity` is derived and never a
column (phase 52). A signed value carries the identical information with no state in which it can
disagree with itself. **This diverges from the vendor's schema and matches its behaviour exactly**,
which is recorded rather than smoothed over — phase 52's trade. The `sqlcmd` column dump in the E2E
is the proof: one `decimal(18,2)` named `Amount`.

**Where it bit, and the hole it exposed.** `SortSweepGuardTests` constructs entities by reflection
and handed the factory `default(StatementAmount)` — a zero. The failure did not appear at the write;
it appeared on the way back **out** of the database, inside the EF value converter, i.e. as *a list
that would not load*. C# hands every caller `default(T)` however private the constructor is, so no
value type can close that hole on its own. `BankStatementLine.Create` closes it, and
`A_default_constructed_amount_is_refused_at_the_write` pins it with the argument name.

**Stricter than the reference product in one place.** Its two-column template stores a negative
Deposit verbatim. A column called Deposit takes a positive number; ours says so, and the E2E's
four-way row-error assertion includes it.

---

## Decision C — Status is derived, and phase 56 adds the column it derives from

The kickoff warned against inventing a lifecycle and pointed at the vendor's `reconciled: false`
filter. The live read settled it more precisely than inference could: the list's Status column is a
filter with exactly two options rendering off whether `reconciliation_id` is set, and the row
carries `reconciliation_data` — an array of the matched documents. That is a nullable foreign key
with a filter over it, which is phase 51's ruling about the serial report's Status filter reaching
the same answer a second time.

**This phase ships neither the column nor the filter.** A nullable foreign key pointing at a table
that does not exist yet is the present-and-ignored shape phase 43 ruled against, and a Status column
reading "Pending" on every row forever is furniture rather than information. Phase 56 adds the
aggregate, the column, the filter and the column's index together. The omission is asserted in both
suites (`A_statement_line_has_no_lifecycle_of_its_own`, and the Angular
`shows no Status column, because nothing can be reconciled yet`) so that adding one is a decision
somebody has to delete a test to make.

The aggregate carries no currency either, and that is not an oversight: `Account` has no currency in
this codebase — phase 28 put multi-currency on documents, not on the chart of accounts — so a
statement line is in the tenant's base currency. Nothing here needs `ToBase`, and nothing may
acquire it before the account acquires a currency.

---

## Decision D — deletion, and the duplicate that cannot be prevented

Phase 21b's rule is that a feature which writes rows owes its deletion story decided alongside it.

**By row id, hard, with no soft-delete or void** — the reference product's own shape
(`POST /bank-statements-delete` with `{account_id, action: "delete", statements: [ids]}`, wired to
both a single-row action and a bulk action behind confirm dialogs). There is nothing to preserve: a
statement line posts nothing, so removing one leaves no trail to contradict and no balance to
restate. It is the bank's record, re-importable at any time.

**There is deliberately no unique index, and this is the decision a re-import makes people want.**
A bank statement has no natural key: two identical ATM withdrawals on one day are two real lines, so
any uniqueness rule wide enough to catch a duplicated upload also rejects genuine data — phase 52's
*"a conversion factor below one is ordinary"* in another key. The reference product accepts
duplicates silently, and so do we (asserted).

**What we add instead is the undo.** Every line carries the `ImportJobId` that created it, so a file
uploaded twice is *one action* to take back rather than a hunt through the list. The reference
product keeps a `batch_code` on its rows for the same purpose and never offers it as a delete unit;
this does. Using the existing job id rather than minting a second identifier meaning the same thing
is the point — the browser pass shows two imports on one account offering "Undo import 1" and "Undo
import 2" separately.

A delete naming nothing at all, or both selectors, is a 400; a delete whose ids belong to another
account of the same tenant is a 404 rather than a silent "0 removed", because the caller cannot
otherwise tell which happened.

---

## Decision E — two permission keys, derived

The census found bank reconciliation carries **no keys of its own** beyond
`bank-reconciliation-export`, riding `bank-view` / `bank-edit` / `bank-full-access`; this pass found
the Import Statement screen gated on **`bank-edit`** specifically. So the shape is a **View/Manage
pair**, not the View/Create/Edit/Approve/Void shape of a document — a statement line has no
lifecycle to gate.

`Accounting.BankStatement.View` and `Accounting.BankStatement.Manage`, **both Admin+Member**, at the
same bar as `ChequeView`/`ChequeManage` (phase 17) and for the reason recorded there: routine
daily-use working data. The Admin-only argument this file applies to flat registers exposing PAN and
counterparty identity does not reach here — a statement line carries the bank's own narration about
the tenant's own account, and a Member who cannot open it cannot do the bookkeeping the feature
exists for.

**Not folded into `BankAccountView`**, which is on the account list and its live balances: a tenant
wanting a bookkeeper who reconciles without seeing every account's balance (or the reverse) cannot
express that if the two share a key. **Not riding `AccountManage`** the way *creating* a bank account
does, because "may edit the chart of accounts" is not the same permission as "may load this month's
bank statement".

**Importing needs both `ImportJobManage` and `BankStatementManage`** — the first to enqueue, the
second re-checked *per row* by `AuthorizationBehavior` as the runner sends each create command under
the initiating user's identity. A user holding only the first gets a job that aborts naming the
second (asserted).

**Neither key can be granted per location, and the kickoff's claim that they should be is wrong.**
It asserted "phase 32b's per-location scope applies because a bank account is location-scoped".
`Account` carries no `LocationId` in this codebase; the check is one grep.

---

## Decision F — one template, not the vendor's two

The vendor's "Single Amount Column" variant exists to save a user a transformation while copying out
of whatever their bank exports. But the user is copying into *our* template either way, so the
transformation happens regardless — and the split-column form is the one that cannot be misread,
because a signed column puts the whole deposit/withdrawal distinction on a minus sign a spreadsheet
will happily strip with a number format. Supporting both would also mean template *variants*, which
is real machinery for a convenience. Recorded as a divergence, additive if ever wanted.

**Three differences from the vendor's parser, each deliberate:**

1. **Columns match by name.** Its parser skips row 1 whatever it says and reads B..E positionally.
   `ImportRowReader` matches header text, which this codebase has done since phase 21a and which
   turns a reordered file into a clear whole-file rejection instead of "invalid date" on every row.
2. **Text dates parse.** Its parser accepts only a real Excel date cell. `GetRequiredDate` accepts
   ISO, `dd-MM-yyyy` and `dd/MM/yyyy`, day-first ahead of month-first (phase 21c). A user pasting
   from a bank's CSV export gets text dates, so refusing them is a defect rather than a rule.
3. **A negative Deposit is rejected** (Decision B).

---

## What was not added, and why

**No index beyond the convention's two.** `TenantIndexConvention` recognises `Date` by name and
derives `(OrganizationId, Date)` and `(OrganizationId, CreatedAt DESC)`, which is what makes both of
`ISortableQuery`'s orderings index-backed — `ListSortIndexCorrespondenceTests` proves it, and the
census there is now 16. The obvious third candidate is `(OrganizationId, BankAccountId, Date)`,
because this list is only ever read one account at a time. **It is not added, because nothing has
measured it.** Phase 34c's rule is that an index is added against a number and that an index added
for one path changes the plan for every other path on the table; phase 50 restated it after a
reasoned-about index took a sibling tab from 2,143 logical reads to 83,308. A tenant holds a handful
of cash-and-bank accounts, so the account predicate's selectivity over a date range is small, and
"obviously better" is exactly the claim those phases say not to ship unmeasured.

The re-entry condition, so this is checkable rather than a shrug: **a tenant with several accounts
and enough statement history for the list's first page to cost more than the count**, measured
through `tools/scale/` on logical reads, never the wall clock.

This is also why the phase owes no performance number: its only indexes are the convention's
automatic ones, on a table it creates. Phase 34c's rule is about other paths on an existing table,
and there are none.

---

## The guard the phase had to teach rather than exempt

`SortSweepGuardTests` and `SearchSweepGuardTests` sweep every paginated `List*Query` with a date
range. `ListBankStatementLinesQuery` is the **first parent-scoped one** to reach them — a statement
belongs to one bank account, and the account is required — and the guard broke on it twice: it built
the query with `Guid.Empty` for the account (so the validator rejected it, which the guard read as
"rejects 'newest'"), and it assumed every handler takes `(db, currentUser)`.

The easy answer was an exemption. That is the shape phases 39, 40 and 50 each criticised — a guard
whose predicate stops matching stops covering and reports success either way, and an exemption on a
*brand-new* screen is how a seam stays empty for six phases. So the guard learned instead:
`ParentScopedIdFor` finds the query's one required parent id, `Activate` fills it, `Seed` gives every
seeded row the same parent **matched by name** between the query's constructor and the aggregate's
`Create` factory, and the handler is constructed by asking its constructor rather than assuming.
Teaching it is what found the `default(StatementAmount)` hole.

---

## Manual E2E

Against real SQL Server on a **fresh Organization**, seeded by direct API calls, every status code
printed. `scratchpad/e2e55.py` and `e2e55_403.py`.

**The dry run and the apply pass agree** — phase 38's whole claim, on the cheapest possible proof.
A four-row file with one bad row (both amounts populated):

```
  status            = PendingConfirmation      rows the DRY RUN rejected  = [4]
  validatedRowCount = 3                            row 4: A statement line is either a deposit or a withdrawal, not both.
  failedRowCount    = 1                        lines written so far = 0     <-- the dry run writes nothing
  -- Confirm Upload --
  status            = Completed                rows the APPLY pass rejected = [4]
  succeeded         = 3                        DRY RUN == APPLY ? True
```

Also proven: both directions of the account rule as 400s (a statement into `Sales Revenue`; a
statement with no account; a **Product** import naming one); `sort=customer` as a 400; delete by id;
undo-a-whole-import; and a re-import.

**`sqlcmd`, the claim that matters:**

```
StatementLines,GlJournalEntries,GlLines,Payments,Cheques,StockLedgerEntries,StockMovements
3,             0,               0,      0,       0,      0,                 0

Amount | decimal | precision 18 | scale 2          <-- ONE signed column, no dr/cr pair
LinesOnTheWrongAccount 0 | LinesWithZeroAmount 0

-- across the WHOLE database, only a BankStatement job may name an account:
Account 2/0 · AccountGroup 2/0 · BankStatement 2/2 · ContactPersonnel 3/0 · MigratedPurchaseRegister 2/0
MigratedSalesRegister 4/0 · Product 1/0 · ProductAttributePool 1/0 · ProductCategory 4/0 · Supplier 3/0
```

**The negative path**, three legs plus a control, one user, one organization, one run:

```
[404] delete a nonexistent line as Admin      {"title":"No matching bank statement lines were found."}
[403] the SAME nonexistent line, restricted   {"title":"You do not have permission to perform this
                                               action (Accounting.BankStatement.Manage)."}
[200] the SAME user still reads the statement  3 lines returned
[404] Admin again, after the restore           back to 404, not 403
```

403-not-404 against a nonexistent id is what proves `AuthorizationBehavior` fired *before* the
handler looked for the row. The membership is restored by
`UPDATE tenancy.OrganizationMemberships` (phase 52: the API cannot undo it, because moving a
membership back needs `Tenancy.Role.Manage`), and phase 51's lesson is why the restore is not
optional — a run that ends with its own user on a restricted role leaves the browser pass looking at
a broken app.

**Browser pass**, on this phase's own two screens: the Bank Accounts card's new Statement link; the
statement list rendering deposits and withdrawals in their own columns with the right colours; row
selection and "1 selected / Delete selected"; **two imports on one account offering "Undo import 1"
and "Undo import 2" separately**; the Import Statement button landing on Import / Export with the
type, the account and the locked Create-only action already set; the account picker appearing for
`BankStatement` and absent for `Product`; and a real upload driven to **Confirm Upload → Completed,
1 imported · 1/1 rows**. No console errors.

The nicest single frame is the Bank Accounts card: **balance `0.00` beside four imported statement
lines**. That is the independence a reconciliation needs, visible without reading a test.

---

## Bugs found during the phase

1. **`default(StatementAmount)` was constructible and read back as zero**, surfacing in the EF value
   converter as a list that would not load. Found by teaching `SortSweepGuardTests`; fixed in
   `BankStatementLine.Create`, which is the only place that *can* fix it.
2. **`PagedResult<T>`'s constructor is `(Items, Page, PageSize, TotalCount)`**, not
   `(Items, TotalCount, Page, PageSize)` — caught by the compiler, noted because the two are
   type-compatible in the wrong order and the mistake is silent in any language without that luck.
3. **The import-page spec doubles stubbed `ActivatedRoute` with `paramMap` and no `queryParamMap`**,
   so eleven existing tests failed the moment the page read one. Fixed in the doubles rather than by
   having the page defend against a stub missing half of what it doubles.
4. **A permission failure aborts the whole job rather than failing every row** — phase 21a's
   existing, deliberate behaviour, which this phase's first test expectation got wrong. Corrected to
   assert the real behaviour, which is the stronger claim.

---

## Carried into phase 56

1. **Phase 56's start condition is met, and the roadmap's reason for thinking otherwise is wrong.**
   `/accounting/recon` takes its account from router state, but the six real screens are
   `/accounting/bank-accounts/:id/{import, bank-statement, book-statement, manual-reconcile,
   matched}` and take it from the **URL**. The two-pane matcher renders fully on Cadehi's existing
   **Cash** account, with the right-hand pane already populated from the tenant's own transactions
   and the header reading `No txn selected | RECONCILE | No txn selected`. No Bank-type account is
   needed for the read.
2. **What 56 adds to this phase's aggregate**: `BankStatementLine.ReconciliationId` (nullable), the
   Status column and its Reconciled/Pending filter, and the index that filter needs. The list
   query, the DTO and the Angular page all have the seam marked.
3. **The two feeds for the right-hand pane** are `/transactions` (document-level: `{id, date,
   source, code, note, amount, npr_amount, currency_code, conversion_rate, status, account}`) and
   `/gl-transactions`. Both filter on `reconciled: false`; the matcher collects `bs_ids` + `tx_ids`
   and POSTs them together.
4. **`account_suggestions` / `is_suggestion_completed`** on the statement row, and the
   `show_suggestions=true` parameter on its list query, are an **auto-match suggestion engine**. Read
   and recorded; not scoped, and 56 should decide explicitly rather than inherit it.
5. **The generic CSV import pipeline** (upload → column mapping → staging grid → POST ENTRIES, for
   Delivery Note / GRN / Inventory Adjustment) is read and out of scope. Two of its three types are
   the `InventoryTrackingMode` deferral; the third, Inventory Adjustment, is a real gap if anyone
   ever wants a *document* importer — which phase 52 noted this codebase has none of.
6. **`is_bank_user`** is a flag on the vendor's user payload that swaps the entire shell for a
   different one. Noticed, not investigated.
