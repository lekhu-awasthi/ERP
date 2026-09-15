# Phase 47 — the accessibility completion, and the one item that still needs a person

## TL;DR

The four items phase 40 could derive, plus the one it could not — and the one it could not is still
not done, which has to be the first sentence rather than a footnote.

1. **Nobody has heard the app, again.** This session has no screen reader and no audio stack; NVDA,
   JAWS and VoiceOver were all unavailable, exactly as in phase 40. The NVDA hour is **not done and
   was not simulated**. Everything else in this phase was built instead, and the re-entry condition
   is unchanged and now the *only* thing between this codebase and a finished accessibility story.
2. **`Sort by` reaches every document list** — 16 screens over 15 queries, which is the derived
   number and not the "18" the roadmap carried from phase 40's approximate shape census. The set is
   derived from one sentence (*a document list is a paginated list that also filters by date range*),
   the two orderings are the two indexes `TenantIndexConvention` gives a document, and the guard has
   a **behavioural half** that seeds two documents whose creation order and business date disagree
   and drives every handler through both — phase 44's lesson, that a guard over query *records*
   cannot see whether the *handler* applies what it accepts.
3. **The four grids stopped nesting a `<select>` inside an `<a>`.** Phase 45 fixed the *navigation*;
   the nesting itself is the accessibility defect and no event handler makes it valid. The row is a
   `<div>`, the link is a `stretched-link` on the document code, the controls are siblings at
   `z-2`, and the focus ring follows the inner link back out to the row. The picker's two
   compensating handlers are gone, and what replaces them is a guard over **every** template whose
   interesting half is derived: the selectors of every component that renders a control, so a nested
   `<app-custom-status-picker>` counts the same as a nested `<select>`.
4. **A failed document form now names its field at the field.** `FieldError` + `<app-field-error>`
   across 13 forms and 16 controls: `aria-invalid`, an `aria-describedby` that resolves, and focus
   moved to the control. Keyed by the control's own DOM id, which is what lets the guard check both
   ends — phase 34a's mirror question (*which labels name no control?*) asked of messages.
5. **The two search implementations are pinned to one table**, `search-cases.json`, read by the
   Angular suite and by a **real SQL Server** integration test — because the property the table is
   mostly about is case-insensitivity, and that belongs to the collation, which InMemory does not
   have. 22 cases, including the three that would have been a real defect (`%`, `_`, `[a-z]` are
   characters a user typed, not wildcards). They agree.
6. **The drop list is decided, not omitted** — seven items, each confirmed dropped with its reason
   and its re-entry condition, in Decision G. This is the last planned phase, so an item left in a
   list nobody re-reads becomes a permanent silent gap.
7. **The AI-scan allowance is now on the screen that spends it** (phase 46 limitation #5). It was
   only on the Subscription screen, so a user's first knowledge of the ceiling was a 409 — a message
   you meet by failing, arriving in a live region as an interruption, after the click.

**Two defects only driving the app could find, both in this phase's own new code.** A `computed()`
that short-circuited past its signal recorded no dependencies and froze at null on every document
form in the app — while all six of its unit tests passed, because each called `fail()` before
reading. And the a11y guard's first run reported five templates that were all *comments explaining
the rule being checked*: the file scans raw template text, so prose that mentions a tag is
indistinguishable from one.

Tests: Domain **674 (+0)**, Application.UnitTests **1185 (+4)**, Api.IntegrationTests **30 (+1)**,
Angular **522 (+35)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean; the initial
bundle is 652.23 kB against phase 42's measured 680 kB budget.

---

## What "an hour with NVDA" would have been, and what was done instead

Phase 40 set this out and the answer has not changed, so it is repeated rather than paraphrased: this
environment has a headless browser and no audio stack. Running a screen reader is not something that
can be approximated by reading the accessibility tree, and phase 40 already recorded why — the tree
is the *input* an AT consumes, not a transcript of what it says.

What was done in its place, this phase, is the four derivable items and one live pass through the
browser pane over every screen touched. That pass is worth naming precisely, because it is what
found both of this phase's own defects and neither was visible to any test:

| checked | how |
|---|---|
| the ordering actually changes | switched `Sort by` on a seeded list and read the row order back out of the DOM, then again from the API with three documents whose dates and creation order disagree |
| the control is no longer in the link | `elementFromPoint` at the select's own centre returns the select, `sel.closest('a')` is null, and a synthetic click does not navigate |
| the ring survived the restructure | the row matches `:has(.stretched-link:focus-visible)` and computes `rgb(10, 88, 202) solid 2px` at `outline-offset: 2px` — the measured phase-40 value — with the inner link painting none, confirmed in a screenshot |
| the field error associates | `aria-invalid="true"`, `aria-describedby` resolving to an element whose text is the message, focus on the control, and the banner still announcing inside a region that already existed |
| every swept list got its own label | walked all 16 routes and read the `<option>` text back; four needed a second and third tenant, because Track Inventory / Multiple Warehouses / Manufacturing gate them |

**Re-entry, unchanged and now the last one:** a person, on Windows, with NVDA, on the live regions,
the roving toolbar, the three phase-46 panels and the new field-level errors. Record what is heard
verbatim before changing anything.

---

## Decision A — the set of document lists is derived, and it is sixteen, not eighteen

The roadmap said "the 18 qualifying document lists", carried forward from phase 40's Decision A,
which classified templates by shape and wrote "~18". An approximation used as a target is how a
sweep ends up missing two screens or inventing two, so the first thing this phase did was derive it.

**The rule:** a document list is a paginated list query that also implements
`IDateRangeFilteredQuery`. That second half is not a new criterion — it is phase 34b's own rule for
"this aggregate has a business date", and it is the same sentence `TenantIndexConvention` reads to
decide which tables get the two indexes an ordering may be offered on. One fact, three consumers.

That yields **15 queries** over **16 screens**: `ListPaymentsQuery` backs both the customer Payments
list and the supplier Payments list, which is why the two counts differ. The invoice list was phase
40's one consumer, so 14 queries and 15 screens were swept.

**`ListChequesQuery` is the exemption, and it is the case that shows the rule has teeth.** Both
halves of the test agree, independently:

* **It is not a document list.** `cheque-register-page` is a dashboard with two status tabs and a
  per-row state transition, carries no `app-list-chrome`, and is routed and filed under Accounting.
  That is phase 40 Decision F's evidence for `transaction-list-page`, arrived at the same way.
* **Its aggregate carries neither index the rule requires.** `TenantIndexConvention` recognises a
  business date by **name** — `Date`, then `PostedAt` — and a cheque spells its `ChequeDate`. So
  `Cheque` falls through to the master-data branch, keeps only its hand-written
  `(OrganizationId, Status)`, and its *existing* `ORDER BY ChequeDate DESC` is already a sort over
  the whole filtered set.

The second half is a finding in its own right and is carried, not fixed here: **a convention that
matches by name is a list, and a list becomes a wrong list** (phase 30's lesson). Adding
`"ChequeDate"` to `BusinessDateNames` is two index adds and one migration, and phase 34c's rule says
an index added for one path changes the plan for every other path on the table — which wants a
measurement on `tools/scale/`, not a guess at the end of a phase whose subject is accessibility. It
is carried item #2.

## Decision B — one function, not fifteen literals

Phase 40 shipped the invoice list's menu as a two-entry array in the component with its reasoning
beside it. Sweeping that would have been fourteen copies of a decision, and CLAUDE.md's standing rule
is to retire copies 1..N before writing copy N+1.

`documentSortOptions(dateLabel)` is the one place. The wire values are the server's `ListSort`
members — a reading of the schema — so the only thing a screen chooses is what to call its own date,
and that is the argument. The invoice list was rewritten to call it too, so there is no copy 1 left.

The labels are written out (*Bill date*, *Voucher date*, *Debit note date*) rather than derived from
the heading, because "Expenses" would give "Expenses date": a label a person reads is not somewhere
to save four words of typing.

## Decision C — the guard has a behavioural half, because phase 44 says a record-shaped one is blind

`SortSweepGuardTests` is in `SearchSweepGuardTests`' shape — derive the set, allow an exemption with
its reason, assert the exemption still names something real. Three of its four tests are that shape.

The fourth is the one that matters. Phase 44 found both statutory registers narrowing only their
return half for three phases while a guard over their request records reported them conformant, and
wrote the lesson: *a sweep guard over query records cannot see whether a handler applies the filter
it accepts*. A `Sort` property that reaches no `OrderBy` is exactly that — the control appears, the
value travels, the validator accepts it, and the rows never move.

So `Every_sortable_handler_actually_applies_the_ordering_it_accepts` seeds **two documents whose
creation order and business date deliberately disagree**, drives the real handler through both
orderings and the default, and asserts the two answers differ. A seed where they agreed would prove
nothing, which is why it is built to make them disagree.

It is generic over all fifteen, and three things made that possible rather than fifteen hand-written
tests:

* every one of the fifteen handlers has the identical constructor `(IAppDbContext, ICurrentUserService)`;
* the aggregate is derivable from the query's name (`ListPurchaseBillsQuery` → the `PurchaseBills`
  set → `PurchaseBill`), which the two DTO-projecting lists need because they never name their
  aggregate in their signature;
* every aggregate is built through **its own `Create` factory** — never by reaching past it — with
  only `CreatedAt` stamped afterwards, because that is the one value a factory takes from the clock
  and no caller can pass.

The two decimals a factory refuses at zero (`outputQuantity`, `amount`) are **named** in the argument
filler rather than defaulted, so a sixteenth document type with a third validated amount fails there
loudly instead of being quietly fed a value its factory rejects.

Proved to bite: deleting the `ListSort.DocumentDate` arm from `ListPurchaseBillsQueryHandler` and
removing `ISortableQuery` from `ListSalesOrdersQuery` failed the two halves by name; both files were
restored **by hash** (`4eff2f57…`, `597ac8f2…`), never by `git checkout --`.

## Decision D — remove the nesting, do not defend against it

Phase 45 found that clicking *Select Status* on a document list opened the document, traced it
correctly (`stopPropagation` suppressed RouterLink's own listener, the one that calls
`preventDefault`, leaving native anchor navigation as the only behaviour), and fixed it with a
mousedown handler that stops propagation and a click handler that does both. The fix was right about
the bug it named.

**It left the nesting, and the nesting is the accessibility defect.** An interactive element inside a
link is folded into that link's accessible name, so the row announces as one control with the
options read out as part of its label; a keyboard user who tabs onto the select is standing inside a
link they never chose to enter; and HTML's parser is entitled to hoist the element out of the anchor,
which is how identical markup behaves differently in two browsers.

The shape now, on all four grids:

```
<div class="list-group-item … position-relative">      <!-- the row -->
  … <h6><a class="stretched-link …" [routerLink]>{{ item.code }}</a></h6> …
  <div class="d-flex … position-relative z-2">          <!-- the controls -->
```

`z-2` is load-bearing and `position-relative` alone is not: Bootstrap paints `stretched-link`'s
overlay at `z-index: 1`, so a merely-positioned control still sits underneath it and the row link
swallows the click. Verified rather than assumed — `elementFromPoint` at the select's own centre
returns the select.

**The ring had to follow.** The link is now a small anchor on the document code, so `:focus-visible`
on it would have shrunk the focus indicator from a whole row to four characters — trading one defect
for a worse one, since 2.4.7 is about seeing *where you are*. `styles.scss` suppresses the link's own
ring and paints the row's instead, through `:has()`, at the same measured colour and offset as every
other control. Confirmed in a screenshot, which is ground truth (phase 34b).

The picker's two handlers are **removed**, not kept as belt and braces, because the comment above
them said "this control renders INSIDE each row's `<a [routerLink]>`" and that is now false. Its two
navigation tests went with them; one new test asserts the click is *not* prevented any more, so a
later "restore the guard" edit has to argue with a test rather than with a comment.

**What replaces them is stronger and is the interesting part.** `a11y-sweep-guard.spec.ts` now
asserts that no template nests an interactive control in a link or a button — over every template in
the app, not over one component. A regex for `<select>` inside `<a>` would have found **nothing** on
the four grids that actually shipped the defect, because what they nested was a *component*. So the
guard first derives `CONTROL_COMPONENTS`: the selector of every component whose own template renders
a control. A nested one of those counts the same as a nested `<select>`, and the sixteenth control
component is covered on the day it is written. Proved to bite by putting the picker back inside an
anchor: the failure named `<a> contains <app-custom-status-picker>`, which is the derivation working.

## Decision E — field errors are keyed by the control's DOM id

Phase 40 left `aria-describedby` reaching 15 fields, all of them in the five auth forms, and said
why: every *other* form in the app surfaces its errors through the banner at the top. That banner
does announce — phase 40 made it a real live region — but what a screen-reader user then has to do is
find the Customer field by walking the form, with nothing on the control saying it is the one at
fault.

`FieldError` is a plain class the page constructs, for `ListFilter`'s reason (two detail pages can be
alive at once while a route is torn down, and a root-provided service would share one state between
them). It takes **the page's own `errorMessage` signal** rather than owning a second one, and that is
what keeps the banner and the field from ever disagreeing: the marked control is *derived* by
comparing the text this class last set against what the banner is currently showing, so a page that
clears its message, or sets a server error, drops the marking for free. A stored flag would have left
a field painted red under a message about something else.

**Keyed by the control's DOM id**, because every control on every document form already carries a
stable one — phase 34a's sweep made each one nameable by its `<label for>`. That single string gives
the message element's id, the `aria-describedby` that points at it, and the `fail()` call, all by
derivation. Which is what makes the guard possible at all.

The guard checks **both directions**, and they fail differently:

* a `fail()` with no `<app-field-error>` marks a control whose `aria-describedby` then dangles;
* an `<app-field-error>` no `fail()` ever names is an element that never renders — markup that looks
  correct forever.

It also asserts the three bindings are on the control with that id, and that at least 16 associated
pairs were found, so it cannot pass over an empty scan. All three directions were proved to bite and
the three files restored by hash.

**What is deliberately *not* associated:** the "Add at least one line with a Product and a Quantity"
message on every document form, and Purchase Bill's import trio. Neither names one control — the
first is about the line table, the second about three fields at once — and pointing `aria-describedby`
at an arbitrary one of them would be a wrong answer that looks checked. They stay banner-only, which
is a correct WCAG 3.3.1 answer, just a weaker one.

Focus moves to the failing control. That is the part a banner cannot do: 3.3.1 is satisfied by the
announcement, and what makes the announcement *actionable* is landing on the field it names.

## Decision F — the search parity table runs against a real database

Phase 40's carried item #5 said the lookup filter and the server's `?search=` "agree today; nothing
enforces that they keep agreeing". `web/src/app/shared/pagination/search-cases.json` is what enforces
it, in the arrangement phase 26b made for `BsCalendar`/`bs-date.ts` and phase 39 for the two rich-text
sanitisers: neither implementation is the source of truth, the table is, and both suites read it.

**The server half is an `Api.IntegrationTests` test against SQL Server in a container, not an
Application.UnitTests one**, and that is the whole point. The property the table is mostly about is
case-insensitivity, which comes from the collation and not from the expression — single-argument
`Contains` is case-insensitive on SQL Server and case-**sensitive** on InMemory. A parity test on
InMemory would have pinned a behaviour production does not have, which is the standing gotcha this
file lives inside.

22 cases. The five that were worth writing are the ones where a `LIKE` pattern built by concatenation
would genuinely have diverged: `%`, a bare `_`, and `[a-z]` are characters the user typed, and the
table says so. **They agree** — the server treats all three literally. That is a verified answer
rather than an assumption, which is the reason to write the case rather than reason about it.

## Decision G — the drop list, decided

Seven items, each **dropped**, each with the reason it was dropped and what would bring it back. This
is the last planned phase; an item left in a list is a gap nobody will ever find.

| item | dropped because | re-entry |
|---|---|---|
| `Organization > Developer Mode` | confirm-lived in phase 25: API credential management — Generate API Keys, a Client ID and Secret Key, a Revoke control, a link to an API Playground. Real substance, and a **platform** feature rather than an ERP one; this rebuild has no public API programme and no OAuth client model. | a public API programme, which is a product decision and then a phase of its own |
| `Organization > Documents` | confirm-lived in phase 25: a bare "Drop your files or Click to upload" zone at `#/config/organization/attachments`, which is phase 18's polymorphic `Attachment` with `ParentType=Organization` and nothing else. No FR asks for it. | someone needing to file a document against the tenant rather than against a record |
| `Product.PrintProfileId` | present in the live product JSON since phase 3, and phase 20d confirmed live that it does nothing observable. A field nobody could see the effect of is not a feature. | a live tenant where changing it changes a printed document |
| the Marketplace flag | a permission flag in the research and nothing else; the storefront it would gate is a PRD non-goal. | unchanged — a PRD change |
| the Service Charge column | `service_charge_applicable` is a product-level flag this codebase does not model, and phase 26b confirmed the live report prints `-` on every row of both modes on a tenant that has it. Phase 8f's precedent: omit rather than fake. | a tenant that charges one |
| supplier credit-limit enforcement | phase 31 read the setting's own wording — "when a **Customer's** balance is about to exceed it's credit limit" — and the only live dialog is on Approve. A supplier still *carries* a limit because the live form offers one; two tests say by name that nothing reads it, so it is not mistaken for a half-built feature. | the reference product growing a supplier-side dialog |
| `customtags` in the rich-text toolbar, and rich-text tables/images | phase 39 #5/#6. `customtags` could not be resolved on any tenant where it works, and a table or an image is a *renderer* capability: phase 39's rule is that the grammar is `RichTextPdfRenderer`'s capability list, so adding either to the toolbar without adding it to the PDF makes the field look one way on screen and another in the document a customer receives. | a tenant who needs a table in their terms — and it is then a renderer phase, not a toolbar one |

## Decision H — the three phase-46/45 leftovers, decided separately

The kickoff listed four items that are not in the roadmap heading. They are not one decision:

1. **The AI-scan allowance is invisible where scanning happens** (46 #5) — **built.** The Document
   inbox now reads the shared `SubscriptionStore` and says "*N of 20 AI scans used today — M left*",
   and reloads it after a scan. It renders nothing when the tenant is unmetered (`quota == 0` is
   phase 46's "no ceiling") or while the read is in flight, because a counter that says nothing is
   worse than no counter. The reference product shows nothing here either, and that is not a reason:
   the 409 is ours, and a limit a user can only discover by hitting it is an accessibility problem
   before it is a UX one.
2. **`LocationQuota` can go stale silently** (46 #6) — **not built, decided.** Phase 46's own finding
   is that the purchased count is a *record* that refuses nothing. A prompt to reconcile a number
   against which nothing is enforced would be an alert about a discrepancy with no consequence, which
   is how a banner teaches people to ignore banners. The Subscription screen already says so when the
   counts disagree. Re-entry: the vendor-side actor, at which point the count means something.
3. **The Attributes Used multi-select** (45 #1) — **not built, carried.** Every option of every
   attribute as an individual checkbox is a real keyboard problem on a large catalogue, and it is
   also a real *design* problem — grouping, per-attribute select-all, a filter — which is a screen to
   design rather than a sweep to run. It is carried item #3 rather than half-done at the end of a
   phase.
4. **The four grids' nested `<select>`** — built, Decision D.

`docs/phase-47-kickoff.md` is deleted, as it asked.

---

## The E2E

Against the running API on `https://localhost:7104`, seeded through the API with a Python `urllib`
cookie jar (curl cannot do the file legs here — phase 30/39), against a **fresh organization**.

Three purchase orders created in one order and dated in the opposite one, so the two orderings must
disagree; the script asserts that they do before it asserts anything else, because a seed where they
agreed would prove nothing.

```
POST /api/auth/login                                 -> 200
POST /api/organizations                              -> 201
POST …/contact-groups, …/contacts                    -> 201, 201
POST …/purchase-orders  (2026-06-20, -06-10, -06-15) -> 201 ×3
GET  …/purchase-orders                 (no sort)     -> 200   created order, newest first
GET  …/purchase-orders?sort=newest                   -> 200   identical
GET  …/purchase-orders?sort=date                     -> 200   a genuinely different order
GET  …/purchase-orders?sort=customer                 -> 400   {"Sort":["Sort must be one of: date, newest."]}
```

**On the negative path CLAUDE.md asks for.** The standing bar is a 403 naming the exact permission
key against a nonexistent id. This phase adds no permission key, so that proof would be about phase
32b's machinery rather than about anything built here — phase 40's argument, and it still holds. The
equivalent negative path for what this phase added is the validator refusing an unknown ordering *by
name*, which is what the last line shows: the rejection happened in the pipeline, not in a handler
quietly choosing a default.

Browser, against the running app on `https://localhost:4200` (the dev cert plus the transplanted
`erp_auth` cookie, phase-25 Step 3):

* **Purchase Orders**: `Sort by` offers exactly *Recently added* and *Order date*; switching to the
  second reorders three seeded rows from creation order to date order, confirmed both in the DOM and
  in a screenshot.
* **All 16 lists**: every one renders the control with its own date label. Four needed a second and a
  third tenant — Inventory Adjustments and Warehouse Transfers are gated on Track Inventory and
  Multiple Warehouses, the two Manufacturing lists on Manufacturing — and phase 27b's feature route
  guard bounces to Home without them, which is correct behaviour and would have read as a missing
  control.
* **The restructured row**: `rowTag: DIV`, the link is the `stretched-link` and its accessible text
  is the document code alone, `sel.closest('a')` is null, `elementFromPoint` at the select's centre
  returns the select, and a synthetic mousedown/click does not navigate.
* **The focus ring**: the row matches `:has(.stretched-link:focus-visible)` and computes
  `rgb(10, 88, 202) solid 2px` at `outline-offset: 2px`, the inner link computes none.
* **New Purchase Order, Save with nothing filled in**: three mutations *inside* the pre-existing
  `role="alert"` region, `aria-invalid="true"` on the supplier select, `aria-describedby` resolving
  to an element reading "Select a Supplier.", and `document.activeElement` on the control.
* **Document inbox**: "0 of 20 AI scans used today — 20 left" on a trial tenant.

---

## Traps hit

* **A `computed()` that short-circuits past its signal records no dependencies and freezes.**
  `owner !== null && this.errorMessage() === owner.message` never reaches the signal on a form that
  has not failed yet — which is the first render of every form — so the computed memoised null with
  an empty dependency set and no later `fail()` could wake it. `aria-invalid`, `aria-describedby` and
  `is-invalid` were absent on every document form in the app while the banner and the focus move both
  worked, which is a confusing symptom. **All six unit tests passed**, because each one called
  `fail()` before reading anything; only opening a form and pressing Save shows it. Read the signal
  first and unconditionally, and the test that now pins the ordering was proved to catch it.
* **The a11y guard scans raw template text, so a comment that mentions a tag is a tag.** The new
  nesting check's first run named five templates, and all five were comments explaining the rule —
  including this phase's own "*a `<select>` nested in an `<a>` is invalid HTML*". Comments are now
  stripped once, for every assertion in the file, with a test asserting both halves: that comments
  are gone and that nothing else is (a greedy `<!--[\s\S]*-->` would swallow most of some templates
  and make every other assertion vacuous). This is phase 40's backtick-in-a-comment trap in a second
  costume.
* **A scripted insert anchored on `protected onSearch(...)` landed between that method and its doc
  comment**, orphaning the comment above an unrelated field on 15 files. Phase 35a's gotnote in
  mirror image: there the fix was to anchor on the statement's *closing* line, here it is to anchor
  on the comment block's *opening* line — the unit is the comment plus the declaration, and an anchor
  has to name the whole unit.
* **Three of the files in the server sweep are LF while the rest are CRLF.** A `\r\n`-anchored script
  aborted on the third file with an anchor that looked correct, which is the useful failure; a script
  that had merely written them would have rewritten three whole files invisibly. Every sweep script
  in this phase detects the newline per file.
* **A feature-gated route redirects to Home**, so a sweep of every list route on one tenant reports
  four screens as missing the control they have. Two more tenants, one per feature set.
* A dev-server page in the browser pane cannot be given a cookie before it has loaded a document —
  `document.cookie` throws `SecurityError` on `about:blank`. Navigate, then set it, then navigate again.

---

## Carried items

1. **Nobody has heard the app.** Unchanged from phase 40 #1, and now the only item standing between
   this codebase and a finished accessibility story. An hour with NVDA on Windows, on the live
   regions, the roving toolbar, the phase-46 panels and this phase's field errors.
2. **`Cheque` has no leading-tenant date index, and its list already orders by an unindexed column.**
   Decision A. `TenantIndexConvention.BusinessDateNames` is a list of names, and `ChequeDate` is not
   on it. The fix is two index adds and one migration; the reason it is not here is that phase 34c's
   rule wants a measurement on `tools/scale/` first, because an index added for one path changes the
   plan for every other path on the table.
3. **The Attributes Used editor is still a flat checkbox list.** Decision H #3.
4. **Two messages per document form are still banner-only** — the line-table one and Purchase Bill's
   import trio — because neither names one control. Decision E says why; the honest improvement is a
   message that points at the *table*, which needs a target element the table does not currently
   have.
5. **`role="toolbar"`'s roving tabindex still exists in one component** (phase 40 #6). No second
   toolbar appeared, so there is still nothing to extract.
6. **`ListSort` has two members and the correspondence to `TenantIndexConvention` is still not
   asserted by a test**, for phase 40's stated reason: `Application.UnitTests` references Application
   only and the convention lives in Infrastructure. Both sides carry the sentence.
7. **Cadehi's trial ends 2026-09-22.** One week after this session. It is the first observable expiry
   this project has a date for, and the read that would settle phase 31's and phase 46's derived
   expired-tenant behaviour. Nothing else waits on it.
