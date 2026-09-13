# Phase 38 — import and export breadth

## TL;DR

Reach, not correctness: no Domain invariant changed and no posting rule moved. The acceptance bar
was therefore not "a good file works" but **"a bad file is refused with a row-level reason before
anything is committed"**, and that is what shaped every decision below.

1. **Five new upload types** — Account, Product Category, Account Group, Contact Personnel, Product
   Variant — taking the list from three to eight. Four of them are the reference product's own
   deferred types; the variant importer is an **addition** and is labelled as one.
2. **Intra-file parent ordering and cycle detection**, solved once in `ImportRowSequencer` and used
   by **two** importers, not the three the roadmap named — see Decision A for why `Account` is a
   leaf. A cycle or a duplicate key fails the **whole file** with its rows named and writes nothing.
3. **The pre-commit review is real** (`ReviewBeforeApply` → `Validating` → `PendingConfirmation` →
   Confirm Upload). It needed **no new table**: the dry run claims only the rows it rejects in the
   existing row ledger, so the apply pass skips them by the same mechanism that makes a crashed
   import resumable.
4. **Export breadth by a rule**: a category earns its place when its rows cannot be reconstructed
   from the five already there. That admits Sales Documents, Purchase Documents and Payments — and
   refuses, say, a Stock Position sheet. Plus a per-category selection and a date range.
5. **The 25,000-row cap is raised to 50,000**, on phase 34c's measured 2.5 kB/row rather than on
   taste, and joined by a **workbook-wide** budget of 150,000 — which is the shape 34c said the cap
   should have had all along, and which this phase needed because it took the category count from
   five to eight.
6. **The landed-cost grid's Import was read live** and phase 29's one-line note was wrong: it is a
   template-based `.xlsx` drawer, not a clipboard paste, and its template is generated **from the
   bill in front of you**.

**Three corrections to the kickoff**, all worth carrying: `Account` is not a tree; the dry-run
review step was **already** confirmed live in phase 21a and is a parity item rather than "the one
genuinely unconfirmed shape"; and the landed-cost paste is a file import.

**One bug only the E2E could find**: a Minimal API binds an **array** parameter from the *body* on a
POST, so `?categories=…` silently arrived null and every export ran all eight sheets while the screen
said otherwise. `[FromQuery]` is load-bearing.

Tests: Domain 452 (unchanged), Application.UnitTests **1035 (+22)**, Api.IntegrationTests **24 (+6)**,
Angular **305 (+4)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. Manual E2E
against a fresh Organization on real SQL Server, with every fixture built by **filling the app's own
generated template**, plus a browser pass that clicked the real Confirm Upload.

---

## Step 1 — what the kickoff got wrong, checked before coding

The kickoff named the landed-cost dialog as "the one genuinely unconfirmed shape" and the review
screen as needing design. Reading `phase-21a-status.md` first inverted both:

| Kickoff said | Actually |
| --- | --- |
| Column sets are the thing to read | **Already read.** Phase 21a downloaded and read **all seven** reference templates (`phase-21a-status.md:107-135`), Account / Product Category / Account Group included. They are specification. |
| The dry-run review is genuinely unconfirmed | **Already confirmed.** 21a read the whole four-step wizard live, including step 3's `{statements, errors}`, its *"N records validated / N records have errors"*, its `Row: {LineNo} {Header} {Message}` rendering, and its **Confirm Upload / Reupload New File** buttons. |
| Account, Product Category and Account Group are all trees | **Two are.** See Decision A. |
| The landed-cost control is "a bulk paste of the grid" | **It is a file import.** See Decision F. |

Only the last of those actually needed the browser, and that is what the live pass was spent on.

---

## Decision A — the ordering mechanism, and the two types that need it

`ProductCategory.ParentCategoryId` and `AccountGroup.ParentGroupId` are **self**-references: a row's
parent can legitimately be another row of the same file, which the reference product's own templates
say out loud (*"Parent category should already exist or should be in the upcoming rows"*, *"Make
sure there are no cyclic dependencies"*).

**`Account` is not the third.** Its template's "Account Group" column points at an `AccountGroup` — a
*different aggregate*, which an Account file cannot contain and which must already exist. An Account
file has no edges inside it at all, so there is no order to compute and no cycle to detect. Giving it
the sequencer anyway would have implied a relationship the model does not have; `AccountImporter`'s
doc comment says so, so the next reader does not "fix" it.

`ImportRowSequencer.Order` is therefore one implementation behind one marker interface
(`IHierarchicalImporter`, two members: the key column and the parent column), run by the processor
**before the first row is planned, in both passes**:

- **Kahn's algorithm with a row-number tie-break**, so the order is a function of the file alone —
  which matters because a resumed job re-sorts the same file and must walk it the same way.
- **Only edges inside the file are edges.** A parent name no row claims is treated as external: it
  is either already in the tenant (the importer resolves it by name) or nowhere (that *one* row is
  rejected). Guessing here would turn a single bad cell into a whole-file failure.
- **A cycle fails the file**, naming the rows. Half-importing was the rejected alternative: a partial
  tree leaves a catalogue in a state neither the tenant nor the file describes, and the user cannot
  fix it by correcting the file and re-uploading, because the good rows would then collide.
- **A duplicate key fails the file too**, for a reason worth separating: the *ordering* becomes
  ambiguous, since a third row naming that parent cannot be said to mean either of them.
- A row that is its own parent is a cycle of length one, caught by the same mechanism rather than
  sorting cleanly and being rejected later by the database.

---

## Decision B — the pre-commit review, and the table it did not need

`CreateImportJobCommand`'s own doc comment predicted the shape in phase 21a: *"Restoring a pre-commit
review step on top of this design is additive (a validate-only mode plus a confirm command)"*. That
is exactly what shipped.

**The mechanism.** `ImportJob` gained two columns — `ReviewBeforeApply` (bool) and
`ReviewConfirmedAt` — and `ImportJobStatus` gained `Validating` and `PendingConfirmation`. Which pass
a runner is in is **derived** from the pair (`AwaitsValidation`) rather than stored a third time.
`ConfirmImportJobCommand` stamps the timestamp and puts the job back to `Queued`; the runner's next
claim is the apply pass. No second runner, no second queue, no job-kind discriminator.

**No new table, and this is the part worth reading twice.** The obvious design is a findings table
next to `ImportJobRow`. It is unnecessary: the validate pass **claims only the rows it rejects**, in
the ordinary row ledger, marked Failed with their reason. The apply pass then skips them because they
are already terminal — the identical mechanism that makes a crashed import resumable — and they are
already the entries the results grid will show. "N records validated" is derived as
`TotalRowCount − FailedRowCount`.

**What the dry run can and cannot promise, stated rather than implied.** It runs
`IEntityImporter.PlanAsync` (required cells, type coercion, enum spellings, name-to-id foreign-key
resolution, the update-mode code lookup) and then the built command's **own registered
FluentValidation validators**, resolved by the request's runtime type — so the review shows the real
rules, not a second weaker copy written for the importer. It does **not** execute anything, so a
uniqueness clash or a lifecycle conflict is still only knowable at the moment of writing and still
surfaces afterwards as a row failure. Validation failures are a strict subset of apply failures,
which is what makes claiming them early safe.

**The trade against NFR-4.3, with the number.** Review costs a **second full pass over the file**
(parse + plan every row twice) plus an unbounded wait for a human. Measured on the E2E's own runs the
validate pass costs about as much as the apply pass minus the writes — for the three-row category
file, ~0.4 s of a 4.3 s round trip, with the remaining ~3 s being the runner's poll interval and the
human. NFR-4.3 is not violated: the job is still asynchronous and the session is still not blocked.
What is traded is **throughput** and time-to-completion, and it defaults **on** anyway, because that
is what the reference product does and because a user about to create a thousand records should be
shown what will happen. Unticking the box is the phase-21a behaviour verbatim.

**Discard is `CancelImportJobCommand`.** Nothing was written, so there is nothing to undo and a
separate Discard command earns no place; `PendingConfirmation` joins `Queued` as a status the cancel
handler retires on the spot, because neither has a runner holding it.

**The one thing the dry run could most easily have got wrong**, and did not: a row whose parent a
*later* row creates. Nothing is written during validation, so the ordinary name lookup finds nothing
and the review screen would have reported a perfectly correct file as full of errors — then applied
it successfully. `ImportRowContext.PendingKeys` is empty during apply and holds every in-file key
during validation; a hierarchical importer that cannot find a parent asks `WillCreate` before
rejecting, and answers a yes with `ImportRowPlan.Provisional`, a plan that **throws if executed** so
the distinction cannot be lost by a later caller.

### The seam change that made it possible

`IEntityImporter.ApplyAsync` became `PlanAsync`, returning an `ImportRowPlan` — the command built but
not sent. Before this there was no moment at which the intended command existed and had not yet run,
and therefore no way to show a user what an import was about to do. Both passes plan rows the same
way; the dry run simply stops. Eight importers, one seam, and no duplicated resolution logic.

---

## Decision C — the five new upload types, and the columns each omits

Every column set below is the reference product's own template as read live in phase 21a, minus what
this codebase has nowhere to put — the rule `ProductImporter` set: *adding a column later is additive
and harmless; shipping one that silently does nothing is not.*

| Type | Ships | Omitted, and why |
| --- | --- | --- |
| **Account** | Code, Account Name**, Account Group** | `Description` (no field). `Opening Balance` / `Opening Balance Type` — an opening balance here is an `OpeningBalanceLine` (phase 17): a dated, balanced "day zero" transaction with its own screen and GL consequences, not an attribute. The same reasoning that kept Opening Quantity off the Product template, and it applies harder: the DR/CR word is meaningful only inside a balanced entry one row cannot express. `Kind` is not on the reference template either, and a bank account needs an institution and a number to be worth anything (phase 17 gave it a screen). |
| **Product Category** | Category Name**, Parent Category | `Description` (no field). Create-only. |
| **Account Group** | Name**, Parent Group**, Primary Group** | `Description` (no field). Create-only — the reference product's own asymmetry, **plus** a second reason: `AccountGroup.Update` deliberately refuses to change `RootType` (phase 3's ruling), so an update file carrying a Primary Group column could not honour half of what it says. |
| **Contact Personnel** | Code, Contact Name**, Contact Group, Phone No, Email, Address, Organisation**, Title** | Nothing. This is the importer phase 21a's confirm-live pass existed to prevent getting wrong: the reference product's "Contact" type is a **person attached to** a customer or supplier, not a `Contact`. |
| **Product Variant** | Parent Product Code**, three Attribute/Value slots, Variant Name, SKU, Barcode, Selling Price, Purchase Price | No reference template exists — every column is a decision. |

**Create-only now numbers five**, and the validator rejects the other mode at upload rather than
producing one identical row error per row. Two are the reference product's asymmetry (Product
Category, Account Group), two are phase 21c's migrated registers, and the fifth is the variant
importer — whose reasoning is its own: a variant's identity *is* its combination, so "update" would
either mean moving it to a different combination (a different variant) or editing its prices, which
`ProductImporter` already does by product code, because a variant **is** a Product.

**Three decisions inside the variant importer**, since none of them came from an observation:

- **Three fixed Attribute/Value slots.** A combination is an open-ended set of pairs, which a
  rectangle cannot hold without either one column per attribute in the tenant's catalogue (a template
  whose shape changes when somebody adds a colour) or a single cell of encoded pairs (unreadable, and
  unparseable the first time an option value contains the separator). Three covers what the live
  product carries — its richest example is four colours × three sizes, i.e. two attributes — and a
  fourth is an additive column.
- **The parent's attribute pool must already offer the combination.** `Product.CreateVariant`
  refuses an option the parent does not list, and this importer does not silently widen the pool:
  a typo in one row would otherwise add a permanent option to a product's matrix.
- **Contact Personnel's update mode matches on Organisation + Code, and says so**, because
  `ContactPersonnel.Code` is nullable free text with no unique index. An ambiguous match is a row
  error naming the ambiguity rather than a coin flip.

**No new permission keys.** Every importer sends the target type's own Create/Update command, which
`AuthorizationBehavior` re-checks per row at execution time — phase 21a's Decision B paying off for
the fifth, sixth, seventh and eighth time.

---

## Decision D — export breadth, by a rule rather than by taste

FR-2.8 names five categories. The rule for a sixth: **a category earns its place when its rows cannot
be reconstructed from the five already there.**

Ledger Transactions is the posted General Ledger, so it carries what every document did to the
accounts and nothing of what the document *says*: no quantities, no unit rates, no per-line VAT, no
line text. A tenant taking their data out had the accounting and not the trade. So:

- **Sales Documents** — Invoice and Credit Note **lines**, with their header repeated alongside.
- **Purchase Documents** — the mirror, Purchase Bill and Debit Note lines.
- **Payments** — a payment's own document number, direction, mode and contact, none of which the GL
  entry it posted carries.

The rule also **refuses** things: a Stock Position category would be `StockMovements` added up, and
the two migrated registers are a tenant's copy of what a prior system filed (the user was offered
them and chose the rule's three).

Two conventions inside the document sheets, both stated in their doc comments: **returns are
negated**, so summing the Amount column gives net rather than gross; and **drafts are included and
labelled**, because this is an export of the tenant's data, not a report — a draft invoice is data
they entered and would expect to find.

**The date range applies only to the categories that have a date**, and every reader publishes
`IsDateFiltered` rather than leaving it to be inferred. The workbook's Summary sheet prints the
window **per sheet**, because the alternative is the worst version of the feature: a user who exports
one month and sees a full product list cannot tell whether the filter worked, and one who sees a
short list cannot tell whether their catalogue is that small.

---

## Decision E — the 25,000 cap: raised, and reshaped

Phase 34c measured this and deliberately did not change it, handing over a number instead:
**ClosedXML costs about 2.5 kB of process working set per row**, from a run that produced 280,024
rows in ~700 MB above a 215 MB idle baseline, in 15.8 s. It also said what the right shape was:
*"25,000 per category was never the right shape"*, because the real constraint is a memory budget for
one buffered workbook divided by however many categories happen to be large at once.

This phase is the moment, for a reason 34c did not have: **it took the category count from five to
eight**, which would otherwise have multiplied the worst case by 1.6 without anybody choosing to.

- `MaxRowsPerCategory` **25,000 → 50,000**. On 34c's own dataset that takes Contacts from truncated
  to *complete* (50,002 rows available) and doubles what the ledger carries.
- `MaxRowsPerWorkbook` = **150,000**, new. At the measured coefficient that is ~375 MB above
  baseline — comfortably inside the envelope 34c already ran successfully. The budget is consumed in
  the fixed category order, which is why that order puts the **bounded** master-data sheets first:
  a workbook that reaches the ceiling truncates its *later* sheets and discloses it per sheet.

**The SAX rewrite is still not the move**, and now has a better answer than "later": per-category
selection and a date range let a tenant take 200,000 ledger rows out a quarter at a time, which is
the honest response to a cap rather than a bigger number.

---

## Decision F — the landed-cost grid's Import: read live, and it is not a job

Phase 29 recorded *"No 'Import' action on the product-wise matrix. The live product offers one (a
bulk paste of the grid)"*. **The paste was wrong.** Read live on 2026-09-12:

Ticking **Add product-wise** on a Purchase Bill reveals an **Import** button beside the checkbox
(it stays inert until the bill has product lines and the matrix has a product — which is why a first
attempt looked like a dead control). It opens a drawer containing a drop zone labelled *Add
Additional Costs*, the instruction *"The uploaded product's additional cost must be in accordance
with the template. Download the template from the link below, enter the product-specific additional
costs, and upload it here"*, and a **Download Template (.xlsx)** button.

**And the template is unlike the other eight in this codebase**, which is the finding worth carrying.
Its bytes (downloaded and read):

- one sheet, `Sheet1`;
- row 1 = `Products` (width 40) plus **one column per tenant cost term**, in the tenant's own order
  (`Addidsoajfdsoj`, `Clearing Charge`, `Custom Duty`, `Excise Duty`, `Freight`, `Insurance`,
  `Other Cost`, `Transportation`, `zxcas`);
- row 2 onward = **that bill's own product lines**, pre-filled in column A, amount cells blank;
- **no instruction block, no sample row, no `**` marker** — none of which would mean anything.

It is generated *from the bill in front of you*. So it needed its own writer
(`AdditionalCostGridWriter`) rather than `ImportTemplateWriter`: bending the latter would have meant
an `ImportTemplateDefinition` with dynamic columns and N sample rows, which is a different type
wearing the same name.

**It is deliberately not an `ImportJob`.** Every other importer in this phase writes master data
through a permission-gated command and must survive a crash. This one fills in a grid on a form the
user has not saved yet: nothing is written, there is nothing to resume, and a background job would
put the answer somewhere the form cannot see it. The file is parsed and handed straight back, and the
bill still has to be saved.

Two smaller calls inside it: the import **replaces** the product-wise rows it touches rather than
appending (a user who corrects their spreadsheet and re-uploads would otherwise get a doubled landed
cost — a number nobody would notice until it reached the FIFO layers); and both requests declare
`ILocationAgnosticRequest`, which is the `Preview*GlPosting` case exactly — an unsaved form's payload,
no row, no stored `LocationId`, nothing per-location to leak.

---

## Bugs and near-misses

### 1. A Minimal API binds an array from the **body** on a POST (found only by the E2E)

`POST /export-jobs?categories=Products&categories=Payments` bound `categories` to **null**, so
`ResolvedCategories` fell back to "all of them" and every export ran all eight sheets while the job
row and the screen both said two. It compiled; 1,035 unit tests passed (they construct the command
directly); the Angular suite passed (it asserts the service's params). Only a real HTTP call showed
it. `[FromQuery]` is load-bearing — and the two `DateOnly?` beside it bind from the query *without*
help, because a **simple** type does, which is exactly what makes the array's behaviour easy to miss.

This is phase-27b's *"a trailing optional parameter reaches nothing until the Api's own request
record carries it too"* in a third costume.

### 2. EF refuses a set operation after a client projection

`SalesDocumentExportReader` originally `Concat`-ed two `select new DocumentLineRow(...)` queries.
A constructor call is a client projection, so EF throws *"Unable to translate set operation after
client projection has been applied"* — **not** an InMemory-only quirk. Fixed by concatenating while
both halves are still anonymous and constructing the shared record from the materialised page.

### 3. Two guards bit, and both were right

- **Phase 23's `sweep-guard.spec.ts`** caught a native `<input type="date">` in the export date
  range. Dates are stored AD and rendered BS; `app-bs-date-input` is the only way to type one.
- **Phase 32b's `LocationScopeSweepGuardTests`** caught the two landed-cost requests carrying a
  location-scopable key with no marker. The answer was `ILocationAgnosticRequest` with a reason —
  which is what that guard exists to force somebody to write down.

### 4. The near-miss: a dry run that reports correct files as broken

Described under Decision B. It would have passed every unit test that did not specifically pair a
hierarchical importer with the validate pass, and the symptom — "the review says 40 errors, I
confirmed anyway and it worked" — teaches users to ignore the review.

---

## Manual E2E

A fresh Organization (`Phase 38 Traders`), master data seeded by curl, **every fixture built by
downloading the app's own generated template and filling it with openpyxl** (phase 21a's rule —
never a hand-rolled workbook). `curl` still cannot do `-F` uploads here, so the driver is Python
`urllib` (phase 30).

Proven:

- **A forward parent reference** (leaf first, root last) validates clean, writes nothing, and after
  Confirm Upload produces a **real three-level tree** — read out of SQL Server, not from the API:
  `Cashews → Nuts → Dry Goods → (root)`. Three roots would have looked identical through the API's
  own row count.
- **A cycle** fails the job with `row 3 ('Alpha' -> 'Beta'); row 4 ('Beta' -> 'Alpha')`, and the
  innocent third row of that file is absent from the database.
- **Account Group**: 2 in, 1 rejected for a Primary Group disagreeing with its parent's; `Misfiled`
  absent from SQL. **Account**: the row naming a real group imports, the other is a row error.
  **Contact Personnel**: attached to the real customer, unknown Organisation rejected.
- **Export**: `?categories=Products&categories=Payments&from=…&to=…` produces exactly
  `['Summary', 'Products', 'Payments']`, the job row stores the selection, and the Summary sheet
  carries both `2026-04-01 to 2027-03-31` and `not date-filtered` per sheet. A backwards range is a
  **400**.
- **Four 403s, each naming its exact key, against a nonexistent organization** — so the 403-not-404
  proves `AuthorizationBehavior` fired before any handler ran:
  `Configuration.ImportJob.Manage` (confirm and template), `Configuration.ExportJob.Manage`,
  `Purchasing.PurchaseBill.Edit` (the landed-cost template).

**Browser pass** over the phase's own new UI. Phase 25's recipe with one deviation worth recording:
port 4200 already had an **http** dev server running, so it was reused rather than starting the
`erp-web-ssl` profile — and an injected cookie then has to carry `SameSite=None; Secure` explicitly
or it is withheld on the cross-origin call to `https://localhost:7104` and the app bounces to Sign In
with no other symptom. The Import/Export screen shows all eight upload types, the
review checkbox, the eight export categories with their `(all dates)` markers and the BS date range;
the history renders the cycle failure with its rows named; and a `PendingConfirmation` job shows
**"Nothing has been imported yet. 1 of 2 records validated · 1 records have errors"** with
**Confirm Upload / Discard / Show errors**. Clicking the real Confirm Upload took it to Completed,
1 imported / 1 failed, and SQL confirms `Bimala Rai` exists, `Broken Row` does not, and the rejected
row carries `ColumnName = 'Organisation'` — claimed during the *dry run* and skipped by the apply
pass, which is the mechanism the whole decision rests on.

**What the browser pass did _not_ cover**, stated rather than left to be assumed:

- **The file input was never used.** The reviewed job was enqueued through the API and the page
  polled it, because the Browser pane cannot readily populate an `<input type="file">`. So the upload
  leg is proven through the real HTTP endpoint (with real `.xlsx` bytes) and **not** through the
  screen's own file picker.
- **The export form was read, not driven.** The category checkboxes, their `(all dates)` markers and
  the BS date range were confirmed to render; none was ticked and Start Export was never clicked from
  the UI. The selection and the range are proven through the API E2E instead.
- **This phase's own landed-cost drawer was never opened in a browser.** The *reference product's*
  version was read live (Decision F) and ours was built from it, but ours is covered only by
  `ng build`, `ng test` and the two endpoints' own tests. It is the least-covered surface in the
  phase, and the first thing to click if a later session has the app open.

---

## Files

- **Domain**: `Imports/ImportEnums.cs` (+5 entity types, +2 statuses), `Imports/ImportJob.cs`
  (+2 columns, `AwaitsValidation`, `MarkPendingConfirmation`, `ConfirmReview`),
  `Exports/ExportEnums.cs` (+3 categories), `Exports/ExportJob.cs` (selection + range).
- **Application**: `Imports/ImportRowPlan.cs`, `ImportRowContext.cs`, `ImportRowSequencer.cs`,
  `AccountImporter.cs`, `ProductCategoryImporter.cs`, `AccountGroupImporter.cs`,
  `ContactPersonnelImporter.cs`, `ProductVariantImporter.cs`,
  `Commands/ConfirmImportJob/`, plus the `PlanAsync` refactor of the four existing importers and the
  processor's validate pass. `Exports/Readers/{SalesDocument,PurchaseDocument,Payment}ExportReader.cs`
  and `DocumentLineRow.cs`. `Purchasing/AdditionalCostGrid/`.
- **Infrastructure**: `20260912012808_Phase38ImportReviewAndExportScope` — two ImportJob columns, a
  Status widening (20 → 30 chars; `PendingConfirmation` is 19), three ExportJob columns. **Both new
  non-nullable columns take a default that is the truth about the rows already there** (phase 37's
  rule): every prior import applied without review, and every prior export ran every category, which
  is why `ExportJob.SelectedCategories` reads empty as "all of them" and why neither needed a
  hand-written backfill.
- **Api**: the confirm route, `reviewBeforeApply`, the export query parameters, two landed-cost
  routes, `AdditionalCostGridWriter`, and `ImportFileException → 400`.
- **Web**: the eight upload types, the review checkbox, the review panel with Confirm/Discard, the
  export category picker and BS date range, and the Purchase Bill's Import drawer.

---

## Known limitations / follow-ups

- **The dry run cannot see what only writing can.** A uniqueness clash or a lifecycle conflict still
  surfaces after Confirm Upload. Closing that would mean running each row's command inside a rolled-
  back transaction, which is a different and much heavier design.
- **Variant import is create-only and needs the parent's attribute pool configured first.** Both are
  decisions (Decision C), but the second is a real ergonomic gap: there is no way to set up a
  product's Attributes Used in bulk.
- **The landed-cost drawer replaces rather than merges per product.** A user who wants to add a
  second cost term to a product already in the grid by file must include the existing amount too.
- **`MaxRowsPerWorkbook` is one machine's memory law, still.** Phase 34c's caution applies to this
  phase's number as much as to its own; a second measurement on other hardware would be worth more
  than another increase.
- **Carried from 37 and untouched here**: Inventory Master still lacks WarehouseTransfer and
  OpeningStock rows; multi-UOM × variants is unbuilt; a standalone Debit Note's Goods line still
  credits Inventory without touching the ledger. **From 36**: product-to-location is unenforced at
  save; *Display Warehouse in Column* and `sales-summary`'s Group Wise location grouping are unbuilt;
  the Sales Register's money columns are transaction-currency.
