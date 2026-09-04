# Phase 28 status — Multi-currency (FR-2.5, NFR-1.3)

## TL;DR

**Status: COMPLETE.** A tenant `Currency` list seeded from a fixed product catalog with NPR always
present; `CurrencyCode` + `ExchangeRate` on all twelve document types whose live form shows them;
amounts stored in the transaction currency with the base-currency figure **folded into the posting
rule's inputs at Approve**, so `GlLine` is unchanged and every Phase 8/19/26 report needed **zero
edits**; two new tenant defaults (Forex Gain, Forex Loss) and a realised-difference posting rule on
Payment allocation.

**The roadmap's decisive experiment could not be run, and that is the headline.** It asked us to add
a currency on the Tigg UAT tenant and read a foreign receipt's GL Transactions panel. That product's
own **"Add New Currency" catalog picker returns "No data"** (two 400s in its console), so no second
currency can be activated there and no foreign-currency document can exist. The allocation posting
rule is therefore **reasoned from first principles, not observed** — see Decision F, which records
both the reasoning and the one strong piece of live corroboration.

Six things the live pass *did* settle, all of which changed the design:

1. The Multi-Currency **switch is genuinely self-service and is ON**, on Organization > Features.
2. A document's Currency picker is populated from **the tenant's own active currency list**, and its
   Exchange Rate input is **disabled and pinned to 1 whenever the selected currency is NPR** — on
   both the Invoice and the Customer Payment form. This is why the entitlement became a cap on the
   *currency list* and **no document command is feature-gated** (Decision B).
3. Opening Balances' **Conversion Rate is the identical control**, so it is a document rate, not a
   separate as-at revaluation rate (Decision C).
4. The chart of accounts has exactly one forex account — **"Forex Gain"** (Income, group "Foreign
   Exchange Gain" under Indirect Income) — and **no loss counterpart anywhere**, account or group.
   We ship two anyway, and Decision E says why.
5. The printed document carries **one money column, a currency-coded Net Total (`NPR 3,06,500.00`)
   and a currency-named amount-in-words, and no base-currency column at all**. A layout with no
   column for the NPR equivalent cannot print one, so the printed figure is the transaction
   currency (Decision G).
6. The rate is **typed and stored per document**, with no date coupling (Decision C).

Tests: Domain.UnitTests **375** (+52), Application.UnitTests **746** (+24), Angular **180** (+6),
Api.IntegrationTests 18 (unchanged). `dotnet build` / `dotnet test` / `ng build` / `ng test` all
clean. One migration (`MultiCurrency`), applied to the dev database.

---

## Step 1 — the confirm-live pass (Moonbeam UAT tenant, 2026-09-04)

Driven read-mostly with one config write authorised by the user in advance. Every finding below was
read from the live DOM, not inferred.

**Organization > Features > Multiple Currency.** A `CODE / NAME / SYMBOL` table plus a Show Inactive
toggle and an ADD NEW CURRENCY action. `read_page` found exactly one `switch` element in that
section and its `aria-checked` is `true` — confirming phase-20f's finding that Multi-Currency is the
one genuinely self-service toggle on that screen, and that this tenant has it on. The list contains
**only NPR**, with Show Inactive checked as well as unchecked.

**The Add New Currency dialog** is `Currency` (a "Select Currency" picker) + `Name*` + `Symbol*` +
Save. **The picker renders "No data".** Typing into it (`US`) still returns "No data", and the
console shows two 400 responses. So the reference product's own currency catalog endpoint is broken
or unpopulated on UAT.

> This is where the roadmap's experiment died. The remaining option was to type a Name and Symbol
> with no code and save — which would have written a code-less currency row onto a shared demo
> tenant whose document forms read that list. That was not done. The blocked experiment is recorded
> here rather than papered over, and Decision F carries the consequence.

**The Invoice add form** (`#/sales/invoices/add`), read from the DOM:

| Control | Live state |
|---|---|
| `Currency` | an `ant-select` whose dropdown lists the tenant's active currencies as `NPR / Nepalese Rupee` |
| `Exchange Rate To NPR *` | `ant-input-number`, **`disabled: true`, value `1`**, placeholder "Exchange Rate" |

**The Customer Payment add form** (`#/sales/payments-received/add`) carries the identical pair, with
`Amount` above it — so a payment's Amount is in the transaction currency too.

**Opening Balances > Account**, expanded row form: `Currency` (Nepalese Rupee) / `Conversion Rate`
(the same `ant-input-number`, `disabled: true`) / `Amount` / `DR` / Add Reporting Tags.

**Chart of Accounts.** Searching accounts for `forex` returns exactly one row: `II0006 — Forex Gain`,
type Income, parent group "Foreign Exchange Gain". Searching *groups* for `Foreign` returns exactly
one: `Foreign Exchange Gain`, Income, parent Indirect Income. **No Forex Loss account and no
Foreign Exchange Loss group exist.** There is no revaluation document anywhere in the product, and
no unrealised account — which is the live evidence behind Decision A.

**The printed invoice** (the print preview's own `collection-report-html` endpoint):

```
Amount in Words

Three Hundred Six Thousand Five Hundred Nepalese Rupee

Subtotal        3,00,000.00
...
Net Total       NPR 3,06,500.00
```

Line amounts are bare; the currency **name** appears in the amount-in-words line and the currency
**code** prefixes the Net Total; dates are BS (phase-27b). **There is no second money column
anywhere in the frame.**

---

## Scope decisions

### A. Unrealised revaluation at period end is **out of scope** (the phase's named Decision A)

User decision, taken explicitly, and corroborated live: the reference product ships a *realised*
Forex Gain account under Indirect Income and nothing else — no revaluation document, no unrealised
gain/loss account, no period-end run. Realising the difference at settlement is the whole model.

Building revaluation would have meant a second account pair, a period-end run over open
foreign-currency AR/AP, and a reversal at the next period's start — with no live shape to confirm
any of it against. Recorded here so a later phase can pick it up deliberately rather than discover
the gap.

### B. The MultiCurrency entitlement is a **cap on the currency list**, not a gate on documents

**The most consequential decision in the phase**, and the second instance of phase-20f Decision #4's
shape. Every Organization is seeded with its base currency at creation, so a tenant without the
entitlement has exactly one and is capped there; the *second* currency is what the entitlement buys,
precisely as the second warehouse is.

The live evidence is what makes this sufficient rather than merely convenient. A document's Currency
picker reads the tenant's own list, and the Exchange Rate input disables itself whenever the base
currency is selected. **With a one-entry list, the whole multi-currency surface degenerates to
"NPR, rate 1, read-only" by itself.** FR-2.6's own worked example — "a tenant without Multi-Currency
should not be prompted for exchange rates" — is satisfied by the cap, with no gate on Invoice,
Payment or anything else.

So **no document command implements `IRequireFeature` in this phase.** Gating them as well would be
a second enforcement of one rule, and the one that breaks first when the two disagree.

Like the warehouse cap it is *conditional* ("reject only if one already exists"), which a
marker-interface pipeline behavior cannot express, so it lives in
`CreateCurrencyCommandHandler.EnforceMultiCurrencyEntitlementAsync`. It fails closed on a missing
subscription row, same as `FeatureGateBehavior`. Stating it as a cap also means an Organization with
no currency row yet — one predating this phase's backfill — can still create its first.

The client half is `featureGuard('MultiCurrency')` on the new route plus a `hasFeature` guard on the
dashboard link, matching what phase 27b did for `MultipleWarehouses`.

### C. The rate is **stored per document**, not looked up by date

Confirmed live twice over: the control is a plain manual number input with no date coupling
(changing the document Date does not touch it), and `erp-module-scan.md:413`'s conversion finding
records the pre-fill snapshot carrying `currency, conversion rate` along verbatim rather than
re-deriving them. A date-driven rate table would also need a rate *source*, which this product has
none of.

Opening Balances' "Conversion Rate" is the same control with a different label, so it is the same
thing: a per-row document rate. It is named `ExchangeRate` on the aggregate anyway, so all twelve
types read with one vocabulary.

### D. The fold: convert the **posting rule's inputs**, never the finished GL lines

This is phase-16b's discount pattern applied to currency, and the reason no report changed. The
document stores its own amounts; at Approve the handler converts each line amount with
`ExchangeRates.ToBase` and hands the converted values to the account resolver. The posting rule then
runs on base-currency numbers and is completely unaware any other currency exists. `GlLine.Debit`/
`Credit` mean exactly what they always meant.

**Why not convert the finished `GlLineInput` list, which would have been one call site instead of
nine?** Because every posting rule in this codebase derives its balancing leg as a *sum of the other
legs* — `InvoicePostingRule`'s AR line is revenue + VAT, `PurchaseBillPostingRule`'s AP line
likewise. Converting afterwards rounds the balancing leg independently of the legs it balances, so
`Round(T×r)` can differ from `Sum(Round(aᵢ×r))` and `GlJournalEntry.Post`'s
`sum(Debit)==sum(Credit)` invariant fails — intermittently, for some rates on some documents, which
is the worst possible failure mode. Two 0.05 debits against one 0.10 credit at rate 1.5 give
0.08 + 0.08 = 0.16 against 0.15. Converting the inputs first keeps every entry balanced by
construction, because the rule sums the very numbers it posts. (Phase-25's lesson: build the entry
from the values actually created.)

**Two document types cannot take that route** — `JournalVoucher` and `CashTransfer`, whose posting
rules take the domain aggregate itself and so have no line-amount argument to convert. They go
through `GlCurrencyConversion.ToBaseAsync`, which converts the finished list and **books any residue
to the tenant's forex account** rather than absorbing it (phase-25 again: name the residue). It is
bounded by half a paisa per line and is exactly zero for most entries, in which case no forex leg is
added and no forex account is required — which keeps a base-currency document on a completely
unchanged code path.

**What is deliberately not converted:** anything already in base currency. FIFO layer unit costs and
the COGS derived from them are written in base currency at receipt, so converting an Invoice's COGS
leg again would double-apply the rate. The one place a document's own rate reaches the stock ledger
is `ApprovePurchaseBillCommandHandler`, and it converts with `ExchangeRates.ToBaseUnitCost` (four
decimal places, matching every `UnitCost` column) rather than `ToBase` (two) — rounding a foreign
unit price to paisa would lose real precision on cheap goods and then multiply it by every quantity
ever received. CreditNote and the Void paths re-increment from a stored `CogsUnitCost`/
`ConsumedUnitCost`, which is already base and must not be converted a second time.

### E. **Two** forex accounts, diverging from the reference product on purpose

Its chart ships only "Forex Gain" and no loss counterpart (confirmed live, both the account list and
the group list). Netting losses into that same Income account would give an Income-type account a
debit balance, which every statement in phase-8a's family presents with the wrong sign. This is the
same call phase 6 made in keeping `DefaultVatReceivableAccountId` separate from
`DefaultVatPayableAccountId`: one real accounting distinction is worth one more nullable column.

Both are resolved **only when a difference actually exists**, never up front — unlike every other
account resolver in this codebase, which fails fast because its accounts are needed on every
document of that type. These are needed on almost none, and demanding them at Approve regardless
would make every tenant configure two accounts to use a feature most never touch.

### F. The realised-difference rule — **reasoned, not observed**

`PaymentForexCalculator` is the phase's one genuinely new posting rule. Per allocation:

```
bookedBase  = ToBase(allocation.Amount, target document's rate)
settledBase = ToBase(allocation.Amount, payment's rate)
difference  = bookedBase - settledBase
```

For a **Received** payment the control account is AR, which the invoice debited: a positive
difference means fewer rupees arrived than were booked, so it is a **loss**. For a **Paid** payment
the control is AP, which the bill credited: a positive difference means fewer rupees left, so it is
a **gain**. That single sign flip is the only difference between the two directions, which is why it
lives in one tested function rather than two branches of a handler.

In both directions a **gain debits the control account and credits the forex account**, and a loss
does the reverse. That reads as a coincidence and is not one: on the receivable side a gain leaves a
credit residue on AR to clear with a debit; on the payable side a gain leaves a credit residue on AP
to clear with the same debit.

Differences are **netted across all of a payment's allocations** before the account is resolved, so
a payment settling one invoice favourably and another unfavourably posts a single net line. That is
correct at the control account (there is only one, and only its net movement matters) and is the
conventional P&L presentation. A net of zero posts nothing and needs no account configured.

**Same-currency invariant.** An allocation whose target is in a different currency from the payment
is rejected outright rather than converted: the allocation's Amount is a single number with no
currency of its own, and treating it as the payment's while the target booked it in another would
silently over- or under-relieve that document by the whole exchange rate. Cross-currency settlement
needs two amounts, not one, and is deliberately out of scope.

**The confirmation status.** Not observed live, for the reason in the TL;DR. The corroboration that
does exist: the reference tenant's chart carries a *realised* Forex Gain account under Indirect
Income and no unrealised or revaluation account of any kind — which is what a settlement-time
realisation model looks like and not what a period-end revaluation model looks like. Re-verify when
that product's catalog picker is fixed. The doc comment on `PaymentForexCalculator` says all of this
so a future reader does not mistake it for confirmed behaviour.

### G. The printed figure is the **transaction currency**, with the rate disclosed as a header field

Settled by the live layout rather than by preference: line amounts are bare, the emphasised total
carries the currency code, and **there is no base-currency column in the frame at all**. A layout
with no column for the NPR equivalent cannot print one.

`PrintableDocumentDto` gains a `CurrencyCode`, `DocumentPdfRenderer` prefixes only the emphasised
summary line with it (every other money cell stays bare, as live), and a non-base document gains an
`Exchange Rate To NPR` header field so a reader can reconcile the document against a Trial Balance
without a second money column crowding the page.

### H. A document stores the currency **code**, not a `CurrencyId`

Same reasoning as phase-27b's `Invoice.Terms`: a document must keep the currency it was actually
issued in even after the tenant deactivates or deletes that row, and the printed output labels the
total with the code itself. The code is also globally meaningful in a way a per-tenant GUID is not,
so no report has to join to read it. `Currency.IsBaseCurrency` is *derived* from the code and
explicitly `Ignore()`d in EF, so it cannot drift or be flipped by a stray update.

### I. `CurrencyCatalog` is a static Domain table, not a seeded database table

Same call as `BsCalendar`: product reference data that varies neither per organization nor over the
life of an installation. Adding a currency to the list is a code change with a test, never a
migration. `BaseCode` is load-bearing rather than merely first — every GL amount is denominated in
it and every rate is quoted to it, so a tenant-selectable base currency would change what every
historical `GlLine` means, and is deliberately not offered.

### J. `SetCurrency` is a mutator, not two more constructor parameters

Threading the pair through twelve `Create`/`UpdateHeader` signatures would have changed twelve
aggregates, twenty-three commands, their handlers and every existing test to express one fact. It is
an orthogonal facet of the header with its own invariant, exactly like `Invoice.SetExport`. The
properties carry **property initialisers** (`= CurrencyCatalog.BaseCode` / `= ExchangeRates.BaseRate`),
so a document constructed by any path — including EF's own private constructor — is already in the
base currency at rate 1, and no backfill is needed anywhere.

Draft-only, enforced by the aggregate: an Approved document's amounts are already posted at its
rate, so changing it afterwards would silently invalidate the posting.

`OpeningBalanceLine` is the one exception — it has no Draft lifecycle, so its `Create`/`Update` take
the pair as trailing optional parameters.

### K. Permission keys

`Tenancy.Currency.View` (Admin **and** Member) and `Tenancy.Currency.Manage` (Admin only). Manage is
Admin-only by the standing rule — adding a currency changes what every document form offers and what
the general ledger's inputs are denominated in. View follows every other lookup's Member-View
default: a Member composing a foreign-currency invoice must be able to read the list their own
Currency picker is populated from, and that list carries no PAN, no identity and no per-transaction
row.

Reading a *document's* own currency needs **no new key** — those are two more header fields on a
document the caller already holds the View key for, exactly as `DiscountPct` was in 16b.

---

## What shipped

**Domain**
- `Common/CurrencyCatalog.cs` — 25 catalog entries, `BaseCode`, case-insensitive `Find`/`Contains`.
- `Common/ExchangeRates.cs` — `ToBase` (scale 2), `ToBaseUnitCost` (scale 4), `NormaliseRate`
  (scale 6), and `Validate`, which enforces both invariants for all twelve aggregates.
- `Tenancy/Currency.cs` — the tenant lookup, with the base currency protected from deactivation.
- `CurrencyCode` + `ExchangeRate` on Quotation, SalesOrder, Invoice, CreditNote, PurchaseOrder,
  PurchaseBill, Expense, DebitNote, JournalVoucher, CashTransfer, Payment and OpeningBalanceLine,
  plus `SetCurrency` on the eleven with a Draft lifecycle.
- `TenantSettings.DefaultForexGainAccountId` / `DefaultForexLossAccountId`.

**Application**
- `Tenancy.Commands.CreateCurrency` (with the cap), `UpdateCurrency`, `Queries.ListCurrencyCatalog`;
  `RegisterLookupHandlers<Currency>` gives List and Delete for free, and `DeleteLookupCommandHandler`
  gained the one type-test that refuses to delete the base currency.
- `Accounting.Posting.ForexAccountResolver` and `GlCurrencyConversion`.
- `Payments.Posting.PaymentForexCalculator` + `PaymentForexInput`, and `PaymentPostingRule`'s
  appended forex pair.
- `Common.Currencies.ICurrencyBearingCommand` (a marker for the sweep guard) and
  `CurrencyValidationRules.AddCurrencyRules`, wired into all twenty-three validators.
- `CreateOrganizationCommandHandler` seeds the base currency row.
- The fold, in nine Approve handlers.

**Infrastructure** — `CurrencyConfiguration`, the twelve document configs' new columns (SQL defaults
plus `ValueGeneratedNever`), four permission-seed rows, and the `MultiCurrency` migration with a
hand-written backfill of the base-currency row for every existing Organization.

**Api** — five currency endpoints on the Organization group (list, catalog, create, update, delete),
the pair on twelve request records, and the two forex accounts on the accounting-defaults record.

**Web** — `shared/currency/currency-rate-fields` (the Currency + Exchange Rate control, with the
base-currency pin), `features/organizations/currency-list-page`, the route behind
`featureGuard('MultiCurrency')`, a feature-conditional dashboard link, and the control wired into
all eleven transactional detail pages.

---

## Manual E2E

Fresh Organization (`Phase28 Forex Co`, MultiCurrency **on**), master data seeded by curl + cookie
jar with **every status code printed**, per phase-26c's lesson. A second Organization
(`Phase28 NoForex`, MultiCurrency **off**) for the cap.

| Proof | Result |
|---|---|
| Activate USD on the flag-on tenant | `201` |
| Activate USD on the flag-**off** tenant | `403` — *"does not have the Multi-Currency Support feature enabled, so it is limited to NPR only"* |
| The flag-off tenant's seeded list | exactly `[('NPR', 'Nepalese Rupee', True)]` |
| USD invoice, 100 USD @ 133 → `sqlcmd` | `CurrencyCode=USD`, `ExchangeRate=133.000000`, line `Amount=100.0000` |
| …its GL entry → `sqlcmd` | AR **13300.0000** Dr / Sales Revenue **13300.0000** Cr |
| USD receipt, 100 USD @ 130 → `sqlcmd` | `CurrencyCode=USD`, `ExchangeRate=130.000000`, `Amount=100.0000` |
| …its GL entry → `sqlcmd` | Cash **13000.00** Dr, AR **13000.00** + **300.00** Cr, **Forex Loss 300.00 Dr** |
| …does it balance? | `13300.0000` = `13300.0000` |
| **AR net movement across both documents** | **`.0000`** — the control account is left flat, which is the point of the whole rule |
| Invoice PDF | `Exchange Rate To NPR: 133` header field, bare line amounts, `Grand Total  USD 100.00`, no NPR column |

**The negative path.** A second user was registered, verified (`request-verification-code` first —
phase-27b finding #4) and invited as **Member**, whose `Tenancy.Currency.Manage` is seeded
`granted: false`. As that Member, **against a nonexistent currency id**:

```
403 {"title":"You do not have permission to perform this action (Tenancy.Currency.Manage).","status":403}
```

and the same call as Admin against the same nonexistent id returns `404 {"title":"Currency not
found."}` — so the 403 is authorization firing *before* the handler, not a masked 404. The positive
half: the same Member's `GET /currencies` returns `200`, because `Tenancy.Currency.View` is granted.

**Browser pass** (`erp-web-ssl` + the `erp_auth` cookie transplanted via `document.cookie` —
phase-25 Step 3's recipe):

- **Currencies page:** NPR renders with a "Base currency" badge, an Edit button and the text
  *"NPR cannot be removed"* in place of a Remove button; USD renders with both. The Add New Currency
  picker lists the catalog minus what is already activated.
- **A complete round-trip through the real UI:** choosing `EUR — Euro` pre-filled Name `Euro` and
  Symbol `€` (the live dialog's behaviour), Add fired `POST …/currencies → 201`, the row appeared,
  EUR left the picker, and `sqlcmd` shows `EUR | Euro | € | 1` in `tenancy.Currencies`.
- **New Invoice form:** Currency lists `EUR / NPR / USD`, defaults to NPR, and the Exchange Rate
  input is **disabled at 1** with the note *"A document in NPR always has a rate of 1."* Selecting
  USD **enables** it and drops the note.
- **The approved USD invoice's detail page** shows `USD — US Dollar` and `133`, both disabled
  because the document is Approved — the DTO round-trip end to end.

---

## Bugs and snags hit

**1. `ErpApp.Application.Common.Currency` collided with the `Currency` type.** `DbSet<Currency>`
failed with *"'Currency' is a namespace but is used like a type"*. Renamed to `…Common.Currencies`.
Phase-13's "never name a Domain type after a common word" lesson, arriving from the namespace side:
the type was fine, the *namespace* was the problem.

**2. `tsc --noEmit -p tsconfig.json` does not typecheck the Angular app.** It returned clean while
`ng build` reported **22** `TS2339` errors for the `currencyCode`/`exchangeRate` properties that did
not yet exist on the detail models. CLAUDE.md's exit bar lists `tsc --noEmit`; on this repo it is
strictly weaker than `ng build`, which is the check that actually covers `src/app`.

**3. The `cat > file <<'EOF'` heredoc truncation, again.** A ~14 KB test file came back as
`unexpected EOF while looking for matching '`. The standing gotcha says use the Write tool; this
phase re-confirmed the limit is well below any comfortable margin.

**4. `identity` is a reserved word in T-SQL.** `SELECT … FROM identity.VerificationCodes` fails with
*"Incorrect syntax near the keyword 'identity'"*; it needs `[identity].VerificationCodes`. Worth
knowing for any future E2E that reads a verification code — which is every E2E that needs a Member.

**5. Two Api request-record shapes had to be read, not guessed,** to get the seed script green:
`CreateAccountRequest` takes `GroupId` (not `accountGroupId`), and `CreateProductRequest` takes
`CategoryId`/`PrimaryUnitId`/`ReOrderLevel`/`AvailableForSale`. A wrong field name yields a bare
`400`, and an empty `$PROD` then makes the *next* request fail with
*"Failed to read parameter … as JSON"* — a Guid, not the JSON, is what fails to bind.

---

## Known limitations / follow-ups

- **The allocation posting rule is reasoned, not live-confirmed** (Decision F). Re-verify when the
  reference product's currency catalog picker works.
- **Unrealised revaluation is unbuilt** (Decision A) — no period-end run, no unrealised account.
- **Cross-currency settlement is rejected, not supported** (Decision F): a payment can only be
  allocated to documents in its own currency.
- **No rate source.** Rates are typed by the user on each document, exactly as in the reference
  product. Nothing fetches or suggests one.
- **`ListAllocatablePayments` and the Allocate screens do not filter by currency.** A user can reach
  a cross-currency allocation and will be refused at Approve with a clear 409 rather than being
  prevented from choosing it. Filtering that list is the cheaper half of the same fix and belongs
  with whichever phase revisits allocation.
- **`ApplyPaymentAllocationCommand` posts no forex leg.** Allocating *further* against an
  already-Approved payment (phase-17's Allocate screens) adds a `PaymentAllocation` row without
  touching the GL at all — pre-existing behaviour, not introduced here — so a forex difference
  arising on that path is not booked. Approve-time allocation, the common path, is fully covered.
  Worth pairing with the currency filter above.
- **The `MultipleWarehouses` route guard, noticed in passing:** `organizations/:id/warehouses` is
  behind `featureGuard('MultipleWarehouses')`, so a flag-off tenant cannot reach the page to create
  its *first* warehouse — which phase-20f Decision #4 says it must be able to do, and which
  Invoice/PurchaseBill require. The server-side cap is correct; only the client guard is stricter
  than intended. Not fixed here (out of this phase's scope), and flagged so it is not lost.
