# Phase 43 — Aggregate completions, and the reversal the fix would have broken

## TL;DR

Six carried items where a thing existed on one side of a boundary and not the other: in the Domain
but not in a command, in a list DTO but not a detail one, on one document family but not its twin.
No new mechanisms — every item reuses a pattern this codebase already has, and the phase's own two
findings both came from checking the *other* side of something it had just changed.

1. **`UpdateOrganizationCommand`** (39 #2). The aggregate had been create-only since phase 1b, so
   eight fields printed on every customer-facing PDF had no command that wrote them. One command,
   one form, one reused key. Three fields are deliberately unreachable and each says why.
2. **Deal and Task detail pages** (39 #1). `Deal` and `WorkTask` join the polymorphic parent enums —
   **three for Deal, two for WorkTask**, because a task does not parent tasks. Phase-27a's sweep
   followed exactly: bridged by name, guarded in both directions, no second mechanism.
3. **`TrialStartsAt`/`TrialEndsAt` renamed** (41 #2) → `OriginatedAt` / `TermEndsAt`, in one scripted
   edit with anchor counts asserted before writing. **`dotnet ef` scaffolded the two renames paired
   by column order and swapped them**; unnoticed, every tenant's origin would have become its term
   end and every live tenant would have read as expired.
4. **Product-to-location enforcement: retired, with evidence** (36 #1). The confirm-live gate was run
   on Cadehi and the reference product **does not refuse it** — an invoice raised at HeadOffice with a
   POS-Retail-only product on the line both saved and approved. Nothing was built.
5. **A Payment's currency control on Quick Payment/Receipt** (36 #6), and **the Sales Register folded
   to base currency** (36 #5) — a reporting-semantics decision, taken to the edge of the shared
   reader and no further, with the rest of the statutory family named rather than half-done.
6. **The standalone Debit Note's Goods line** (37 #1) gets a `WarehouseId`, so a Goods return consumes
   from somewhere instead of crediting the Inventory account while the FIFO ledger never moves.

**The finding that mattered most is item 6's second half.** Closing the approve-side gap created a
worse one on the void side, which still keyed its restock on the presence of a source bill: stock
would have gone out on Approve and never come back on Void. Only the E2E saw it — every handler test
in the suite exercised the converted path. That is phase-6 bug #3 and phase 29's restatement of it
(*changing what a document does to the ledger changes what its reversal owes*) arriving through the
**stock** ledger rather than the general one.

---

## The confirm-live gate: product-to-location is a picker filter, not a rule

Phase 36 Decision I proved by experiment that a product scoped to one location vanishes from another
location's line picker, **server-side** — and deliberately did not build enforcement at save, because
"whether the reference product also refuses a line naming an out-of-location product at Approve is
unobserved". Phase 43's brief made answering that a decision gate.

**Read on Cadehi, 2026-09-14.** The tenant still carries phase 36's `Location Probe Product`
(`P00001`), and its Edit dialog still shows `Location: 1 Selected` — **POS Retail** checked,
HeadOffice and POS Restaurant not.

| step | result |
|---|---|
| `#/sales/invoices/add`, header location **HeadOffice**, open the line picker | **No data** — phase 36's finding reproduced |
| switch the header to **POS Retail**, open the picker | the product is offered immediately |
| add it at 100, then **switch the header back to HeadOffice** | the line **stays on the form**; nothing is cleared or flagged |
| **Save** | `Transaction Complete`. Draft saved, `Billing Location: HeadOffice (HO)` |
| **Approve** | **Approved**, numbered `INV0001HO\|83-84` |

So the reference product raises, saves *and* posts an invoice at HeadOffice whose only line names a
product restricted to POS Retail. The document even draws its number from the **HeadOffice** pool,
which independently confirms the header location was genuinely HeadOffice and not a display artifact.

**Decision: the enforcement idea is retired.** Product-to-location restricts *what the picker offers
at a location*, and nothing else. Building a save-time or approve-time refusal would have been
shipping a rule the reference product does not have, and — as phase 36 already warned — one that could
invalidate documents a tenant has legitimately saved.

Phase 32b's lesson applies in the pleasant direction this time: a recorded inference about a control
nobody operated is not settled, and operating it settled it. The inference happened to be right (35b
read two Cadehi writes that "suggest it does not"), but it was right the way a guess is right.

---

## Scope decisions

### A. `UpdateOrganizationCommand` takes eight fields, and refuses three by name

The live `EDIT DETAILS` dialog edits nine; this codebase's nine are not quite the same nine (it has
`Industry` where live has `Display Name`, and it has `WorkspaceName`, which live has no counterpart
for). What ships is editable, with a reason recorded for everything that is not:

| field | editable | why |
|---|---|---|
| Name, Industry, Address, Email, Phone, PAN, Website | **yes** | Descriptive; every one of them prints on a document header, and a typo in any is a customer-visible defect with no other way to fix it. |
| IsVatRegistered | **yes** | Stored and read only through the profile. A registration status genuinely changes. |
| AccountingStartDate | **yes, with a guard** | The live dialog edits it, and a cutover date typed once in a wizard is exactly what gets typed wrong. But see below. |
| WorkspaceName | **no** | A login-adjacent unique slug, normalised and uniquely indexed, that *addresses* this tenant. Changing it silently breaks every bookmarked workspace URL. The live dialog has no counterpart field. It is **absent** from the request record, not merely unedited, so a client sending one gets no impression it was accepted. |
| Entitlement flags | **no** | Phase 20f/41 Decision F, unchanged: a tenant with stock in a FIFO ledger must not be able to turn inventory off underneath it. |
| LockDate | **no** | Keeps `SetOrganizationLockDateCommand` and its own Admin-only key. It is a ledger control, not a detail on a letterhead — and phase 16a's reason for that key (a Member who could move it could reopen the backdated-write window) is not a reason to fold it into a profile form. |

**The accounting-start-date guard.** `CreateOrUpdateOpeningStockLineCommandHandler` stamps the
organization's `AccountingStartDate` onto the FIFO layer and the `StockMovement` row it writes, and
nothing ever restates them. Moving the date afterwards leaves the layers dated to the old day zero
while every report cutting off at the new one disagrees with them — phase-37's "a stock value has to
reach all three views or two of them drift", arriving through the *date* instead of the amount. So
the command refuses a **changed** start date once any opening-stock line exists, with a 409 that says
what to do, and leaves every other field editable. Refusing the whole command would have made the
screen useless for exactly the tenants most likely to need it; a test asserts both halves.

**No new permission key.** `Tenancy.Organization.ProfileManage` already exists and is already
Admin-only, derived by phase 39 for this screen and this bar. The logo write on the same form uses
it. Two keys on one form would be two ways to be denied on it.

### B. Deal joins three parent enums; WorkTask joins two

Phase 39's live pass (2026-09-13) recorded the two detail pages precisely, and the record is the spec:

| | tabs |
|---|---|
| **Deal** | Overview / Contact Personnel / Tasks, plus a **Documents** dropzone and an **Activity** composer |
| **Task** | Overview / **Documents** / **Activity** (Comments / Activities / Emails), plus `MARK AS DONE` |

So `Deal` joins `TaskParentType`, `AttachmentParentType` and `CommentParentType`; `WorkTask` joins the
latter two and **not** `TaskParentType`, because a task does not parent tasks. The brief said "the
three polymorphic parent enums" for both; the live record says otherwise, and the asymmetry is
asserted in both directions (`A_deal_parents_tasks_and_a_task_does_not`) rather than left looking
like a member somebody forgot — phase 30's rule that a list sampled from a few screens becomes a
wrong list, and that the fix is to find the rule and guard it both ways.

**They are records, not documents, and the whole sweep falls out of that.** Both gained a
`DocumentType` member — the audit feed keys on `(DocumentType, DocumentId)` and a record with an
Activity tab needs a name in that vocabulary — but both are classified in
`DocumentMechanisms.NotApplicableReasons`, so `DocumentParentTypes.TryToDocumentType` returns null for
them exactly as it does for `Contact`. That single property is what routes them to their own keys
through `ParentPermissions` with no special-casing anywhere else.

- `ParentPermissions.ContactOrThrow` became `RecordOrThrow`: Contact keeps its pre-split
  `Contact.View`/`Contact.Manage`, Deal resolves `Crm.Deal.*` and WorkTask `Workflow.Task.*`. A test
  asserts all three are **distinct**, which is the assertion that fails if one falls through to
  another's key — the direction that leaks (a Member holding only `ContactView` reading files on a
  Deal they cannot open).
- `Workflow.Task.*` keeps phase 13's blanket treatment for tasks *as tasks*. What phase 43 adds is a
  file or comment **on** a task, which is `ParentPermissions`' business. Worth stating, because
  "WorkTask is now a parent name" reads like the two had merged.
- **A third route family** (`RecordTabsEndpoints`), not a widened `{documentType}` segment.
  `DocumentTabsEndpoints` binds that segment to `DocumentType` and resolves through
  `DocumentParentTypes.For<T>()`, which answers only for the 15 transactional types; relaxing it
  would have given up the property that makes an unroutable type a 404 from routing. The two record
  families are one parameterised helper, not two copied blocks.
- **No migration.** All three parent-type columns and `Audit.DocumentType` are
  `HasConversion<string>()`, so new enum members are new values in an existing column.

**The detail pages read their record through the list query's new `id` filter**, not through a
`GetDealQuery`/`GetTaskQuery` of their own. A deal row is assembled from a contact name, a lead-source
name, a stage name and colour and one user name per assignee; a second query would be a second copy
of that assembly (phase-26b's shared-reader rule). It also inherits the **private-deal visibility
rule**, which `ListDealsQueryHandler` enforces itself rather than through the permission key — so a
deal the caller may not see is a not-found on the detail page for free, rather than something a new
handler could forget. The filter is composed as its own `.Where()`, per the standing rule.

### C. `OriginatedAt` and `TermEndsAt` — and one of them is not a term date

Phase 41 carried this as "`TrialStartsAt`/`TrialEndsAt` are now misnamed — they describe any term,
trial or paid", and the brief said to rename both "to term dates". Only one of them is a term date:

- `TrialEndsAt` → **`TermEndsAt`**. It has described paid terms since phase 41 recorded one, and a
  column whose name says *trial* is a standing invitation to reason about it as one.
- `TrialStartsAt` → **`OriginatedAt`**, deliberately *not* a term date. Phase 41 Decision E is
  explicit that it records when the tenant began and that **a renewal does not move it** — which is
  precisely what `TermStartsAt` is. Calling it a term date would have made three columns look like a
  set of three when it is a set of one plus a pair. (A tenant that signed straight onto a paid plan
  never had a trial at all, which is the milder half of the same misnomer.)

Neither rename changed what `SubscriptionUsageReader` or `SubscriptionQuotaBehavior` computes: the
window is still `TermStartsAt`..`TermEndsAt`, and `SubscriptionExpiryBehavior` still reads the end.
15 files, 4 spellings, anchor counts asserted **before** anything was written — the first run refused
and named four files whose counts were wrong, which is what the assertion is for. Migration
`.Designer.cs` snapshots are excluded on purpose: they record the model as it was.

### D. The Sales Register folds to base currency, and stops at the shared reader

A statutory register filed with the IRD is denominated in NPR, so its money columns are NPR. A
foreign invoice used to contribute its own 100 beside a domestic invoice's 100, with nothing on the
page saying the two numbers were not the same kind of thing.

**Folded per line, before the bucketing, never on the finished buckets.** That is phase-28's
posting-rule rule arriving in a report: `Total` is derived as a sum of the other three magnitudes, so
converting the buckets independently would let `Total` differ from `TaxExempt + Taxable + VAT` by a
paisa on some rates — and the footer, the page and the per-line view would each round it differently.
Converting the inputs keeps every view summing to the same number by construction. In **memory**,
after `ToListAsync`, because `ExchangeRates.ToBase` is a static call: inside the query it is
untranslatable on SQL Server and silently evaluated in C# by InMemory, so every handler test would
pass while the endpoint 500s.

**It also folds inside `SalesReturnReader`, and that boundary is the decision.** Both statutory
registers that show a credit note read that reader, and phase 36 built it precisely so the two cannot
disagree about the same note. Folding only in the Sales Register would have made it report a foreign
return in rupees while the Sales Return Register reported the same return in dollars — a regression,
not a partial fix. So the fold goes to the edge of the shared reader and stops.

**What is deliberately *not* folded, named rather than left to be rediscovered.** The rule selects
more than the item did. The **Purchase Register**, the **Purchase Return Register** (via
`PurchaseReturnReader`), the **VAT Summary** (4 bucketing sites) and **Annex 13** (5, several with
their own line-matching back through the source bill) all read transaction-currency line amounts the
same way. That is a report-semantics phase, not a line item in an aggregate-completions one — and
phase 44 is a report-semantics phase. Carried, with the list.

### E. Quick Payment/Receipt gets the currency control, and no new rule

The two Payment detail forms have carried `app-currency-rate-fields` since phase 28; these two
screens did not, so a receipt in dollars could only be recorded as though it were rupees. Not a
display gap: the amount folds at the document's rate when it posts, so an unstated rate of 1 books a
foreign receipt to the ledger at its face value in the wrong currency.

There is **no cross-currency question to answer**, which is why this is a control and not a rule.
Phase 36's "a settlement folds at the rate of what it settles" governs *allocation*, and a Quick
Payment allocates to nothing (`allocations: []`) by construction — that is what makes it quick. The
remainder folds at the payment's own rate, which is the branch phase 36 already built.

### F. `DebitNote.WarehouseId` — one rule, and the premise that turned out to be wrong

The brief recommended a warehouse on the aggregate "because that is what the Credit Note already does
on the sales side". **The Credit Note does not do that**: it has no `WarehouseId` either, and a
standalone Credit Note skips the stock ledger entirely. The real asymmetry is narrower and worse:

- A **standalone Credit Note** posts no Inventory leg at all (`resolveInventoryAccounts:
  sourceInvoice is not null`), so its GL and its ledger agree — both untouched.
- A **standalone Debit Note** credits the Inventory *account* unconditionally, because a Goods line
  resolves through `PurchaseBillAccountResolver` (post-phase-19), while the FIFO ledger never moved.
  **The two views disagreed**, which is what made this a bug rather than a modelling gap.

So the warehouse goes on `DebitNote`, and Approve reads **the note's own** warehouse whether or not
there is a source bill — one rule, not a branch. A conversion pre-fills it from the source bill
(server-side, so a client that predates this phase is unchanged), and a migration backfills existing
rows the same way, which makes the new one-rule path produce exactly what the old two-branch path
produced.

- **Nullable**, because a Service-only note has no goods to move and requiring a warehouse there
  would be requiring a fact that does not exist.
- **No Domain backstop**, and that is a deliberate departure from phase-39's "keep the Domain check
  as the backstop". Whether a line is Goods is a fact about `Product`, an aggregate `DebitNote`
  cannot see, so a Domain guard could only restate the caller's own claim.
  `PurchasingValidation.EnsureDebitNoteWarehouseForGoodsAsync` holds it on the way in (a **400 naming
  the field**) and the approve handler holds it again on the way out (a **409**, for the rows that
  predate this phase).
- The field reaches the **detail DTO** and the **conversion template** in the same commit as the
  command that writes it — phase-35a's rule that a new field owes three assertions, write, read and
  every prefill between.

---

## Bugs and traps hit

**1. `dotnet ef` scaffolded the two column renames paired by ordinal position, and swapped them.**
The generated migration emitted `TrialStartsAt → TermEndsAt` and `TrialEndsAt → OriginatedAt`. Applied
as written, every existing row's origin would have become its term end and vice versa.
`SubscriptionExpiryBehavior` reads the end on every request, so **every live tenant would have read as
expired** — and nothing in the model would ever have said so, because the model only sees names. EF
cannot infer a rename; when a diff contains two of them on one table it pairs them positionally.
Hand-corrected in both `Up` and `Down`, with the reasoning in the migration file.

The verification that catches this class of error is not "do the columns exist" but a statement that
is true of the data only if the pairing was right: `SELECT COUNT(*) ... WHERE OriginatedAt >=
TermEndsAt` returns **0**. Had the swap shipped, every row would have violated it.

**2. Closing the Debit Note approve gap opened a worse one on Void.**
`VoidDebitNoteCommandHandler` looked its warehouse up from the source Purchase Bill and was therefore
null for a standalone note — harmless only while Approve consumed nothing for a standalone note
either. After item F, stock went **out** on Approve and never came back on Void. The E2E caught it:
after the void the Inventory account was restored to 6,000 while the ledger still said 6 units. Fixed
by reading `debitNote.WarehouseId`, the same value Approve consumes at.

**No handler test would have found this**, and that is the point: every test in the suite exercised
the converted path, where the old lookup worked.

**3. A guard assertion that was simply wrong.** The extended ordinal test asserted
`TryFor<AttachmentParentType>(DocumentType.Contact)` is null. It is not — `Contact` is a member of
both vocabularies, so the bridge parses it and then refuses it *downstream* because it is not
transactional. Replaced with the assertion that actually holds and actually matters
(`TryToDocumentType(AttachmentParentType.Contact)` is null), which is the property routing all three
record parents to their own keys.

**4. Phase 37's pinned test was guarding the bug.**
`A_standalone_return_posts_unchanged_because_it_relieves_nothing` asserted 2,400 credited to Inventory
with no stock consumed — a fact, not a requirement, and exactly the divergence phase 37 named in its
own carried items. Replaced with `A_standalone_return_consumes_from_its_own_warehouse`, which asserts
the same 2,400 *and* `StockConservation.AssertHoldsAsync`, which the old behaviour could not have
passed. Phase 37's own lesson, applied to phase 37's own test.

**5. The heredoc ate a backslash escape, again.** A `cat > file <<'PY'` containing `phase-39\'s`
inside a single-quoted Python string died with `unexpected EOF`. CLAUDE.md warns about this; the fix
is the Write tool, which is what the rest of the phase's scripts used.

**6. Two anchor-count refusals and one ambiguous anchor, all caught before writing.** The rename
script refused on its first run and named four files whose counts were wrong (it counts all four
spellings; `grep` had missed two lowercase locals). A later script refused because
`Guid? LocationId = null);` appears three times in `PurchasingEndpoints.cs` — re-anchored on the
`DebitNoteRequest` record itself. This is the assertion earning its keep twice in one session.

**7. Seeding traps, for the next E2E.** `POST /units-of-measurement` needs **`shortName`** as well as
`name` (a 400 naming `ShortName`). `PUT /accounting-defaults` takes
**`defaultAccountsReceivableId`** and **`defaultAccountsPayableId`** — no `Account` in the middle,
unlike every neighbouring field — and a wrong name is a **200** that writes nothing, which surfaces
three steps later as `Default Accounts Payable account is not configured` on approve.
`ListAttachmentsQuery` returns **`rows`**, not `items`. And `MozillaCookieJar.load` silently yields no
cookie from a curl jar containing `#HttpOnly_` lines — the resulting 401 reads exactly like an auth
failure; pull the token out by hand and send a `Cookie` header.

---

## What the E2E proved

A fresh organization (`phase43-…`), master data seeded through the API with curl and a cookie jar,
every status code printed.

**Item A — the Organization update.** `PUT /profile` → 200, and `GET /profile` reads back all eight
changed fields (`Phase43 Trading Pvt Ltd`, `Wholesale`, `Naxal, Kathmandu`, `updated@example.com`,
`01-7777777`, `309999999`, `updated.example.com`, `isVatRegistered: false`) with
**`workspaceName` unchanged**.

**Item B — the two record parents.**

| | result |
|---|---|
| `POST /deals` | 201 |
| `POST /tasks` with `parentType=Deal` | 201 — `TaskParentType.Deal` |
| `POST /deals/{id}/comments`, `POST /tasks/{id}/comments` | 201, 201 |
| `POST /deals/{id}/attachments`, `POST /tasks/{id}/attachments` | 201, 201; read back as `deal-doc.txt` and `task-doc.txt` |
| `GET /deals/{id}/activities`, `GET /tasks/{id}/activities` | 200, **count 1 each** — the new `IAuditableRequest` markers really write |
| `GET /deals?id=`, `GET /tasks?id=` | 200, 1 row each — how the detail pages read |

**Item F — the standalone Debit Note, and the three views at every step.** A purchase bill of 10
Widgets at 600, then a standalone Goods debit note for 3 with a warehouse on the note itself:

| | FIFO layers | movements (net) | Inventory GL |
|---|---|---|---|
| after receipt | 10 | 10 | 6,000 |
| after the note is approved | **3** | **3** | **4,200** |
| after the note is voided | **6** | **6** | **6,000** |

(The 6 rather than 10 at the end is the *earlier* note in the same tenant, approved and voided
against the pre-fix code — its 4 units left and never came back. That residue is the bug, preserved
by accident and worth keeping in the record.) The note's own movements read `Out 3 @ 600.0000` then
`In 3 @ 600.0000`.

Beside it: `POST /debit-notes` with a Goods line and **no** warehouse → **400** naming
`WarehouseId`; with one → 201, and `GET /debit-notes/{id}` returns `warehouseId` in the detail DTO; a
**Service-only** note with no warehouse → 201.

**The negative paths.** A custom role holding exactly one grant (`Crm.Deal.View`), a second
registered user in it (verification code read from `[identity].VerificationCodes`), logged in
**before** accepting the invitation:

| probe (every id nonexistent) | code | names |
|---|---|---|
| `GET /deals` — the 200 that makes the rest mean something | **200** | — |
| `PUT /profile` | **403** | `Tenancy.Organization.ProfileManage` |
| `POST /deals/{missing}/comments` | **403** | `Crm.Deal.Manage` |
| `POST /tasks/{missing}/comments` | **403** | `Workflow.Task.Manage` |
| `GET /tasks/{missing}/attachments` | **403** | `Workflow.Task.View` |
| `POST /debit-notes` | **403** | `Purchasing.DebitNote.Create` |
| the same two ids **as Admin** | **404** | — |

The last row is the strongest part: the same requests against the same missing ids give 404 to a
caller who holds the key, so each 403 was authorization firing *before* the handler looked anything
up, not a not-found in disguise.

**`sqlcmd` on the schema.** `tenancy.TenantSubscriptions` now has `OriginatedAt`, `TermStartsAt`,
`TermEndsAt` and neither old name; `OriginatedAt >= TermEndsAt` on **0** rows.
`purchasing.DebitNotes.WarehouseId` is `uniqueidentifier NULL`, and the backfill left **13**
PurchaseBill-referred notes with a warehouse and **8** standalone notes without one.

**The guard proved to bite.** `DocumentParentTypes` was backed up, an ordinal cast injected in place
of the by-name bridge, and `DocumentMechanismSweepGuardTests` failed **5 of 31** — including the
extended `Enum_mapping_is_by_name_and_not_by_ordinal`. Restored **by hash** (`sha256sum -c`), never
`git checkout --`, and re-run green.

---

## Tests

| Suite | Before | After | Added |
|---|---|---|---|
| Domain.UnitTests | 660 | **666** | `OrganizationTests` +6 — every editable field, the three that survive an update, and the two refusals |
| Application.UnitTests | 1095* | **1103** | `UpdateOrganizationTests` (6) — the read-back through the query the screen uses, the workspace name, the not-found, and both halves of the opening-stock guard; `SalesRegisterQueryHandlerTests` +2 — the base-currency fold on both sides, with `Total == TaxExempt + Taxable + VAT` asserted per row and in the footer; `DocumentMechanismSweepGuardTests` +2 — the Deal/WorkTask asymmetry both ways, and that the three record parents resolve three distinct keys; `PurchaseBillLandedCostTests` +2 net (one replaced, three added) |
| Api.IntegrationTests | 29 | **29** | — |
| Angular | 443 | **447** | `tab-parent.spec.ts` (4) — a distinct endpoint family per parent kind, the ids, and SMS history staying Contact-only |

\* 1091 at the end of phase 42, plus the 4 this phase added before the count was next taken.

`dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. `Api.IntegrationTests` failed 2 of
29 on the first run under load and passed 29 of 29 on re-run — the known Testcontainers
nondeterminism (phase 36/37), not a regression. `ng build` reports 651.81 kB against phase 42's
measured 680 kB budget.

All prior guards green: `a11y-sweep-guard`, `sweep-guard`, `SearchSweepGuardTests`, 32b's location
sweep guard, 35a's `LocationReadPathSweepGuardTests`, 35b's two, 36's
`product-picker-location-sweep-guard.spec.ts`, 34b's nav guard, 39's
`RichTextWritePathSweepGuardTests`, 41's `MeteredTransactionSweepGuardTests`, 42's
`build-budget.spec.ts`.

---

## Known limitations and follow-ups

1. **Three more statutory reports and one annexure still report transaction currency** (Decision D):
   the Purchase Register and Purchase Return Register (via `PurchaseReturnReader`), the VAT Summary
   (4 bucketing sites) and Annex 13 (5, several resolving `ExpenditureClassification` back through
   the source bill). The rule is decided and written down; applying it is report-semantics work, and
   phase 44 is that phase.
2. **Product-to-location is a picker filter and always will be** (the confirm-live finding). Recorded
   as settled rather than carried — the re-entry condition would be the reference product growing an
   enforcement it does not have.
3. **A standalone Credit Note still puts no stock back**, and unlike the Debit Note this is not a
   divergence: its GL posts no Inventory leg either, so both views agree. Giving the sales side a
   `WarehouseId` too would be a modelling decision about what a standalone sales return *means*, not
   a fix. Named here because the two sides now look asymmetric and the asymmetry is deliberate.
4. **Neither record detail page has an edit form.** Both read their record and host the tabs; editing
   a Deal or a Task still happens through the inline form on its list, which is where it has been
   since phases 15 and 13. The live pages have a left-rail edit; wiring it is small and was not this
   phase's item.
5. **Deal and Task carry no Custom Fields, Custom Status or Reporting Tags.** They are records, not
   documents, and `DocumentMechanisms` classifies them accordingly — the 27a sweeps are keyed to
   transactional types. Only revisit if the live record pages show those controls, which the
   2026-09-13 pass did not.
6. **`activity-panel.html` renders comment timestamps as raw ISO strings** (`{{ row.createdAt }}`
   on two lines), so a comment reads `2026-09-14T14:00:21.0991469+00:00`. Pre-existing — that
   component has served the Contact tab since phase 18 and every document tab since 27a — but
   phase 43 put it on two more screens, and phase-23's rule says a date a user reads goes through
   `NepaliDatePipe`. Not fixed here because choosing the format (BS date alone, or date plus time)
   is a decision, and changing a component with 17 hosts at the end of a phase is not the moment.
7. **`MARK AS DONE` is not on the Task detail page.** The live page has it; the status change is
   already reachable from the list, so this is a convenience rather than an unreachable field.
