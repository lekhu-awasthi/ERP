# Phase 48 — the shared display components, and the sweep that was four times its stated size

## TL;DR

Five carried items that all live in shared Angular components or on the two record pages. Two of
them turned out to be **larger or different than recorded**, and in both cases the roadmap's own
number was the thing that was wrong.

1. **The comment timestamp (43 #6) was not 17 hosts of one component — it was every date this app
   *outputs*.** `activity-panel.html` did render raw ISO strings, but the sweep that finds them also
   finds **23 uses of Angular's own `DatePipe` across 19 templates**, which renders a *Gregorian*
   date in the browser's locale whatever the BS/AD toggle says. Phase 23 swept date **inputs** and
   inline `.toFixed(2)`; nobody had asked the mirror question about date **output**. 27 render sites
   now go through `NepaliDatePipe`, the pipe shifts an instant to the Nepal wall clock before taking
   its day, and a guard covers both halves.
2. **Nothing in the app called `updateDeal` or `updateTask`.** Phase 43 #4 recorded that "editing a
   Deal or a Task still happens through the inline form on its list". It does not: that form is
   **create-only**, and both service methods had no caller anywhere. `UpdateDealCommand` and
   `UpdateTaskCommand` were reachable over HTTP and from no screen — phase-31's rule (*name the
   command **and** the screen*) failing on the screen half. A typo in a deal's title was permanent.
3. **`MARK AS DONE` on the Task detail page** (43 #7), in the top bar, where the live page has it.
4. **Phase 44's four screens get real specs** (44 #6), pinning the three decisions its live reads
   *overturned* rather than the code as written.
5. **The Attributes Used editor** (45 #1, 47 H#3) gets a filter and a per-attribute Select all.
6. **The line-table message** (47 #4) stops being banner-only — and the target is the **Add Line
   button**, not the table phase 47 reached for, because a button is a real control, carries
   `aria-invalid` legitimately, and is the thing the user presses to fix the problem.

**The phase's own correction:** `user-log-page` had carried a recorded reason for using `DatePipe`
since phase 26c — *"this is a to-the-second audit trail, and the seconds matter more here than the
calendar does"*. Read once it is a trade-off; read again it is a false choice, because a BS date
carries seconds perfectly well. The effect was that an Admin reading the sign-in log with the
calendar set to BS saw Gregorian dates, on the one report where being certain which day something
happened on matters most. That is phase 45's lesson (*an allow-list reason can be the argument for
the opposite conclusion*) arriving through a code comment instead of an allow-list.

Tests: Domain 674 (unchanged), Application 1185 (unchanged), Api.IntegrationTests 30, Angular
**522 → 554**. `ng build` clean with no budget warning; the initial bundle **fell** 652.23 kB →
643.39 kB, because dropping `DatePipe` from 19 components took it out of the initial chunk.

---

## The live reads (2026-09-16)

Read-only on `cadehi.tigg.app` and `moonbeamtradingandsuppliers.tigguat.com`. The user logged in; no
credentials were entered by the agent and none are recorded here. Nothing was saved on either
tenant — one modal was opened and closed without pressing Save.

### A. What the reference product shows on an event timestamp

The kickoff's question was the comment timestamp specifically, and neither tenant had a comment on
any record. Four adjacent readings answered it unambiguously, and the fourth is the feed itself:

| Where | What it shows |
|---|---|
| Invoice `INV0001/HO/83-84`, left rail (Cadehi) | `CREATED DATE  September 14, 2026 19:24:44` |
| Task `npc`, left rail (Moonbeam) | `CREATED DATE  August 16, 2026 21:45:55` |
| Deal `Cust Deal`, left rail (Moonbeam) | `CREATED DATE  June 29, 2026 10:48:55` |
| Deal `Cust Deal`, **Activity → Activities** (Moonbeam) | `demo@tiggapp.com updated opportunity Cust Deal` / `22:56:37 14/09/2026` / `a day` |

**The decision: date plus time, to the second.** This *overturns the kickoff's recommended default*
of "BS date plus time" — the recommendation reasoned that a bare date makes two comments a minute
apart indistinguishable, which is right, and then stopped one unit short. Seventeen of the
Activities rows above are within three seconds of a sibling; `HH:mm` would have collapsed them.

Three further things that read came with, none of which were asked for:

- The feed's row is `{user} {action} {noun} {title}` — "updated **opportunity** Cust Deal". It names
  the record's *type*, which is why this phase's `tabParentNoun()` exists (see below), and its
  *title*, which this app does not do and does not need to, since the feed is already on that record.
- A **relative age** ("a day", "11 days", "3 months") sits under every row. Not built — see carried
  items; it is a second formatting concern with its own rules, and phase 23's whole point is that
  one calendar gets one shared implementation.
- The Deal's Activity tab shows **three** sub-tabs (Comments / Activities / **Emails**), not four.
  That confirms `hasSmsHistory` returning false for a record parent, which phase 27a inferred.

### B. What the record pages' edit actually is

Phase 43's 2026-09-13 pass recorded that an edit exists and not what was in it. It is **not** a
left-rail inline editor:

- `OPTION ⋮` on the detail page offers **Edit** and **Archive this task** / **Archive this Deals**.
- **Edit opens the same modal the create flow uses, prefilled.** On the Task it is still captioned
  **"New Task"** — the reference product reusing one component for both shapes and not relabelling
  it. The Deal's is relabelled, "Update Deals".
- `MARK AS DONE` is in the detail page's **top bar**, beside `OPTION`. It is also on the list as a
  per-row checkmark, so 43 #7 was right to call it a convenience.

| Live modal | Fields | Our command |
|---|---|---|
| New Task (editing) | Title\*, Description, Assign To, Due Date, Type, Priority, private toggle | `UpdateTaskCommand` — **exact match** |
| Update Deals | Deal Contact\*, Title\*, Assign To, Lead Source, Description, Expected Revenue, Expected Closing Date, private toggle | `UpdateDealCommand` — match **except Deal Contact** |

The live modal lets you re-point a deal at a different contact; `UpdateDealCommand` has no
`ContactId`. See Decision C.

**This read validated the kickoff's recommendation rather than overturning it.** "Reuse the
component the list already renders inline, rather than a second form" is exactly what the reference
product does — one component, two shapes, prefilled.

---

## Decision A — the sweep is date *output*, and it is 27 sites, not 4

Phase 43 counted `{{ row.createdAt }}` twice in `activity-panel.html` and called the item "17 hosts
of one component". Both halves of that are true and neither is the size of the problem.

Asking the question the other way — *which dates does this app render, and which of them honour the
calendar toggle?* — produces:

| Shape | Sites | Files | What it did |
|---|---|---|---|
| Raw interpolation of an instant | 4 | 1 (`activity-panel.html`) | `2026-09-14T14:00:21.0991469+00:00` |
| `\| date: 'medium'` | 15 | 13 | Gregorian, browser locale, **with** seconds |
| `\| date: 'short'` | 4 | 3 | Gregorian, browser locale, no seconds |
| `\| date: 'mediumDate'` | 2 | 2 | Gregorian, browser locale, date only |
| `\| date: '<explicit>'` | 2 | 2 | Gregorian, the two audit screens |

`| date:` is not a near-miss: **it ignores `DatePreferenceService` entirely.** A tenant reading in BS
saw "Approved Sep 14, 2026, 7:24:44 PM" under a document whose own Invoice Date said `29-05-2083`.
One screen, two calendars.

**The mapping preserves what each screen already showed**, rather than imposing a house style:
Angular's `'medium'` includes seconds and `'short'` does not, so `'medium'` became
`'datetime-seconds'` and `'short'` became `'datetime'`. That rule is also what the live read
independently supports for event stamps.

### Why a mode on the existing pipe, not a second pipe

The kickoff said this explicitly and it is right: two pipes for one calendar is the divergence
phase-26b's shared table exists to prevent. `NepaliDateMode` is `'date' | 'datetime' |
'datetime-seconds'`, and the time is rendered **from the same shifted instant that produced the
date**, so the two halves cannot disagree about which day it is. The pipe stays `pure: false`
(phase-23's reason is unchanged: the ISO argument does not change when the toggle does).

### The bug inside the bug: an instant is not a date

The previous implementation tolerated an instant by taking `value.slice(0, 10)`. That is the **UTC**
day. Nepal is UTC+05:45, so between 18:15 and 24:00 UTC the slice names the day *before* the one the
event happened on in Kathmandu.

This was not hypothetical and not confined to the new work: `transaction-list-page` has piped
`approvedAt` and `createdAt` — both `DateTimeOffset` — through `NepaliDatePipe` since phase 26a, and
has therefore been dating every late-evening approval to the previous day. The fix is in the pipe,
not at the call sites, because **a caller cannot tell from a string whether it is holding a date or
an instant, and this pipe can**. `SystemAuditReportPage`'s spec pins the boundary directly: an
instant at `20:30Z` on the 4th renders `05-05-2026`, and asserts the absence of `04-05-2026`.

### `nepal-time.ts`, because this was already the third copy

`home-dashboard-page.ts` and `bs-date-input.ts` each carried their own `(5 * 60 + 45) * 60_000` with
its own paragraph about the 18:15 boundary. CLAUDE.md's rule is to retire copies 1..N before writing
copy N+1, so both now call `nepalToday()` from the new module and the offset exists once on the
client, mirroring `Domain/Common/NepalTime`.

### The guard, and the two shapes it is

Added to phase-23's `sweep-guard.spec.ts`, which is the file that already owns "a date a user reads":

* **A ban on `| date:`** — exact, and the one that matters, because `DatePipe` is a single
  recognisable token.
* **A ban on interpolating a known instant field with no pipe** — **heuristic**, and the spec says so
  in its own comment: it recognises a list of `DateTimeOffset`-valued field names and is blind to a
  field called something else. Stating that is the point; a guard whose limits are implied gets read
  as a proof.
* A third check names `activity-panel.html` specifically, because a regression there is 17 screens.

The glob was widened to `.ts` as well as `.html`, for phase 40's reason (a predicate naming a *file
extension* silently stops covering inline `template:` components). **Widening it found nothing**,
which is the honest result and is not the same as never having looked.

**Proved to bite**, by injecting two regressions into `activity-panel.html` and restoring **by
hash**: swapping the pipe for `| date: 'short'` failed the suite; deleting the pipe entirely failed
it with 2 failures / 524 passed. The restore mattered more than usual — the first, aborted, run of
the proof script left its injection in the file, and only the recorded digest
(`30c36335…a7a522`) showed it.

### Two things found while in there

- The Activities feed said **"…d this contact"** on all 17 hosts. Phase 18 wrote that sentence for a
  Contact and phase 27a parameterised the component around it without revisiting the one line that
  names the parent, so a document's Activity tab read "Asha Created this contact". `tabParentNoun()`
  renders a document type from its own name (`PurchaseBill` → `purchase bill`) rather than a lookup
  table, so a new `DocumentType` member reads correctly the day it is added.
- The Email Logs sub-tab used `| date: 'short'` while the three beside it used raw ISO — one panel,
  two wrong answers.

## Decision B — one form per record, serving create and edit

The kickoff's premise ("reuse the component the list already renders inline") rests on there being
an inline edit form. There is not, and there never was: `deal-list` and `task-list` have
**create-only** forms, and `grep` for callers of `updateDeal` / `updateTask` across `web/src/app`
returns nothing.

So the recommendation is honoured by **extracting** rather than reusing: `app-deal-form` and
`app-task-form` take the record or null, and the lists now host them for create while two new routes
host them for edit. One validation surface, which is what the recommendation was reaching for, and
what the live product does.

**A route, not a modal**, because this app has no modal mechanism at all — phase 22 established that
Bootstrap's JavaScript is not loaded anywhere. A route is linkable and back-button-correct, and
`NavigationCatalog` ignores it for free because the path carries a `:param` (its coverage sweep
filters those out), which is correct: an edit form is not a screen anyone searches for or pins.

**No new permission keys**, and the reasoning rather than the default: editing goes through
`UpdateDealCommand` / `UpdateTaskCommand`, which have declared `Crm.Deal.Manage` and
`Workflow.Task.Manage` since phases 15 and 13. A new key would have to be seeded by migration and
would split one capability across two grants for no gain — the capability being gated is *edit this
record*, and that is the key those commands already name. The E2E proves both.

## Decision C — the Deal's Contact is shown, not editable

The live modal offers it; `UpdateDealCommand` does not carry it. Three options: add it, offer a
control that silently does nothing, or show the contact as context.

Offering a dead control is the one phase 43 already ruled out — its `WorkspaceName` finding is that a
field an aggregate refuses should be **absent**, not present-and-ignored, because
present-and-ignored reads to a user as accepted. Adding it is a modelling decision, not a missing
line: re-pointing a deal moves its Contact Personnel tab (which shows the *contact's* people) and its
place in that contact's Deals list. So the form renders the contact as plain text when editing, and
the gap is carried with its live evidence rather than closed by reflex at the end of a phase.

Stage and status are absent for a simpler reason: the live modal has neither. Both are left-rail
controls driven by `MoveDealToStage` / `MarkDealWon` / `MarkDealLost`, which the list already has.

## Decision D — the line-table message points at the Add Line button

Phase 47 left this banner-only and named the fix: *"a message that points at the table, which needs a
target element the table does not have yet"*. Building that reveals why it was still open — the
`FieldError` contract is three bindings (`[class.is-invalid]`, `aria-invalid`, `aria-describedby`)
on one control, and **`aria-invalid` is not a supported state of the `table` role**. A `tabindex="-1"`
wrapper would satisfy the guard's string match while making a weaker accessibility claim than the
guard implies.

The **Add Line button** is a better target on every count: it is a real control, natively focusable,
carries `aria-invalid` legitimately, and — the part that matters — it is *what the user presses to
fix the problem*. `FieldError.fail()` moves focus, so focus lands on the fix rather than on the
evidence. This refines phase 47's sentence rather than deferring it again.

Twelve document forms wired. **Journal Voucher had no `FieldError` at all** — every message on it was
banner-only, because the line check is its only validation — so it gains the import, the instance
and the bindings. Driven in a browser: pressing Save Draft on an empty JV sets `aria-invalid="true"`,
points `aria-describedby` at a rendered message, and focuses the button.

The thirteenth "Add at least one…" message is on `variant-attribute-list-page` ("Add at least one
option"), a configuration screen with no line table and no `Add Line` button. Left alone, named here
so nobody counts 13 and finds 12.

## Decision E — the Attributes Used editor gets a filter and Select all, not a redesign

47's Decision H #3 declined this as "a screen to design rather than a sweep to run". The cheap half
is real and separable: a filter box and a per-attribute **Select all / Clear all** with a count.

Two details that are decisions rather than mechanics:

- **The filter matches an attribute's own name *or* an option's value.** Typing "Colour" keeps all
  eight colours; typing "Red" keeps the one option. Both are things a user means by that box, and an
  attribute whose options are all filtered out is dropped entirely rather than rendered as an empty
  heading — a heading over nothing reads as "this attribute has no options".
- **Select all operates on what is *visible*.** A button beside a filtered list that silently
  selected eight things when one is on screen would be the same class of lie as a filter a screen
  displays but does not apply.

Driven on a seeded product with **29 options across 6 attributes** — deliberately past the reference
tenant's 16, since "tolerable at 16, a problem at 60" is the item's whole premise. Filtering to
"Red", pressing Select all, then clearing the filter shows `1 of 8`, which is the visible-only rule
observed rather than asserted.

## Decision F — phase 44's specs pin what its reads overturned

44 #6 says plainly: *"No new Angular tests. The count stayed at 447 and that is a gap, not a claim."*
The four screens now have specs, and what they assert is the **overturned** decision in each case,
because that is the part a later tidy-up would quietly undo:

| Screen | What is pinned |
|---|---|
| Inventory Master | The Txn Type filter offers **Opening Stock and Warehouse Transfer** — the two 26c excluded *by prediction* — Opening Stock first, exactly eight types, and a transfer row with blank money columns still renders |
| Inventory Position | *Display Warehouse in Column* is **disabled until** Group by Warehouse is ticked, and unticking the parent **clears** the modifier (otherwise the next request carries a flag the server refuses) |
| Sales Summary | *Group Wise location* **composes** with the Billing Location filter (both on one request), resets the pager, and its Location column appears only within the split |
| System Audit | The location is shown, a null location stays **blank** rather than becoming a fabricated HeadOffice, and phase 48's own timestamp change — Nepal day, BS when the tenant reads BS, seconds kept |

---

## Verification

**Angular:** 522 → 554 across 64 files. `ng build` clean, no budget warning, initial bundle 643.39 kB
against the 680 kB budget — **down** 8.84 kB, because `DatePipe` left the initial chunk.

**Backend:** Domain 674 and Application 1185, both unchanged (nothing server-side changed this
phase). `Api.IntegrationTests` 30/30 — **the first run showed 10 failures and they were not
regressions**: every one was a constructor throwing `DockerEndpointAuthConfig`, the documented
Docker-not-running signature. They passed once Docker was started, which is the check CLAUDE.md asks
for rather than an assumption.

**Manual E2E**, fresh Organization `Phase48 Records Ltd`, seeded by API calls, **32/32**:

- A Deal and a Task created, then **edited through the commands the new routes call** — `PUT /deals/{id}`
  and `PUT /tasks/{id}`, which had no caller in the app before this phase.
- Every edited field **re-read back** through the query the page uses, so the stored value is the
  proof (phase-32's read-side lesson), including `deal.contactId` **unchanged** — the field the form
  deliberately offers no control for.
- The Task marked Done through `PUT /tasks/{id}/status`, the command the new button sends.
- **The negative path:** a custom role holding no grants, the acting user demoted into it *after* a
  201 from the same user on the same organization in the same run, then both verbs against a
  **nonexistent** id:
  > `403 You do not have permission to perform this action (Crm.Deal.Manage).`
  > `403 You do not have permission to perform this action (Workflow.Task.Manage).`
  403 and not 404 is what proves `AuthorizationBehavior` fired before the handler.

**Browser pass** on a second seeded organization (the first was left grant-less by the negative
path, which is itself the demotion working):

| What | Result |
|---|---|
| Task detail page | `MARK AS DONE` and `Edit` in the top bar, matching the live layout |
| Comment timestamp | `Phase16c Tester · 16-09-2026 10:40:36` — was a raw ISO string |
| Activities feed | "Phase16c Tester Created this **task**" — was "this contact" |
| Edit route | Prefilled; the button reads **Save Task**, not Create Task |
| Edit round trip | Title changed in the browser, saved, navigated back — **and re-read through the API**, because a page showing its own optimistic signal would look identical |
| MARK AS DONE | Status → Done, and the button is then **absent**, not disabled (Done is terminal) |
| Deal edit route | Contact renders as plain text with **no control**, per Decision C |
| Journal Voucher | Empty save sets `aria-invalid`, points `aria-describedby` at the rendered message, focuses the Add Line button |
| Attributes Used | Filter by attribute name (8 shown), by option value (1 shown), no-match message, Select all → `8 of 8`, Select-all-while-filtered → `1 of 8` after clearing |

---

## Known limitations / follow-ups

1. **A Deal's contact cannot be changed.** The live `Update Deals` modal allows it; `UpdateDealCommand`
   does not carry a `ContactId` and this phase did not add one — see Decision C. Re-entry: a decision
   about what re-pointing a deal means for its Contact Personnel tab and the old contact's Deals list.
2. **No relative age on the Activity feed.** The reference product prints "a day" / "3 months" under
   every row. It is a second formatting concern with its own rules (and its own i18n question), and
   phase 23's principle is one shared implementation per display concern; inventing a second one at
   the end of this phase is how that gets violated. Re-entry: anyone who wants it should build it as
   a shared pipe beside `NepaliDatePipe`, not inside `activity-panel`.
3. **The instant-field guard is heuristic.** It recognises seven `DateTimeOffset` field names. A DTO
   that names its timestamp something else is invisible to it. The `| date:` half is exact; this half
   is a floor, and the spec says so in its own comment so a pass is not read as a proof.
4. **`transaction-list-page`'s two instants were being dated by UTC since phase 26a** and are fixed
   by the pipe change, but that screen has no spec of its own — the boundary is pinned on
   `SystemAuditReportPage` instead, which renders the same pipe with the same mode.
5. **The BS rendering of the new timestamps was verified by unit test, not in the browser.** The
   calendar toggle is not in the app shell's top bar, and hunting it was not worth the budget;
   `SystemAuditReportPage`'s spec asserts `22-01-2083` with seconds for the same instant that renders
   `05-05-2026` in AD.
6. **The four phase-44 specs test the component, not the server.** Each stubs its service, so they
   pin what the screen *sends and shows*, never that the handler honours it — which is exactly the
   gap phase 44's own find was about (`ReportLocationSweepGuardTests` could not see whether a handler
   applied the filter it accepted). Phase 44's server-side behaviour is pinned by its own
   Application tests.
7. **`role="toolbar"`'s roving tabindex still exists in one component** (40 #6, 47 #5). Unchanged; no
   second toolbar appeared.
8. **`ListSort` / `TenantIndexConvention` correspondence is still unasserted** (47 #6), for phase 40's
   stated reason: `Application.UnitTests` references Application only and the convention lives in
   Infrastructure.
