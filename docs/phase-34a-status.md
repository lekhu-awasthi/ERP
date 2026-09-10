# Phase 34a — Accessibility: the WCAG 2.1 AA sweep and the guard that keeps it done

## TL;DR

Phase 34 as written in the roadmap was three sweeps and a re-layout. **It was split, by agreement,
into 34a (accessibility), 34b (the left-nav shell + NFR-6.1 list consistency) and 34c (scale)** — see
Decision B for why the boundary falls there and why a11y goes *first* despite the re-layout coming
later.

This phase is 34a. Every template in the app was swept for the parts of WCAG 2.1 AA that a machine
can decide, and a guard spec now fails the build on the next regression:

| check | WCAG | before | after |
|---|---|---|---|
| Page titled | **2.4.2 (A)** | 0 of 141 routes; `<title>Web</title>` | every route, derived from the router |
| Control has an accessible name | **1.3.1 / 3.3.2 / 4.1.2 (A)** | 348 unnamed | 0 (+1 allow-listed component) |
| Label is associated with something | **1.3.1 (A)** | 152 orphan `<label>`s | 0 |
| Icon-only control has a name | **4.1.2 (A)** | 25 of 45 unnamed | 0 |
| Decorative icons hidden | **1.1.1 (A)** | 0 of 1035 | 1035 |
| Table headers declare scope | **1.3.1 (A)** | 135 of 788 | 788 |
| Text contrast ≥ 4.5:1 | **1.4.3 (AA)** | 161 failing pairs + the whole brand palette on the page ground | 0 |
| Bypass blocks | **2.4.1 (A)** | no skip link | one, first in tab order |
| Combobox state exposed | **4.1.2 (A)** | none | `role`/`expanded`/`activedescendant` + a live region |

**The three findings worth remembering.**

1. **121 date fields had a visible label, a real input, and nothing joining them** — and no scan for
   `<input>` could ever have seen it, because the control is wrapped in `<app-bs-date-input>`. A
   component boundary hides a control from a control-shaped scan. The fix needed the *mirror* check
   (every `<label>` must be associated) rather than a better version of the first one.
2. **Bootstrap's default brand tones fail AA on this app's own page background.** They are tuned to
   clear 4.5:1 against pure white and only just — `text-primary` is 4.50:1 — and this app's body is
   `#f8f9fa`, where the same colours fall to 4.27–4.45:1. The margin was never there; it was the
   white in the worked example.
3. **The codebase was already doing the badge pairing correctly 50 times and incorrectly 161 times.**
   `bg-*-subtle` with the plain tone is 3.4–3.9:1; with `-emphasis`, 7.2–10.5:1. The same defect was
   simultaneously an AA failure and an NFR-6.1 consistency failure, which is the first evidence this
   phase produced that 34a and 34b are the same subject seen twice.

**Confirm-live.** Accessibility cannot be confirm-lived — WCAG is the bar, not Tigg — but the
question *"does the reference product label its icon-only controls?"* was put to it anyway, and the
answer settles the premise with evidence rather than assumption: **no, and it structurally cannot.**
Chart of Accounts has 54 icon-only clickables and names 7 of them; the app is built from `div`s (one
`<button>` and two `<a>` in a 255-element DOM), `RETRY` is a `DIV.label`, the row ⋮ is
`<div tabindex="0">` with no role and no name, and there is not a single `<h1>` on any screen read.
The same session took NFR-6.1's baseline for 34b — see "The confirm-live pass" below.

Tests: Domain 443, Application.UnitTests 921, Api.IntegrationTests 18, **Angular 246 (+21)**.
`dotnet build` / `dotnet test` / `ng build` / `ng test` clean. E2E: the phase-33 harness re-run as a
server regression (63 steps, every status code printed, all passed) plus a browser pass that measured
the same badge at **3.49:1 before and 10.35:1 after**.

---

## The confirm-live pass (2026-09-10, `moonbeamtradingandsuppliers.tigguat.com`)

Read-only. Three modules opened and probed with the same script, because the module scan's
list-page-chrome line claims the pattern "repeats across nearly every module" and nobody had ever
checked it.

### NFR-6.1 — mostly true, with two named exceptions

| chrome element | Contacts | Products | Chart of Accounts |
|---|---|---|---|
| `+ ADD NEW` | ✓ | ✓ | ✓ |
| `OPTIONS` | ✓ | ✓ | ✓ |
| `Search For…` box | ✓ | ✓ | ✓ |
| pager `1 - 20 / n` + `Rows Count` | ✓ | ✓ | ✓ |
| leading select-all checkbox column | ✓ | ✓ | ✓ |
| **trailing row-⋮ action column** | **✗** | ✓ | ✓ |
| tab strip above the list | ✗ | ✓ (Goods / Service) | ✗ |

So the scan's claim survives for five of seven elements and is **false for the row-⋮ menu**, which
Contacts does not have at all. That is 34b's baseline, and it is now an observation rather than an
inference — the third time a phase has had to make that correction (32, 32b, 33).

### The screen-reader question

| screen | icon-only clickables | with an accessible name |
|---|---|---|
| Chart of Accounts | 54 | 7 |
| Contact list | 32 | 8 |
| Invoice error state | 21 | 1 |

Its own search box has a placeholder and no label. Its rows are `div[tabindex="0"]` with no `role`.
**Tigg's accessibility is not the bar and could not be**, which the kickoff assumed and this
establishes. Every target in this phase comes from the specification instead.

*Incidentally*: `#/sales/invoices` and `#/purchases/purchases-bill` both failed to render on that
tenant during the pass ("Something went wrong", and a blank body). Recorded because a future phase
reading those screens should not assume its own setup is at fault.

---

## Decision A — what WCAG 2.1 AA means *here*, concretely

**The rule for the split: a check belongs in the guard spec exactly when it can be decided from the
template source, without rendering the page and without anyone forming an opinion.** Everything else
is a human pass, and saying so is the point — a guard that asserts something it cannot actually
decide is worse than no guard, because it reports green.

**Mechanised** (`web/src/app/shared/a11y/a11y-sweep-guard.spec.ts`, 9 assertions over all 161
templates): page titles, control naming, label association, icon-only control names, decorative-icon
hiding, `<th scope>`, and colour contrast.

**Left to a human pass, and why each one resists mechanisation** — recorded as 34b's opening list
rather than quietly dropped:

| criterion | why a machine cannot decide it |
|---|---|
| 2.4.3 Focus Order, 1.3.2 Meaningful Sequence | depends on rendered geometry, not source order |
| 2.4.7 Focus Visible, 1.4.11 Non-text Contrast | needs computed pixels for every interactive state |
| 3.3.1 / 3.3.3 Error Identification & Suggestion | the *quality* of a message is a judgement |
| 2.5.3 Label in Name | needs to know the two strings mean the same thing |
| 4.1.3 Status Messages | whether a change is "status" is semantic |
| 1.4.10 Reflow, 1.4.4 Resize Text | needs a viewport |

### Contrast is the interesting boundary case

In general contrast needs rendering — you cannot know a colour without computing the cascade. Here
you can, because every colour in the app comes from a closed set of Bootstrap utility classes whose
values are fixed constants. So `contrast-rules.ts` records the *palette* and computes the ratios, and
the guard is derived from that computation rather than from a hand-written list of forbidden class
pairs. That matters for a reason beyond tidiness: a list records a conclusion and loses the reason,
and it was the *computation* that surfaced finding #2 above. Nobody writing down "don't use
`text-success` on `bg-success-subtle`" would have gone on to notice that `text-primary` fails on the
page background too.

**The moment a screen sets a colour any other way, this stops being true and the guard stops covering
it.** That is written in the file.

---

## Decision B — the left-nav shell does not ship here, and a11y goes first

Phase 33 deferred the shell to phase 34 by name (its carried item #5: no left nav, no Create New
flyout, no company switcher, no global date filter). Phase 34 as scoped is therefore a ~130-route
re-layout *plus* three sweeps, which is three sessions of work, so it splits. The question worth
recording is not *whether* to split but **in which order**, because the obvious argument runs the
other way: re-layout first, then audit the final layout once.

The obvious argument is wrong, and the reason generalises:

- **The template-level sweep is layout-independent.** A label's `for`, a `<th scope>`, a badge's
  colour class, an icon's `aria-hidden`, an icon button's name — none of them change when the page
  moves inside a new shell. All 2,000-odd edits survive 34b untouched.
- **The landmark-level work is not**, and it is small: a skip link, `<main>`, `<header>`, `<search>`,
  focus order. Doing that work *inside* 34b, while the shell is being built, is cheaper and more
  correct than retrofitting it afterwards — you get it right by construction instead of by audit.
- So the split is not "a11y then layout" or "layout then a11y" but **per-template now,
  per-layout with the layout** — and the guard spec built here is what makes 34b's new markup
  conformant on the day it lands rather than a phase later.

**If it ships in 34b, `NavigationCatalog` drives it.** It already exists, is already derived from
`Router.config`, already carries an `area` and a `name` per screen, and 34a has now added a second
consumer (`PageTitleStrategy` reuses its `titleOf`/`areaOf`). A left nav is a third view of the same
derivation. Writing the nav tree down by hand would be phase-33 lesson (d) violated in the one place
the catalogue was built to serve.

---

## Decision C — "a tenant-sized dataset", and measuring before rewriting

**Deferred to 34c with its measurement defined here**, because the roadmap makes the export rewrite
conditional and the condition has never been evaluated.

- **The number.** NFR-5.1 says "tens of thousands of transactions/products/contacts". 34c's dataset
  is **50,000 invoices, 50,000 contacts, 20,000 products** in one organization — the top of the PRD's
  stated range, so a pass is a pass for every tenant the PRD anticipates.
- **How it is produced.** *Not* through the API: 50,000 invoice creates plus approvals through
  MediatR would take hours and would measure the seeder. Direct `INSERT` through `sqlcmd` with a
  numbers table, then a **single** API-driven approve to prove the seeded rows are shaped like real
  ones. The seeding volume is the point, so 34c budgets for it as a work item rather than a setup step.
- **What is measured.** p95 wall time for: each paginated list's first page; the same list's *last*
  page (offset pagination degrades at the tail, and every list here uses `Skip`/`Take`); the three
  financial statements; the two heaviest registers; and global search.
- **The order matters.** The 25,000-row export cap and the OpenXml SAX streaming writer are named in
  the roadmap as conditional on a tenant having hit the cap. No tenant has — there are no tenants —
  so the honest position today is that the cap stays and the rewrite has a **re-entry condition**,
  not a schedule. This is phase-8f's "omit rather than fake" and phase-33's Tigg-Subscriptions
  decision in the same shape: **do not build for a threshold nothing has crossed, and write down what
  crossing it would look like.** 34c decides the rewrite *after* it has the measurement, not before.

---

## Decision D — global search's cap of 5 and its 18-query fan-out stay carried

Phase-33 carried item #4, and it **stays carried into 34c rather than becoming a 34a item**, for the
reason that makes it a scale question and not an accessibility one: the per-collection cap of 5 only
bites once a collection has more than five matching rows, and the 18-query fan-out only costs
anything once those queries are not answering from an empty table. On today's data both are free.
Measuring them belongs in the same pass that produces the dataset, and 34c is that pass — the
alternative, measuring them now against 2 invoices, would produce a number with no meaning and the
false comfort of having checked.

---

## What was built

**The sweeps** (all script-driven with an asserted anchor count, then reviewed — phase-32's rule):

| sweep | sites | files |
|---|---|---|
| `bg-*-subtle` + plain tone → `-emphasis` | 161 | 83 |
| bare `text-warning` / `text-info` → `-emphasis` | 5 | 4 |
| `<th>` → `scope="col"` / `"colgroup"` (7 spanning) | 653 | 79 |
| `<i>` → `aria-hidden="true"` | 1035 | 152 |
| label immediately before a control → minted `id` + `for` | 226 | 85 |
| line-item grid control → `aria-label` from its column header | 96 | 22 |
| `<app-bs-date-input>` → `[inputId]` + the label's `for` | 121 | 69 |
| icon-only control → `aria-label` from its click handler | 25 | 19 |
| group caption `<label>` → `<span id>` + `role="group" aria-labelledby` | 9 | 8 |
| the remaining controls and labels, decided one at a time | 20 | 16 |

**The structural work**

- `shared/navigation/page-title.strategy.ts` — a `TitleStrategy` deriving every page title from the
  route through `NavigationCatalog`'s existing `titleOf`/`areaOf`, plus `index.html`'s `Web` →
  `ErpApp`. No `title:` on any route definition; a route may still set one and it wins.
- `app.html` / `app.scss` — the skip link (first in tab order, `position: fixed` on focus above the
  sticky header), `<main id="main-content" tabindex="-1">`, and the search box wrapped in `<search>`
  so it is its own landmark.
- `shared/platform/global-search.*` — the ARIA combobox pattern. The keyboard model was already
  right in phase 33; none of it was *exposed*. The rows became `role="option"` in a `role="listbox"`
  (an option must not contain its own focusable control, so they stopped being buttons), the input
  gained `role="combobox"` + `aria-expanded` + `aria-controls` + `aria-activedescendant`, and a
  `role="status"` live region announces the result count — rendered outside the `@if`, because a live
  region inserted *together with* its message is frequently not announced at all.
- `aria-expanded` on the three remaining signal-driven popups (History, the Contact OPTIONS menu, the
  BS calendar). Bootstrap's JS is not loaded here (phase-22's gotcha), so nothing sets it for free.
- `styles.scss` — the four brand text utilities re-pointed at Bootstrap's `-600` shades, and
  `--bs-link-color` with them. **Text utilities only**: `bg-primary`, `btn-primary` and the rest keep
  the brand colour, because white-on-colour is a different pairing that already passes.

**The guards**

- `shared/a11y/a11y-sweep-guard.spec.ts` — nine assertions over every template, built to phase-23
  Decision D's three rules (assert the glob matched first; every allow-list entry states its reason
  and is checked to still exist; failures print paths).
- `shared/a11y/contrast-rules.ts` — the palette, and the WCAG luminance/ratio arithmetic, so the
  contrast rule is computed rather than listed. Pinned against the specification's own worked
  examples (21:1, 1:1, `#777` on white = 4.48:1).
- `shared/navigation/page-title.strategy.spec.ts` — every route in the config produces a title that
  is more than the app name, ends with the app name, and (for non-detail routes) is **distinct**:
  two screens sharing a title is 2.4.2's real failure mode, not a missing one.
- One extra assertion worth calling out: **every caller of `<app-bs-date-input>` must pass
  `[inputId]`.** The allow-list exempts that component from the "needs a name" rule *on the grounds
  that its caller names it* — so the exemption is only honest while that holds, and the guard checks
  it rather than assuming it.

**The guard was verified to bite.** Three regressions were injected into a real template — a badge
back to a failing pair, an unlabelled input, an un-hidden icon — and each was caught by its own
assertion with the offending path printed. (A fourth attempt, reverting a badge to
`bg-success-subtle text-success`, correctly did *not* fail: after the palette change that pairing is
4.97:1 and legal. The guard forbids what fails, not what is merely old.)

---

## Bugs hit while building

**1. A lazy `.*?` between two anchors silently spans the instances in between.** The date-label sweep
matched `<label…>(.*?)</label>\s*<app-bs-date-input`, and where a label was *not* followed by a date
input the engine happily expanded `.*?` across that label's own `</label>` to reach a later one —
merging two labels and rewriting everything between them. **The tell was that two independent counts
disagreed** (99 from the sweep, 121 from a separate scan), which is the only reason it was caught
before it wrote anything; the anchor-count gate refused, as designed. The body must be
`((?:(?!</label>).)*?)`, which cannot cross the closing tag.

**2. A column-index sweep is confidently wrong in an expansion row.** Naming 96 grid controls from
their `<th>` by counting `<td>`s got 6 of them wrong, because those sit inside a
`<td colspan="5">` mini-form where td index 0 is the whole row, not the first column — so an
**Amount** field was labelled "Name". *A confidently wrong accessible name is worse than none*: a
screen reader user is told something false instead of nothing. The audit that found them checks two
things the original sweep could not see (the cell spans columns; the cell has its own visible label),
and both were then fixed to real `for`/`id` pairs.

**3. `[attr.for]` contains no `for=` substring.** The orphan-label check tested
`attrs.includes('for=') || attrs.includes('[for]')` and reported six correctly-associated labels as
orphans, because Angular's third spelling is `[attr.for]="…"`. A guard's false positives are as
expensive as its false negatives — they train you to ignore it.

**4. `?raw` on a `.scss` file returns an empty string, not an error.** The test that keeps
`styles.scss` in step with the measured palette read the stylesheet through `import.meta.glob(…,
'?raw')`; Vite compiles SCSS, so the value came back `''`. `expect(stylesheet).toBeDefined()` passed
on `''`, and every assertion over the contents was vacuously true — **the exact failure mode this
file's own header warns about**, in the file that warns about it. Caught only because the failure
message happened to print the length. `?inline` behaves the same way. Fixed by reading through
`node:fs` (with a two-declaration shim in `src/testing/`, excluded from the app build, rather than a
dependency on `@types/node`) and asserting the content is **non-empty**, not merely defined.

**5. `git checkout --` on a file to undo a deliberate test regression reverts the phase's work on it
too.** Restoring `invoice-list-page.html` after the guard-bites experiment silently threw away that
file's contrast, `th scope`, icon and label edits. The suite stayed green because the guard reads
every template and that one file's regressions were the *only* thing it would have flagged — and it
did flag them, which is how it was noticed. Back up to the scratchpad and restore by **hash**, which
is what the before/after contrast measurement then did.

---

## E2E and the browser pass

**Server regression.** `e2e_phase33.py` re-run unchanged against a fresh organization: **63 steps,
every status code printed, all passed** — including both directions of phase-32b's location-scoped
permission negative and the 403 naming `Platform.GlobalSearch.View`. Phase 34a touches no server
code, and this is the evidence for that claim rather than an assertion of it.

**Browser pass** (dev cert + `erp-web-ssl` + transplanted `erp_auth` cookie, phase-25 Step 3), on the
organization that harness seeded:

- **The finding proven the way a permission negative is — the same control, the same page, the same
  running app, with only this phase's two files toggled:**

  | | class | colour on `rgb(209,231,221)` | ratio |
  |---|---|---|---|
  | HEAD | `badge bg-success-subtle text-success` | `rgb(25, 135, 84)` | **3.49:1** ✗ |
  | now | `badge bg-success-subtle text-success-emphasis` | `rgb(10, 54, 34)` | **10.35:1** ✓ |

  Measured by `getComputedStyle` and the WCAG formula in the page, after checking out HEAD's
  `invoice-list-page.html` and `styles.scss`, letting HMR rebuild, and then restoring both by SHA-256.
  Decorative icons on the same page: **4 of 12 → 12 of 12**.

- **Titles**: `Sign In · ErpApp` on the login screen, `Invoices · Sales · ErpApp` on the list,
  `New Invoice · Sales · ErpApp` on the create form — the singularisation rule working live.
- **The New Invoice form**: 16 controls, **0 unnamed**, both date fields labelled through `inputId`
  ("Date", "Due Date"), **zero duplicate ids** in the rendered document — the minted-id scheme holds
  in a real render, which no source-level check can prove.
- **The grid**: 7 of 7 `<th>` carry `scope`; the line controls announce as Product / Qty / Rate /
  Discount % / VAT.
- **The combobox**, typing `inv` then ArrowDown: `aria-expanded="true"`,
  `aria-activedescendant="global-search-option-1"` resolving to a real `role="option"` with
  `aria-selected="true"`, a `role="listbox"` of 8 options, and the live region reading
  **"8 results available."**
- **The skip link**: first element on Tab from a fresh load, becomes `position: fixed` at (8,8),
  179×40, `z-index: 1080` with a 4px focus ring **above** the sticky header, and activating it moves
  focus to `<main>` (`tabindex="-1"`, `outline: none` so it never draws a ring it did not earn).

---

## Carried items

1. **The human pass has not been done.** Decision A's six non-mechanisable criteria — focus order,
   focus visibility, error-message quality, label-in-name, status messages, reflow — need a person
   with a keyboard and a screen reader. 34b's opening act, and it should run *after* the re-layout,
   not before.
2. **`role="group"` is not `role="radiogroup"`.** Nine group captions were wired as `group`, which
   names the set correctly; two of them (Product Type, Contact Type) are radio sets where
   `radiogroup` would also convey the roving-selection semantic. Deferred because the markup is
   `btn-check` radios inside a Bootstrap `btn-group` that already declares `role="group"`, and
   changing it is a UI-behaviour question, not a naming one.
3. **Two pre-existing `NG8113` build warnings** (an unused `DatePipe` import in `ChequeRegisterPage`
   and `AllocatePaymentPage`). Present at HEAD, unrelated to this phase, left alone rather than
   silently widened into it — a two-line fix for whoever touches those files next.
4. **The contrast guard covers utility classes only.** Any screen that sets a colour with an inline
   `style` or a component stylesheet is outside it. Nothing does today; the guard cannot notice when
   something starts.
5. **`aria-live` exists on exactly one region** (global search). Every other async result in the app —
   a saved form, a failed request, a loaded report — changes silently for a screen-reader user. That
   is WCAG 4.1.3, it is genuinely a judgement call per site, and it belongs to the human pass.
