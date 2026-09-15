# Phase 47 kickoff prompt

*(Generated at the end of phase 46. Paste the block below into a fresh session. Delete this file
once phase 47 has started — it is a handoff, not a document.)*

---

Phase 47 — Accessibility completion: the parts that need a person.
Goal: finish the WCAG work phases 34a and 40 could not finish without a human at a screen reader, and
clear the four list/copy items 40 left behind. The roadmap heading "47. Accessibility completion —
the parts that need a person" is the scope. **This is the last planned phase**; after it the roadmap
has only the deferred list, so this session also decides what "done" means for the roadmap itself.

Before starting: `git log --oneline -3` should show the phase-46 commit on top. `Api.IntegrationTests`
needs Docker Desktop running — without it nine Testcontainers-backed tests fail in their constructors
before any assertion, which reads like nine regressions and is not. Phase 46 ended with 29/29 green.
**`ng build` and `ng test` need Node 24** — run `nvm use 24.11.0` first; on v16 the build dies with
`availableParallelism is not a function`, which looks like a code fault and is not. Run `ng test`
from `web/`.

**Phase 46 changed two things on surfaces this phase will touch.** The Subscription & Features screen
gained three panels (an AI-scan meter, a billing-location record row, and a conditional
entitlement-mismatch panel) — the mismatch panel is **rendered conditionally**, which makes it the
kind of thing phase 40's live-region rule is about: it appears after data loads, so if it ever gains
an `aria-live` it will announce on arrival, and if it does not, a screen-reader user meets it only by
walking the page. Decide which, deliberately, during the NVDA hour. Second, `SubscriptionQuotaBehavior`
now refuses AI scans with a 409 whose message is the only place a user learns the allowance exists —
there is no counter on the Document inbox (phase 46 limitation #5), so that message is doing
accessibility work it was not designed for.

**This phase's "confirm live" is not a tenant read — it is a person with a screen reader.** There is
no reference product to read for this; phase 34a and 40 already established that the reference
product is *less* accessible than this codebase, so parity is not the bar and WCAG is. What cannot be
derived is what things actually *sound like*, which is the whole reason this phase was deferred twice.

Read first (docs):
- `docs/roadmap.md`, heading "47. Accessibility completion — the parts that need a person", plus the
  "Method" and "Ordering rule" paragraphs above the 42–47 block, and the **Recommended drop list**
  immediately below it — several of those decisions are this phase's to confirm or reverse.
- `docs/phase-40-status.md` in full — its six carried items are most of this phase's scope, and its
  reasoning about live regions, focus rings and the focusable census is the phase's premise.
- `docs/phase-34a-status.md`, Grep "contrast" and "guard" — the measured palette in
  `contrast-rules.ts` and what `a11y-sweep-guard.spec.ts` does and does not cover.
- `docs/phase-34b-status.md`, Grep "Sort by" — the one consumer that proved the ordering seam, which
  40 #4 wants swept across 18 lists.
- `docs/phase-46-status.md`, TL;DR only — the three new panels named above.
- `docs/phase-lessons.md`: the **40**, **34a**, **34b** and **46** paragraphs.
- `docs/known-gotchas.md`, the **Angular** group in CLAUDE.md and its headings there — particularly
  the live-region pair (a region created already holding its text announces nothing; `role="alert"`
  on page furniture interrupts), the focus-ring measurement, and the two guard-predicate entries
  (a predicate naming a type, a file extension, or now a dependency stops covering what it claims).

Scope decisions to make (recommended default, precedent):
- **The NVDA hour** (40 #1). A person, on Windows, hears the live regions, the roving toolbar, the
  status banners and the new phase-46 panels. **Recommend recording what was heard verbatim before
  changing anything**, and fixing only what was heard to be wrong — phase 40's own rule, and the
  reason `getComputedStyle` inside a `focusin` handler produced an app-wide 2.4.7 failure that did
  not exist. If no screen-reader session is possible this session, say so plainly and do the four
  derivable items below rather than simulating it.
- **`aria-describedby` on every document form** (40 #2): the pattern exists on 15 fields; the sweep is
  the work and a guard pins it. Recommend deriving the guard from the same mirror question 34a used
  (which *messages* name no control?), not from a list of forms.
- **`Sort by` across the 18 qualifying document lists** (40 #4), with the orderings read from
  `TenantIndexConvention` so an offered sort always has an index behind it, and an unknown sort value
  a 400 naming the field. Recommend doing this one first — it is the largest and the least dependent
  on the NVDA session.
- **Lookup vs server search parity** (40 #5): one shared table both suites read, in the
  `rich-text-cases.json` / `bs-date` style. Recommend it; it is small and it closes a real
  case-sensitivity divergence (`Contains` is case-insensitive on SQL Server, sensitive on InMemory).
- **The drop list** (roadmap): `Organization > Developer Mode` and `> Documents`,
  `Product.PrintProfileId`, the Marketplace flag, the Service Charge column, supplier credit-limit
  enforcement, `customtags`, rich-text tables/images. Recommend **confirming each as dropped with its
  reason in the status doc** rather than leaving them in a list nobody re-reads — this is the last
  phase, so an undecided item becomes a permanent silent gap.
- The `role="toolbar"` extraction still waits for a second toolbar (40 #6); the two `NG8113` warnings
  go when those files are next opened (39 #8).
- Permission keys: none expected. Say so explicitly if any item disagrees.

Exit bar: dotnet build/test (Docker up for `Api.IntegrationTests`), ng build/test clean (`ng test`
from `web/`, Node 24); every new guard shown to bite by injecting a regression and restored **by
hash**, never `git checkout --`; if `Sort by` ships, a hand-driven E2E proving one list's ordering and
a 400 naming the field for an unknown sort; `docs/phase-47-status.md` with a TL;DR, what the screen
reader actually heard written up **whichever way it went**, and every drop-list item confirmed or
reversed with reasoning; then update CLAUDE.md's phase index line and Current status, append the
phase-lessons paragraph, add any new gotcha (one line under 220 characters in CLAUDE.md, narrative in
`docs/known-gotchas.md`), and add the roadmap index-table row. Do not commit; hand over a commit
message.

**What phase 46 left for you that is not in the roadmap heading.**

1. **Cadehi's trial expires 2026-09-22** — the first observable expired tenant this project has ever
   had a date for. Phase 31 and phase 46 both ship *derived* read-only behaviour and both say so.
   After that date a single read settles it. Worth doing even if it lands outside phase 47's scope,
   because both phases named it as the re-entry condition.
2. **The AI-scan allowance is invisible where scanning happens** (46 limitation #5). The meter is on
   the Subscription screen; the Document inbox shows nothing, so a user's first knowledge of the
   ceiling is a 409. The reference product shows nothing either, which is why it was not built — but
   this one is ours to improve, not theirs, and it is an accessibility problem as much as a UX one.
3. **`LocationQuota` can go stale silently** (46 limitation #6). The screen says so when the counts
   disagree; nothing prompts anyone to fix it. A candidate for the same treatment as phase 45's
   "explain the empty picker" pattern.
4. **Phase 45's four list grids still render a `<select>` inside an `<a routerLink>`** — invalid HTML
   that no `preventDefault` makes valid, with a sweep script in the phase-45 session notes that finds
   exactly those four. **This is now an accessibility item, not a tidiness one**, and phase 47 is the
   phase whose remit it falls in: an interactive control nested in a link is a real screen-reader and
   keyboard problem, not just a spec violation. Strongly recommend taking it here.
5. **The multi-select on the Attributes Used editor** (45 limitation #1) is still every option of
   every attribute as an individual checkbox, with no per-attribute *Select all* and no filter. On a
   tenant with a large attribute catalogue that is a keyboard-navigation problem too.
6. `docs/phase-47-kickoff.md` is this file; delete it when phase 47 starts.

Because this is the last planned phase, end the session by **replacing the roadmap's forward section
with a short "what remains" statement** (the deferred list, the re-entry conditions, and anything
phase 47 itself drops) rather than by generating a phase-48 kickoff.
