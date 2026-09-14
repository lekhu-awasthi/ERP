# Phase 40 — the human accessibility pass, and the guard that could not see its own blind spot

## TL;DR

The phase 34a explicitly could not do: six WCAG criteria a machine cannot decide, over 140 templates
a machine did not write. Its stated risk was **a large diff that proves nothing** — an accessibility
pass driven from a checklist rather than from a keyboard. So the shape of this phase is: drive the
app, record what that finds, and only then write code.

1. **A keyboard-only user could not enter the application.** The organization picker's rows were
   `<div (click)>` — not focusable, no role, no key handler. The screen after sign-in had **six**
   focusable elements and none of them was an organization, so all 140 other screens sat behind a
   control `Tab` could not reach (WCAG 2.1.1, Level A). Fixed to `<a routerLink>`: 6 focusables → 120,
   and `Enter` on a row navigates. **No guard in this codebase could have found this** — 34a's asks
   whether controls are *named*, and this was an element that was never a control.
2. **Every focus indicator in the app failed 1.4.11, and the reason is 34a's own finding one layer
   down.** Bootstrap paints `:focus-visible` as the control's own tone at 50% alpha. 34a found those
   tones clear 4.5:1 against pure white "and only just"; halved, they measure **1.21:1 to 2.53:1**
   across the twelve variants this app uses, against a 3:1 requirement. Not one passed, so there was
   nothing to tune: `styles.scss` now paints one opaque `#0a58ca` outline on everything, at 6.11:1 on
   the page body, and `contrast-rules.ts` measures focus rings the way it already measured text.
3. **163 status banners announced nothing, and there were really 170.** The house idiom was
   `@if (msg) { <div role="alert">…</div> }` — a live region created *already holding* its text,
   which is one DOM mutation and no change to a region any screen reader was watching. Confirmed
   against the running app with a MutationObserver. `<app-status-banner>` keeps the region outside
   the `@if`; the same observer now records the message arriving **inside an existing region**. The
   new guard then found seven more that spelled it `role="status"` and had never been counted.
4. **`aria-invalid` and `aria-describedby` appeared zero times in the codebase.** Fourteen fields
   painted themselves red and rendered a message with nothing joining the two — including
   `bs-date-input`, which is 126 of the app's date fields.
5. **25 ARIA groupings named nothing.** `role="group"` with no name announces "group" and adds a
   boundary carrying no information — worse than the `<div>` it would otherwise be. 34a asked whether
   every *control* was named and never asked it of a *grouping*.
6. **34b's three list-chrome leftovers are closed with reasons, not with shrugs.** `sortOptions` has
   its first consumer, and its re-entry condition is now checkable: an ordering may be offered when an
   index leads on `(OrganizationId, <that column>)` — which is a reading of 34c, not a preference.
   Six unpaginated Configurations lookups filter client-side. `transaction-list-page` is a report and
   `alert-list-page` is a list, and the evidence for each is in Decision F.

**Two findings the guards produced about themselves.** `a11y-sweep-guard`'s glob names a file
extension, so five inline-`template:` components had been outside all nine assertions since 34a —
widening it found nothing wrong, which is the honest answer and not the same as never having looked.
And its control test accepted `[id]` but not `[attr.id]` while its *label* test accepted both
spellings of `for`, so a control naming itself the second way read as unnamed.

**One defect only a live tenant could show.** `ListChrome` rendered a "Billing location" caption above
a control that hides itself on a single-location tenant — which is the default tenant. A visible label
naming no control, on 22 list screens, invisible to any source-level scan because the control
disappears at *runtime*.

**1.4.10 Reflow passes** at 320 px on all three shapes (list, entry form, report) with no horizontal
page scroll and no offending element. A criterion that passes is a finding too; 34a predicted this
pass would find things, and this is the one place it did not.

Tests: Domain **640 (+0)**, Application.UnitTests **1060 (+4)**, Api.IntegrationTests **29 (+0)**,
Angular **426 (+4)**. `dotnet build` / `dotnet test` / `ng build` / `ng test` all clean. The two
`NG8113` warnings carried since 34a are gone; `ng build` now warns only about the initial bundle
(pre-existing, 651 kB against a 500 kB budget).

---

## What "with a screen reader" means here, exactly

The kickoff set the evidence bar at naming the assistive technology and version. **No screen reader
was run, and that has to be said plainly rather than implied away.** This session has a headless
browser and no audio stack; NVDA, JAWS and VoiceOver were all unavailable. What was used instead:

| the pass needed | what was actually done |
|---|---|
| keyboard operation | real `Tab` / `Enter` / arrow key events through the browser pane, with a `focusin` recorder capturing the order and the computed indicator at each stop |
| what a screen reader would be handed | Chrome's own accessibility tree and the ARIA attributes it exposes — which is the input an AT consumes, not a substitute for how it narrates |
| whether a live region fires | a `MutationObserver` distinguishing "region added already holding text" from "text changed inside an existing region" — the *observable* fact; the consequence for AT is cited from the ARIA specification, not observed |
| focus visibility | computed `outline` / `box-shadow` under real keyboard focus, plus screenshots |
| reflow | a 320 px viewport with a reload, then measuring `documentElement.scrollWidth` and every element wider than the viewport outside a scroll container |

Two of these gaps matter and are named as carried items: nobody has heard how the app *reads*, and
nobody has confirmed that a real AT announces the fixed live regions. The mechanism is right by
specification and the DOM evidence is recorded; the last step is a person with headphones.

**One measurement error, worth recording because it nearly became a finding.** The first focus sweep
read `getComputedStyle` inside a `focusin` handler and reported `outline: none` on every control in
the content area — an app-wide 2.4.7 failure that did not exist. The style had not recalculated for
`:focus-visible` yet. This is CLAUDE.md's phase-34b gotcha (*the screenshot is ground truth; computed
style can lag*) in a second costume, and the screenshot is what corrected it. The real defect turned
out to be the adjacent one — the ring is painted, and it is invisible.

---

## Decision A — the unit of work is a *shape*, and that has to be a claim, not a convenience

140 templates is not 140 audits, but saying so is only honest if the shapes are derived rather than
asserted. Classifying every `*-page.html` by what it actually contains (a line-item table, a form
group, the list chrome, a pager) gives five:

| shape | count | what it is |
|---|---|---|
| report screen | ~50 | filter bar, one table, an export |
| document list | ~18 | `app-list-chrome`, rows, a pager |
| document entry form | ~18 | header fields plus a line table |
| configuration lookup | ~15 | inline add/edit form above an unpaginated list |
| auth / dashboard / misc | ~15 | one-offs |

Each shape was driven by keyboard; then every finding was checked for whether it was **the shape's**
or **the screen's**, by asking how many templates carried it. Every finding in this document turned
out to be the shape's — which is why each fix is one component or one stylesheet rule rather than a
diff across 140 files. Where a finding was genuinely one screen's (the organization picker, the
document inbox row), it is fixed in that screen and named as such.

The claim this rests on, stated so a later phase can falsify it: **the screens within a shape were
generated from one another**, which is visible in the source — 130 of the 163 status banners were
byte-identical including their Bootstrap utility classes. A sample generalises here in a way it would
not in a codebase where each screen was written by hand.

---

## Decision B — the `aria-live` policy, and why it is a component rather than a paragraph

34a's carried item #5 asked for "an `aria-live` policy". A policy in a document is a thing 140
templates can quietly diverge from. This one is `app-status-banner`, so a screen gets it by wiring:

* **A failure interrupts** — `role="alert"` / `aria-live="assertive"`. The user's action did not
  happen and they must not type another field first.
* **A success or a notice waits** — `role="status"` / `aria-live="polite"`.
* **`aria-atomic="true"` on both**, so a changed message is read whole. Without it an AT may read only
  the words that differ from the previous message, which for two validation errors sharing a prefix
  is gibberish.
* **Progress is not a status message.** 4.1.3 is about the *result* of an action. A spinner announces
  nothing, and "loading" on every keystroke of a search box drowns the result when it arrives. The two
  `aria-live` regions that pre-date this phase follow the same rule — both announce a result *count*,
  never a request in flight — and the two added here do too.

Six banners lost `role="alert"` entirely rather than gaining the component: the migration page's and
five register pages' standing explanatory notes are always rendered, so the role fired on page load
and interrupted whatever was being read to recite a paragraph. A seventh, the warehouse cap notice,
was the same in the `role="status"` spelling. Unconditional page furniture is not a status message.

Two call sites project their own content (the SMS send result, assembled from three counts; the
Production Journal's stock warning, which carries an *Approve anyway* button beside it), so the
component takes a `visible` input and a content slot rather than forcing two screens to keep the
broken idiom.

---

## Decision C — one focus ring, derived the way 34a derived the text palette

34a's contrast rules ask one question of every colour pair: does the *text* clear 4.5:1. That
predicate names a **property**, and so the file said nothing about the other thing WCAG puts a
contrast floor under — SC 1.4.11, which requires 3:1 for a focus indicator against what it sits on.

Measured from the built stylesheet, compositing each variant's `--bs-btn-focus-shadow-rgb` at the
0.5 alpha Bootstrap uses, over both grounds the app has:

| variant | on a white card | on the #f8f9fa page |
|---|---|---|
| `btn-light` | 1.21:1 | 1.17:1 |
| `btn-warning` | 1.49:1 | 1.45:1 |
| `btn-info` | 1.65:1 | 1.61:1 |
| `btn-secondary` | 1.74:1 | 1.70:1 |
| `btn-success` | 1.78:1 | 1.75:1 |
| `btn-primary` | 1.83:1 | 1.79:1 |
| `btn-danger` | 1.91:1 | 1.87:1 |
| `btn-outline-*` | 1.95–2.15:1 | 1.92–2.11:1 |
| `btn-dark` | 2.53:1 | 2.50:1 |

Nothing is close to 3:1, so this is not a tuning problem. `:focus-visible` now paints a 2px opaque
`#0a58ca` outline with `outline-offset: 2px` and clears the box-shadow — **6.11:1 on the page body,
8.6:1 on a card**, and the offset means the pair it must contrast against is the page rather than the
control's own fill, which makes one number correct for every variant at once. The left nav's
hand-written version (34b) stops being a special case and becomes the general one; it keeps its inset
offset because the rail is dense. Text inputs get `outline-offset: 0` so the ring does not double up
with the border they already thicken on focus.

`FOCUS_RING_RULES` in `contrast-rules.ts` computes all thirteen numbers, and the guard asserts both
halves: that every stock ring fails, and that the shipped stylesheet still paints the replacement.
The first half is the unusual one and is deliberate — it is what stops someone "simplifying" the rule
away on the grounds that Bootstrap already has a focus style.

---

## Decision D — what the human pass found that no guard could, itemised

The kickoff asked for at least one defect found *by* the pass. There are five, and what they have in
common is worth more than the list: **every one of them is about something that is not there.** A
source scan can check the properties of an element it can see. It cannot ask why a click handler has
no element, why a label's control vanished, or why a region has no prior existence.

1. **The organization picker** (2.1.1, Level A). A `<div (click)>` with no focusable element in it.
   The tell was a focusable-element census: six on the whole page, 114 rows, zero overlap.
2. **The focus ring** (1.4.11). Present, painted, measured — and invisible. Only driving the app with
   a keyboard puts a ring on screen to look at.
3. **The live regions** (4.1.3). Correct attributes, correct text, correct placement, and structurally
   unable to fire. Every static check of `role="alert"` passes.
4. **The orphan "Billing location" caption** (1.3.1, 2.4.6). Sound in source: a caption, then a
   control. The control hides itself on a tenant without the Multiple Locations feature, which is the
   default tenant — so on most tenants, on 22 screens, the caption named nothing. This is 34a's mirror
   question (*which labels name no control?*) asked of **runtime** rather than of source, and it is
   the one finding here that required a second tenant shape rather than just a keyboard.
5. **The rich-text toolbar's broken promise.** `role="toolbar"` says the group is one tab stop and the
   arrow keys move within it. All eleven buttons were tab stops, so reaching the text area of a Terms
   field took eleven presses and the arrow keys did nothing. Not a WCAG failure — the *order* was
   correct, which is exactly why no criterion catches it and only using the thing does. Now a roving
   tabindex: one stop, arrows and Home/End inside, and the stop follows the last button used.

The document inbox's row selection (2.1.1 again, `<tr (click)>` driving the preview pane) is a sixth,
found the same way but by reading rather than by driving.

---

## Decision E — `radiogroup`, and why 34a's hesitation was the wrong shape

34a deferred `role="radiogroup"` on the two `btn-check` sets as "a UI-behaviour question, not a naming
one", because the markup is Bootstrap's button-group and changing the role felt like changing
behaviour. It is not: the children are native `<input type="radio">`, and the browser already gives
them the roving arrow-key selection the role promises. The role was the only part missing, and
declaring `group` where the platform is doing `radiogroup` understates what is there.

A third set turned up while looking — Bank Account's Bank/Cash toggle — which 34a's list of two had
missed, and which also had no accessible name at all.

---

## Decision F — `transaction-list-page` and `alert-list-page`, settled

34b asked "are they lists at all?" and could not answer. The reason it could not is that the question
has a false premise: this codebase has **three** list shapes, not one, and the pair are one of each.

**`transaction-list-page` is a report.** Four pieces of evidence, all from the codebase rather than
from taste: it is routed under `/reports/`, `nav-tree.ts` files it under *Reports > Accounting*, it is
gated by `Reports.TransactionList.View`, and it has an export. No list screen has any of those. Its
rows are a projection (`TransactionListRowDto`) across thirteen document types rather than an
aggregate you can open — a list screen lists one aggregate. It keeps the report conventions it already
has, permanently, and the exclusion is no longer provisional.

**`alert-list-page` is a list** — of the *configuration lookup* shape, not the paginated document
shape. It fetches every row through `listAll`, carries its own inline add/edit form, and its rows are
an aggregate you edit. So it gets what that shape gets: the unpaginated search control, not the
chrome. That is also 34b's carried item #3 arriving at a screen 34b had put in a different bucket.

---

## Decision G — `sortOptions` gets a re-entry condition that can be checked

34b shipped the chrome's `Sort by` control with no consumer and set the re-entry condition as "the
first list whose default ordering someone complains about". Nobody can check that, and waiting for a
complaint is how a seam stays empty for six phases.

34c supplies one that can be checked, and it is the stronger rule: **an ordering may be offered
exactly when an index already leads on `(OrganizationId, <that column>)`.** That is not tidiness. 34c
measured the invoice list at 50 000 rows and found `(OrganizationId, CreatedAt)` is what makes it
fast; `ORDER BY Code` beside it would be a sort over the whole filtered set, and the pager would hide
how slow it had become, because page 1 still returns ten rows.

So the first consumer is the **Invoices** list — the one 34c measured — with exactly two options,
because `TenantIndexConvention` builds a document exactly two useful indexes: `(OrganizationId,
CreatedAt DESC)` for the list and `(OrganizationId, Date)` for the date range. The menu is a reading
of the schema, not a design choice.

`ISortableQuery` is the seam, `ListSort` holds the wire values, and `ValidateSort` rejects anything
else with a 400 naming the field — never a silent fall back to the default, which is phase 35a's
read-side gap in a new place: a control that looks like it works and rows that never change.

**The correspondence between `ListSort`'s members and the indexes is not asserted by a test**, and the
reason is stated rather than hidden: `Application.UnitTests` references Application only, and the
convention lives in Infrastructure. It is written where a reader of either side will meet it.

---

## Decision H — the widened guard, and the two holes in it

The kickoff's trap was to ask of `a11y-sweep-guard` the question phase 39 asked of
`SearchSweepGuardTests`: does its predicate name something that silently excludes what predates it?
It does, twice.

* **The glob names a file extension.** Five components carry an inline `template:` string — the
  billing-location picker, the two location filters, the location badge, the calendar toggle — and
  were outside all nine assertions. Widening it found **nothing wrong**, and that is the honest
  result: the hole was real, it happened to be empty, and the only way to know which was to look.
  (Proved non-vacuous: stripping the `aria-label` off `location-list-filter` now fails the guard, and
  could not have before.)
* **The control test accepted `[id]` but not `[attr.id]`,** while the *label* test already accepted
  both spellings of `for`. The one component that spells it `[attr.id]` is allow-listed, which is why
  nobody had noticed; this phase's own `lookup-filter` was the second, and the guard reported it the
  moment the widening let it see the file.

Three assertions were added, each one a finding turned into a rule: every ARIA grouping is named, no
template writes a bare `role="alert"`/`role="status"` (the spinner exemption is scoped to markup that
carries one), and the focus ring measures what `contrast-rules.ts` says it does.

All four new rules were proved to bite by injecting a regression each, then **restoring by hash** from
a copy — never `git checkout --`, which would have reverted this phase's own work on the same files
(34a's rule). The hashes are recorded in the session: three files, matching byte-for-byte after
restore.

---

## The E2E

Seeded through the API (curl-equivalent via a Python `urllib` script with a cookie jar — `curl -F`
cannot read a file here, phase 30), against a fresh organization per CLAUDE.md's rule.

* Three invoices whose creation order and document date deliberately disagree.
* `GET /invoices` with no `sort` → newest-first; with `sort=newest` → identical; with `sort=date` → a
  genuinely different order. A test where the two orderings agreed would prove nothing, so the seed
  is built to make them disagree and the E2E asserts that they do.
* `GET /invoices?sort=customer` → **400** with `{"Sort":["Sort must be one of: date, newest."]}`.

**On the negative path CLAUDE.md asks for.** The standing bar is a 403 naming the exact permission
key against a nonexistent id, so that 403-not-404 proves the check fired before the handler. This
phase adds no permission key, so that proof would be about phase 32b's machinery, not about anything
built here. The equivalent negative path for what this phase *did* add is the validator refusing an
unknown ordering by name — and that is what is proved above, for the same reason: it shows the
rejection happened in the pipeline rather than the handler quietly choosing a default.

Browser, against the running app:

* Organization picker: focusable elements 6 → 120, all 114 rows are `<a href>`, `Enter` on a focused
  row navigates into the organization, and the row shows a `rgb(10, 88, 202) solid 2px` ring at
  `outline-offset: 2px`. The tablist reports `aria-selected` per tab and the panel names the active
  one.
* New Invoice, Save with no customer: the live region exists **before** the error
  (`role=alert aria-live=assertive aria-atomic=true`, empty) and the message arrives as a mutation
  *inside* it. Before the fix, the same observer recorded exactly one event — region added, already
  holding "Select a Customer."
* Invoice list: `Sort by` present, switching to *Invoice date* reorders the rows; the orphan
  "Billing location" caption is gone on a single-location tenant.
* Banks: typing `ban` narrows 5 rows to 4 and the live region announces "4 of 5 shown".
* Terms editor: 11 toolbar buttons, **1** tab stop, `ArrowRight`/`ArrowLeft` move focus and carry the
  tab stop with them, `End` jumps to the last tool.
* Reflow at 320 px on the list, the entry form and the Trial Balance: `scrollWidth === 320` on all
  three, no element wider than the viewport outside a scroll container.

---

## Traps hit

* **A backtick inside a comment inside an inline `template:` literal** terminates the template. Three
  compiler errors that all pointed at the `@Component` decorator and none at the comment. Worth
  knowing because the newly widened guard has the same failure mode by construction — which is why it
  asserts each extracted template is non-empty, the phase-34a empty-stylesheet lesson applied in
  advance.
* **A quoted heredoc in the Bash tool eats backslash escapes** — phase 39's gotcha, hit twice more,
  once corrupting a regex and once an escaped apostrophe inside a TypeScript string. CLAUDE.md already
  says to use the Write tool; the reason it keeps happening is that the script *looks* fine until it
  runs.
* **The dev server serves a stale bundle after a failed rebuild.** Two edits were verified as "not
  applied" against a bundle compiled before the fix — the component's `_ngcontent` id was the tell.
  Restarting the preview was the fix; `ng build` passing while the browser disagrees means the browser
  is looking at something older.
* **A fresh organization has no warehouse, no unit of measurement, no product category and no
  product**, and an invoice line needs all four even for a service. The 400s name the field, so this
  is four rounds of seeding rather than a mystery — but it is four rounds.
* `POST /organizations` and every other create here return **201**, not 200.
* Configuration lookups live under `/organizations/{id}/configuration/...`, not at the org root.
* `POST /products` needs `categoryId` as well as `primaryUnitId` and `type`.

---

## Carried items

1. **Nobody has heard the app.** The mechanism is right by specification and the DOM evidence is
   recorded, but no screen reader was run (none is available in this environment). The two claims that
   deserve a real AT are the live regions firing and the roving toolbar announcing correctly. Re-entry:
   an hour with NVDA on Windows, which is where this app's users are.
2. **`aria-describedby` reaches 15 fields.** Every *other* form in the app surfaces its errors through
   the banner at the top rather than per field, which is why there were only 15 to fix — but "the
   error is somewhere above" is a weaker experience than "this field is wrong". Re-entry: the first
   document form someone reports as hard to correct.
3. **Two of the six lookup lists were done by hand** (Cost Terms has two derived sections, Reporting
   Tags has options nested under categories), so the four-screen sweep is a sweep of four. A fifth
   shape would need its own hand pass.
4. **The `Sort by` control has one consumer.** The rule now says which lists *may* have it; nothing
   says which *should*. Every document list qualifies on the same two indexes — that is 18 screens of
   identical wiring, deliberately not done here, because a seam with one proven consumer is worth more
   than eighteen unproven ones.
5. **The lookup filter is client-side and the paginated one is server-side**, so two lists in the same
   app answer "search" from two places. They agree today (`matchesLookup` is case-insensitive
   substring, which is what SQL Server's collation makes `LIKE` do); nothing enforces that they keep
   agreeing.
6. **`role="toolbar"`'s roving tabindex exists in one component.** No other toolbar in the app declares
   the role, so there is nothing to sweep — but if a second one appears it will have to re-implement
   it, and that is when it should be extracted.
7. **1.4.10 was checked at 320 px on three shapes, not on 140 screens.** Decision A's claim covers it;
   a screen that later grows a fixed-width element would not be caught by anything.
8. **The initial bundle is 651 kB against a 500 kB budget** — unchanged by this phase and still
   pre-existing. 34c took it from 726 kB to 650 kB by deferring the shell; the remaining overage is
   Bootstrap's CSS plus the eager routes.
