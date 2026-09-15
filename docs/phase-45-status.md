# Phase 45 — Multi-UOM × variants, and import ergonomics

## TL;DR

**A variant owns its unit matrix, and the live read is what settled it.** Phase 24 left multi-UOM ×
variants unbuilt with the words "a genuine combinatorial design question nobody has posed", and the
kickoff framed it as a fork: an inherited conversion is a read-through, an owned one is "five more
columns and a sweep". It is **owned** — and it needed **neither**. The decisive observation is one
divergence on the reference tenant: variant `18001 Iphone 16 Pro Max XXL Blue` carries primary unit
**Number (NOS)** while its parent `Iphone 16 Pro Max` and all three of its siblings carry **Piecess
(ppp)**. A per-variant unit change did not propagate, so the matrix is per row; and because phase
24's Decision A already made a variant *a Product*, the collection already hangs off the row.
**Zero schema change this phase** (`dotnet ef migrations has-pending-model-changes`: "No changes
have been made to the model").

**What the read did change is the parent.** A variant parent's detail page in the reference product
has **no Inventory Details panel at all** — no stock figures, no Secondary Unit tab, no Warehouse
tab — while every variant child has its own with its own `ADD NEW` and a per-row `Action` column.
That is the same fact as phase 24's rule that a parent may not reach a document line. Phase 24's
sweep-guard allow-list had excused `AddSecondaryUnitCommandHandler` in exactly those words — "a
secondary unit is catalog metadata … attaching one to a parent moves nothing and reconciles against
nothing" — which is **the argument for refusing it**, used as a reason not to look. The exemption is
gone and the handler goes through `ProductVariantRules` like every other.

**The unit matrix gained its missing two thirds.** Phase 3 shipped add-only. The live table has an
`Action` column, so this phase added `UpdateSecondaryUnitCommand` and `DeleteSecondaryUnitCommand`,
plus the two refusals the add never had (the primary unit cannot also be a secondary; one unit
cannot have two rows). Without them a mistyped conversion rate was permanent.

**A ninth upload type, `ProductAttributePool`**, closes phase 38's recorded ergonomic gap. Its one
interesting property: a row **adds** to a set the command it sends would otherwise **replace**, so a
file of N rows for one product builds one pool rather than leaving the last row standing.

Also: `ListSmsLogsQuery` got the search term phase 39 named its re-entry condition for; the
landed-cost drawer's replace-per-product semantics were decided and pinned rather than left as a
sentence; the dry-run-in-a-transaction question was answered no, with the reason.

Tests: Domain **666** (unchanged), Application.UnitTests **1130 → 1156**, Api.IntegrationTests
**29/29** (unchanged, run with Docker up), Angular **447 → 479**. `dotnet build` / `dotnet test` /
`ng build` / `ng test` all clean; `ng build` does not warn and the initial bundle is **651.92 kB**,
unchanged, against phase 42's 680 kB budget.

---

## Step 1 — the live read (2026-09-15, Moonbeam UAT, read-only)

The kickoff asked three questions. All three were answered, and **no write was performed on the
reference tenant** — every dialog opened was closed without saving, which is why question 3 is
answered structurally rather than by experiment.

| # | Question | What the live product does |
|---|---|---|
| 1 | Which unit/price fields does a variant's own form show? | **All of them.** A variant's Edit form is the ordinary *Edit Product* form: Type, Name, Code, Category, Tax, **Primary Unit** (editable), HS Code, Available For Sale, Selling Price, Purchase Price, the four GL accounts, Track Inventory, Valuation Method, Reorder Level. Nothing marks it as a variant and nothing is read-only. Secondary units are not on the form at all — they live on the detail page's *Inventory Details → Secondary Unit* tab, which carries `ADD NEW` and a per-row `Action` column. |
| 2 | Are they pre-filled from the parent? | **At creation, by omission; afterwards, not at all.** The "New Variant Product" modal is exactly `Name*`, `Code*`, one select per attribute, `Selling Price*`, `Purchase Price` — **no unit field**, so the parent's primary unit is the starting value. The *Add Secondary Unit* dialog on a variant opens with `Measurement Unit*`, `Conversion Rate*`, `Selling Price*`, `Purchase Price*` **all blank**. |
| 3 | Does editing one variant's conversion change its siblings? | **No**, and the tenant's own data proves it without a write: `18001 … XXL Blue` has primary unit **Number (NOS)**; `15001 … XL Red`, `16001 … XL Blue`, `17001 … XXL Red` and the parent all have **Piecess (ppp)**. One sibling diverges and the others did not follow. |

Two further observations that were not asked for and both mattered:

- **A variant parent has no Inventory Details panel at all.** Its Overview is *Attributes Used* + a
  *Variant Details* table (`SKU/BARCODE`, `NAME`, `SELLING PRICE`, `PURCHASE PRICE` — no unit or
  conversion column) + *Recent Transactions*. No stock figures, no Secondary Unit tab, no Warehouse
  tab. A variant child's Overview has all three.
- **The "primary unit at rate 1" row is not special to variants.** An ordinary non-variant product
  (`HP Envy Laptop`, primary unit *Nos*) shows the same auto-seeded `Nos | 1 | 0 | 0` row. So the
  reference product materialises its primary unit as row 0 of `secondary_units[]`; this codebase
  keeps the primary unit on `Product.PrimaryUnitId` and leaves the collection genuinely empty. A
  presentation difference, not a model one, and it is why a variant's table here starts empty where
  the live one starts with one row.

**What this falsifies.** Nothing recorded — but it closes an allow-list entry whose stated reason
had become an argument for the opposite conclusion, which is phase 44's lesson arriving from a new
direction. Phase 24 wrote "attaching a secondary unit to a parent moves nothing and reconciles
against nothing" as a reason the handler needed no check; read once, that sentence says a parent
must be **refused**.

---

## Decision A — a variant owns its unit matrix, and that needed no schema

**Owned.** Settled by the read, not in advance.

The kickoff priced the two branches as "a read-through" versus "five more columns and a sweep", and
the actual answer cost neither, because phase 24's Decision A had already paid for it: a variant *is*
a `Product` row, `Product.SecondaryUnits` hangs off that row, and `Product.PrimaryUnitId` is that
row's own column. Ownership was already true; nothing was stored, read through or swept. This is the
same shape as phase 24's own finding that the FIFO ledger needed no change — an identity decision
made once keeps paying.

`Product.CreateVariant` copies `PrimaryUnitId` from the parent (the creation-time default the live
form expresses by having no unit field) and deliberately copies **no** secondary units, so a new
variant starts with an empty matrix. Both are asserted rather than reasoned about
(`SecondaryUnitLifecycleTests`).

**The phase-35a three-assertion rule did not apply**, and saying why matters: that rule is about
adding a *field* to many aggregates, where write, read and every prefill between are three separate
chances to drop it (14 of 15 detail DTOs dropped `LocationId`). Here no field was added. `GetProduct`
already `Include`s `SecondaryUnits`, the Angular `Product` interface already carried
`secondaryUnits`, and the detail page already rendered the table — for variants as much as for
anything else, because the Products list links every row, variant children included, to
`/products/:id`.

---

## Decision B — a variant parent is refused a unit matrix

The one real change the read forced. A parent holds no stock, so there is nothing for a conversion
rate to convert, and the reference product says so by giving a parent no Inventory Details panel.

Three layers, and the split is the standing one:

- `Product.AddSecondaryUnit` / `UpdateSecondaryUnit` throw `InvalidOperationException` — the Domain
  backstop.
- `ProductVariantRules.EnsureCarriesAUnitMatrix(name, hasVariants)` throws `ConflictException` — the
  409 that names the reason. **A Domain invariant reached through an endpoint is a 500, which tells
  a caller nothing** (phase-39), so the handler checks first and the Domain keeps the invariant.
- `ProductVariantSweepGuardTests`'s allow-list entry is **deleted**, so the guard now requires this
  handler to route through `ProductVariantRules` like the other nineteen — and the two new handlers
  are in scope from birth.

Proven live: `409 'T-Shirt' has variants, so it holds no stock of its own and has no units to
convert -- set the secondary units on each of its variants instead.`

**The delete is the one verb a parent keeps**, and getting that wrong was this phase's own bug —
see "The bug the browser pass found" below. The add and the update are refused; the delete is not,
because a product can hold secondary units and be promoted to a parent *afterwards*, and refusing
the delete as well would make those rows permanent.

### The bug the browser pass found

The first version of Decision B guarded **all three** verbs, and the status doc claimed "the Domain
method does not [guard], so a repair path exists if one is ever needed". Driving the UI showed that
sentence was false, and that phase 45 had created the problem it described.

`SetProductVariantAttributesCommand` promotes an ordinary product to a variant parent and does not
look at its secondary units — correctly, because the pool is the thing being set. So:

1. `Plain Mug` is created with two secondary units. Fine.
2. Its Attributes Used are set. It is now a parent, `200 OK`.
3. Its two rows are now stale — and the new `showsSecondaryUnits` gate **hid the whole card**, while
   `DeleteSecondaryUnitCommandHandler`'s guard returned `409` for the only verb that could clean them
   up. Reproduced against the real API:

   > `409 'Plain Mug' has variants, so it holds no stock of its own and has no units to convert`

   The rows were invisible *and* unremovable. Phase 45 made this worse than it found it: before, the
   card at least rendered, and there was no delete to refuse.

**The fix is that a delete is never the thing to refuse.** Removing a row that should not exist is
the repair, not the damage. So `DeleteSecondaryUnitCommandHandler` drops the guard (add and update
keep it), and the client shows the card whenever a parent still holds rows — read-only except
Delete, with the reason on screen ("This product now has variants, so it holds no stock of its own
and these units convert nothing").

**Why no test caught it.** Every handler test constructs the product in its *final* role — an
ordinary product, a parent, or a child — so none of them could reach "ordinary, given units, then
promoted". That is the ordering the UI makes natural and a test fixture never does. Both halves are
now pinned: `A_product_promoted_to_a_parent_can_still_delete_the_units_it_already_held` in the
handler tests, and a component test asserting the stale card shows Delete but neither Edit nor Add.

---

## Decision C — the unit matrix gains its other two verbs

Phase 3 shipped `AddSecondaryUnitCommand` and nothing else. The live table has an `Action` column per
row, and without an edit a mistyped conversion rate is permanent — a bad enough outcome on its own,
and worse now that a variant can carry its own.

| Added | Shape | Why |
|---|---|---|
| `UpdateSecondaryUnitCommand` | `(OrganizationId, ProductId, SecondaryUnitId, ConversionRate, SellingPrice, PurchasePrice)` | **`UnitId` is absent from the record, not ignored.** The unit is the row's identity — a product refuses two rows for one unit — so changing it is a delete and an add. A present-and-ignored field reads to a client as accepted (phase-43's `WorkspaceName`). |
| `DeleteSecondaryUnitCommand` | `(OrganizationId, ProductId, SecondaryUnitId)` | A **hard** delete, because nothing points at the row: every quantity in this codebase is stored in the product's primary unit and nothing persists "this line was entered in Boxes". That is exactly what makes it unlike `DeleteProductVariantCommand`, whose whole body is a reference check. |
| Two refusals on the add | primary-unit and duplicate-unit, both 409 | Neither existed. Two rows for "Box" at different rates is a product where nothing says which rate a document means. |

Both new commands ride `Catalog.Product.Manage` — the same key as the add, because editing a
product's unit matrix is editing the product. **No new permission key**, stated explicitly per the
kickoff.

The Angular panel gained Edit/Delete per row with an inline delete confirm, the unit select disabled
while editing (the command takes no new unit, so offering the control would be a value silently
dropped), and already-used units filtered out of the add picker.

---

## Decision D — the `ProductAttributePool` importer (the phase-38 carried item)

**An importer, per the revised kickoff, and the distinguishing fact is the file's shape.** Phase 38
declined an importer for the landed-cost grid because that grid is generated *from the document in
front of you* and its columns change whenever a tenant adds a cost term. A pool is a set of
`(AttributeId, OptionId)` pairs, so its file is a fixed three-column rectangle no tenant's data can
reshape. Quoting that as the distinction — rather than as cover for either answer — is what the
kickoff asked for.

| Column | Required | Notes |
|---|---|---|
| `Product Code` | ✔ | Must already exist; `ProductImporter` creates products. A variant *child* is refused here with its own message rather than left to the command's 409. |
| `Attribute` | ✔ | Resolved by name. **Ambiguity is a row error**: phase 24 read live that attribute names are deliberately not unique (the reference tenant carries both `size` and `Size`), so the index is non-unique and two rows can come back. |
| `Value` | ✔ | An option of that attribute, same treatment. |

**One row is one pair, and a row adds rather than replaces.**
`SetProductVariantAttributesCommand` replaces a pool wholesale, which is right for a form submitting
the whole set and wrong for a file arriving row by row: a replace per row would leave each product
holding only the last row that named it. Each row re-reads the product's current pool and sends the
union. Three consequences, all deliberate:

- Re-running a file is **idempotent**.
- An import can never **remove** an option, so it can never strand a variant built from one. Removal
  stays on the product's Variants tab, where the refusal that protects those children already lives.
- During the **dry run** nothing is written, so two rows naming one product each plan against the
  same starting pool. That is correct for a validation pass — it checks rows, it does not accumulate
  them — and `ImportJobProcessor` plans *and executes* each row in turn during apply, so the union
  really accumulates there. This is the same asymmetry `ImportRowContext.PendingKeys` exists for in
  the hierarchical importers, reached from the other side: there the dry run needed extra knowledge
  to avoid reporting a correct file as broken; here it needs none, because a row that would be a
  no-op still validates.

**Create-only**, and the word is about the pool entry rather than the product: a pair is present or
absent, so "Update Existing Records" would be a mode with nothing different to do. It joins the other
five in `CreateImportJobCommandValidator`.

**No new permission key.** It sends `SetProductVariantAttributesCommand`, which rides
`Catalog.Product.Manage`; `AuthorizationBehavior` re-checks it per row (phase-21a's Decision B paying
off for the ninth time).

On the Import screen it sits **immediately before** *Product Variant*, because that is the order they
must be run in.

---

## Decision E — the landed-cost drawer keeps replace, and the decision is a test

Phase 38 shipped the Import drawer replacing a product's rows rather than merging, and named the cost:
"a user who wants to add a second cost term to a product already in the grid by file must include the
existing amount too." The kickoff asked for a decision rather than a sentence.

**Keep replace.** The file is generated from the bill in front of you — one row per goods line, one
column per tenant cost term — so it is not a fragment naming a product, it is the whole matrix for
the products it names. A merge would make re-uploading a *corrected* spreadsheet silently additive:
the user who fixes Freight from 600 to 60 and uploads again gets 660, a number nobody notices until
it has reached the FIFO layers. A correction reading as an addition is worse than the stated cost of
restating an amount, and replace is what every other child-collection editor here does (phase-4 bug
#1's snapshot-and-`RemoveRange` idiom).

Pinned by two tests rather than left as prose, because **one half of "replace" is not obvious**: it is
scoped *per product*, not per grid. A row whose product the file never mentions survives, because the
file makes no claim about it.

---

## Decision F — the dry run stays outside a transaction

**No**, as recommended, and the reason is that phase 38 already built the thing that makes it
unnecessary. `PlanAsync`/`ExecuteAsync` means the dry run resolves every row exactly as the real run
will — same foreign-key lookups, same coercions, same validators — and the only failures it cannot
see are the ones only writing can produce: a uniqueness race, a lifecycle conflict. Running each row
inside a rolled-back transaction to catch those would mean holding a transaction open across an
entire file, re-doing every write twice, and still not catching a race (the row that wins between the
rollback and the real insert). **Confirm Upload's post-hoc row errors are the accepted shape**, and
they are already per-row, already name their column, and already leave the rest of the file imported.

---

## Decision G — `ListSmsLogsQuery` gets its search term

Phase 39 exempted it in `SearchSweepGuardTests` and named the re-entry condition: "a tenant sending
enough SMS that the last page stops being what they want." **Met by construction rather than by
observation** — a send to a contact group writes one row per recipient, so one `SendSmsCommand` over
a 200-contact group puts 200 rows on this list at once, and "bounded by construction" was the one
thing that was never true of it. The other six exemptions phase 39 uncovered stand unchanged, and the
guard's comment now says so.

The term matches the row's **own** columns — `Title`, `Content`, `PhoneNumber` — and not the joined
`ContactName`, following `ListDealsQuery` and `ListInvoicesQuery`: a search that silently joins on one
screen means something different from the same box on every other. `Content` is included because the
*resolved* text is what distinguishes two rows of one batch, which is the whole reason this list is
per-recipient. Asserted in both directions: a term matching only a contact name returns nothing.

It also turned out to have **no validator at all**, so `Page`/`PageSize` reached the handler
unbounded. That is phase 34b's finding repeating — asking one question of every paginated list is
worth more than the question, because it surfaces the lists nobody ever asked anything of.

---

## Angular tests — phase 44's carried item #6, closed

Phase 44 added four screens and no Angular tests; the count stayed at 447. The rule is to close the
habit at the first screen touched rather than widen it, so this phase added **15**: nine for the
product detail page's secondary-unit panel (including both directions of each visibility gate, and
the stale-parent case the browser pass uncovered), four for the SMS History search box, and two
pinning the landed-cost replace decision.

The two gates are worth the tests because a `computed()` deciding whether a whole card renders fails
silently in both directions: a panel that fails to hide is a 409 the user can only find by pressing
the button, and one that hides wrongly is a feature nobody can reach.

---

## What was verified, and how

### `dotnet ef migrations has-pending-model-changes`

> No changes have been made to the model since the last migration.

The phase's central claim in one line: ownership needed no schema.

### E2E 1 — the unit matrix (33 checks, 33 passed)

Fresh organization `Phase45 Units Ltd`, master data seeded by curl + cookie jar. Add / update /
delete on an ordinary product; both refusals; a `400` naming the field for a zero conversion rate (not
a Domain `500`); a parent promoted by setting its Attributes Used and then **refused** a secondary
unit; a variant child refused its own attribute pool.

The phase's own question, end to end: **`T-Shirt Blue` carries Box at 36 and `T-Shirt Red` carries
Box at 6** — the same unit of measurement, two conversion rates, on two siblings of one parent.

### The negative path

A custom role holding **no grants**, the acting user demoted into it *after* a `201` from the same
user on the same organization in the same run, then all three verbs against a **nonexistent** product
id:

> `403 You do not have permission to perform this action (Catalog.Product.Manage).`

403 and not 404 is what proves `AuthorizationBehavior` fired before the handler.

### `sqlcmd` — a predicate true only of the right write

```
Role      Name          Unit    ConversionRate  SellingPrice  PurchasePrice
ORDINARY  Plain Mug     Box          24.000000      2400.0000      1920.0000
ORDINARY  Plain Mug     Carton       48.000000      4800.0000      3840.0000
PARENT    T-Shirt       NULL              NULL           NULL           NULL
VARIANT   T-Shirt Blue  Box          36.000000     36000.0000     28800.0000
VARIANT   T-Shirt Red   Box           6.000000      6000.0000      4800.0000

Verdict: OWNED  (siblings diverge on one unit; the parent holds no matrix)
```

The predicate asserts **two distinct conversion rates across exactly one distinct unit** among the
parent's children, **and zero rows on the parent** — all three of which are false under an inherited
model and under a per-parent one.

### E2E 2 — the importer (19 checks, 19 passed)

The fixture is built by **filling the app's own generated template** (`GET
/import-templates/ProductAttributePool` → openpyxl → upload), never hand-rolled, per phase-21a's
ClosedXML gotcha; the upload leg is driven from Python's `urllib`, because curl cannot read a file
for `-F` here (phase-30).

Five rows over two products, one deliberately naming a product code that does not exist:

```
status=Completed succeeded=4 failed=1
T-Shirt: hasVariants=True attributes=2 options=3  [('Colour', ['Blue', 'Red']), ('Size', ['Large'])]
Hoodie:  hasVariants=True attributes=1 options=1  [('Colour', ['Blue'])]
row 6: column='Product Code' message="No product with code 'Nonexistent-Code' exists in this organization."
```

```
Verdict: ACCUMULATED  (3 pairs over 2 attributes on one product; the other kept its own 1; both promoted)
```

Three rows became three pairs on one product. **A per-row replace would have left T-Shirt holding
exactly one**, which is precisely what the predicate rejects.

### The browser pass

Driven against the real API and the SSL dev server, with curl's `erp_auth` cookie transplanted via
`document.cookie` (phase-25 Step 3) rather than typing a password into the browser.

| Screen | What was exercised | Result |
|---|---|---|
| Variant child (`T-Shirt Blue`) | Secondary Units card | Present, subtitled "This variant's own units, independent of its parent and of its siblings", showing Box 36 with Edit/Delete. **No variant panel.** |
| Variant parent (`T-Shirt`) | The inverse | Variants panel with Attributes Used chips and both children. **No Secondary Units card.** Exactly complementary to the child, as live. |
| Ordinary product (`Plain Mug`) | Both | Secondary Units (two rows) *and* the "Set Up Variants" panel — an ordinary product can still become a parent. |
| Edit | A real edit through the form | Unit select **disabled** showing `Box`, other three fields pre-filled; changing 12 → 24 saved and re-rendered. |
| Delete | Cancel, then confirm | "Remove Carton?" inline; Cancel left both rows; Remove deleted exactly one. |
| Add | The picker | Offered **only `Carton`** — `Piece` (primary) and `Box` (already used) both withheld, matching the two 409s. |
| SMS History | The new search box | Placeholder "Search title, message or number…"; typing `Dashain` changed the empty state from "No SMS sent yet." to "No SMS matches that search.", and the request carried `…&search=Dashain → 200`. The unsearched call carried no `search` parameter. |
| Import / Export | The new upload type | "Product Attributes Used" present immediately before "Product Variant"; selecting it **disabled** the mode select, disabled the "Update Existing Records" option and showed "This list can only be created by import, never updated." |

Two things the pass changed rather than confirmed:

- **The stale-parent bug above**, which no test could reach.
- **The template button named the enum, not the label.** It bound `{{ entityType() }}`, so it read
  "Download ProductAttributePool Template" — and, pre-existing since phase 38, "Download
  ContactPersonnel Template". The labels were already in `entityTypes`; the button simply was not
  using them. One `entityTypeLabel()` computed fixes all nine.

One thing checked and found **correct**, worth recording so it is not re-investigated: the export
date fields render as `mm/dd/yyyy` native date inputs, which looks like a violation of phase 23's
rule. Every one of them is inside `app-bs-date-input` — the shared control *is* the sanctioned
wrapper, and `sweep-guard.spec.ts` forbids native date inputs in page templates, not inside it.

---

## Review pass — six defects the phase did not go looking for

Everything below was found by a person using the app after the phase was otherwise complete, and
none of it was reachable by any test in the suite. They are recorded here rather than deferred
because each one is small, and because together they make a point worth keeping: **the defects a
test suite cannot see are the ones about a control's relationship to its surroundings** — what it
sits inside, what it says, and whether the data behind it can exist at all.

| # | What was wrong | Why no test saw it |
|---|---|---|
| 1 | A product given secondary units and **promoted to a parent afterwards** held rows that were invisible and unremovable (see "The bug the browser pass found"). | Every fixture builds a product in its final role. |
| 2 | **Clicking Select Status on a document list opened the document.** | The bug is the control's relationship to the anchor it is nested in; the component in isolation behaved correctly. |
| 3 | **Custom Statuses had no management screen at all**, so the picker phase 20b shipped on four grids was empty on every tenant. | Nothing asserts that a control's options can be created. The API had full CRUD; only the screen was missing. |
| 4 | **Reporting Tags opened a blank multi-select** on a tenant with none defined — no options, no explanation, and a Save button that would have saved an empty set. | Same shape as #3: the empty state is invisible to a test that seeds data. |
| 5 | The **Product detail page showed an Active badge with no control**, so a product could never be deactivated; the client read `isActive` and handed it straight back. | `UpdateProductCommand` accepted the field, so every server test passed. 19 sibling screens have the control; Product had neither it nor Contact's Deactivate action. |
| 6 | The import **Download button named the enum** — "Download ProductAttributePool Template", and pre-existing since phase 38, "Download ContactPersonnel Template". | Cosmetic, and the label list it should have used was already there. |

**#2 is the one with a general lesson.** The picker already called `stopPropagation()`, and that was
not merely insufficient — **it was the cause**. Stopping propagation prevents `RouterLink`'s own
click listener from running, and that listener is the thing that calls `preventDefault()` before
navigating in-app; suppressing it left the browser's *native* anchor navigation as the only
behaviour. The fix needs both halves, and they are not symmetric: `click` prevents the default,
`mousedown` must **not**, because a native `<select>` opens its popup as the default action of
mousedown. Both directions are asserted so neither can be tidied into the other. A sweep found
exactly four anchors in the app containing an interactive control, all four the same component, so
one fix covers every instance — verified on two of the four grids in the browser.

**#3 and #4 are the same defect twice**, and the pattern they establish is worth copying: a control
whose options a tenant must define first should **say what it is and link to where it is defined**,
never render empty. A blank control reads as a broken one — which is exactly how both were reported.

**One thing checked and found correct**, recorded so it is not re-investigated: the export date
fields render as native `mm/dd/yyyy` inputs, which looks like a violation of phase 23's rule. Every
one is inside `app-bs-date-input` — the shared control *is* the sanctioned wrapper, and
`sweep-guard.spec.ts` forbids native date inputs in page templates, not inside it.

**And one wording change with no behaviour attached.** The shell banner read "Contact your Tigg
representative to choose a plan" directly above a screen where the reader chooses one themselves.
The copy was inherited from the reference product, where a vendor rep does the selling; this codebase
models exactly one actor (phase-41 Decision G). The banner, the Usage panel and the Record panel now
name the screen instead of a person, and the Record panel leads with "This records an agreement; it
does not take payment." `subscription-notice.spec.ts` asserts in both directions that no banner names
an external actor — so if a vendor-side actor is ever built, the copy fails loudly rather than
quietly becoming true again.

---

## Known limitations / follow-ups

1. **The multi-select on the Attributes Used editor was not built.** The revised kickoff called it a
   one-parent ergonomic win and the importer the thing that closes the recorded gap; the importer
   shipped. The editor still lists every option of every tenant attribute as an individual checkbox,
   with no per-attribute *Select all* and no filter box — tolerable at the reference tenant's 16
   attributes, and the first tenant with 60 will feel it. Re-entry: a tenant whose attribute
   catalogue outgrows one screen.
2. **A secondary unit is priced metadata and nothing consumes it.** No document line stores a unit,
   so a conversion rate converts nothing yet — it is catalog data a user maintains and reads. That
   was true before this phase and is why the delete can be hard; it is also the reason multi-UOM is
   not *done* in any larger sense. Re-entry: a document line that carries a unit, which is a change
   to every line-bearing aggregate and its own phase.
3. **`RemoveSecondaryUnit` is unguarded in the Domain on purpose** (Decision B), so a parent that
   acquired a row before this phase can still have it removed. No tenant is known to have one; the
   path exists rather than being needed.
4. **The importer's dry run cannot see cross-row accumulation**, by construction (Decision D). A file
   whose rows are individually valid validates clean and is applied correctly; there is no case where
   this misleads, but the review screen's `Action` text for the second row naming a product reads as
   if the first had not happened.
5. **Carried from 44 and untouched here**: nothing. **From 41**: the metered add-on axes, the
   plan-change/entitlement mismatch, the quota race and expired-tenant behaviour are phase 46's.
   **From 40**: the NVDA session, `aria-describedby` per field, `Sort by` across the 18 qualifying
   lists and lookup/server search parity are phase 47's.
