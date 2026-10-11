# Phase 82 — a discarded dish takes its share of the order's discount with it

## TL;DR

**A dish carrying a frozen share of an order's Amount discount can now be discarded.** Phase 74 froze each dish's
share of an order's rupee discount at the order's first bill, and from then on refused to discard any dish carrying
one. Phase 77 let a transfer and a parcel take the share with the food, but kept the discard refused because the share
had "nowhere honest to go" (phase 77 § 5). A restaurant could not take a sent-back plate off a part-billed tab. The only
way round was to bill it and refund it: a sale and a credit note in the VAT records for food nobody ate.

The user's decisions (2026-10-11), each the recommended option:

- **A — the share leaves with the dish.** The order's amount falls by what the discarded quantity carries
  (`ShareCarriedBy`, phase 77's rule for a transfer and a parcel), and every other dish keeps the share and the price
  it was frozen at. This gives exactly the figures that billing the dish and refunding it gave, without the two tax
  documents; a Domain test asserts the equality. Before the first bill nothing changes: the amount is still a term of
  the tab, spread over whatever is on it, so a discard leaves it whole.
- **No live read.** Phase 74 had already read the vendor's restaurant split with writes: it puts the whole order
  amount on the first bill, so it has no per-dish share and a later discard touches no discount. There was nothing to
  copy, and another pass would only repeat that.

| | |
|---|---|
| Live read taken | **no**, by the user's choice (phase 74 § Live read 7 already holds the vendor's behaviour) |
| Migration | **none** (no schema change) |
| New permission keys | **none**. A discard is still `Pos.Order.Void` (Admin-only, phase 64 Decision F): no dish's price changes, the tab loses food |
| Changed endpoints | none in shape. `POST /pos/orders/{id}/discard` now accepts what phase 74 refused, and the order it returns carries the lowered `discountAmount` |
| Changed screens | the order page's discard panel says, for a dish carrying a frozen share, that the share leaves with it (joined to the quantity by `aria-describedby`); the message after a discard names what the order's discount fell by, from the server's two figures |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged |

**Three things worth carrying forward.**

1. **When a refusal has a legal way round, the way round's figures are the specification for lifting it.** "Bill it
   and refund it" was always allowed, so its arithmetic was already what the business accepted. The discard was built
   to equal it, and a test holds the two to the same bill lines. Re-spreading the share over the rest of the tab, the
   alternative, would have re-priced dishes after the freeze, which no existing path does.
2. **Read a share before the ticket that changes the quantity it is part of.** `ShareCarriedBy` divides by the line's
   unbilled quantity, which is derived from the tickets. The discard collects every item's share in the validation
   loop, then tickets, then gives the shares up, as `MarkTakeAway` does.
3. **A figure the browser cannot work out can still be named afterwards.** What a discard takes depends on the dish's
   bills, which the page does not hold, so the panel says the rule in words and the message reports the difference of
   two server figures (before and after). Phase 63's rule was about a figure someone pays against; this one is only
   reported.

Tests: Domain **1019** (+5), Application.UnitTests **1760** (+1), Infrastructure.UnitTests 14, Api.IntegrationTests 71,
Angular **844** (+2, 102 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and
`ng test` are green.

---

## 1. What shipped

- **Domain (`PosOrder.Discard`).**
  - Takes the billed state (`IReadOnlyDictionary<Guid, PosOrderLineBilled>`: quantity and discount share per line)
    instead of quantities, as `Transfer` and `MarkTakeAway` did in phase 77. The "at most what is unbilled" refusal reads
    the quantity from it.
  - `EnsureShareStays` is gone (the discard was its last caller).
  - When the order's shares are frozen, each item's share is read with `ShareCarriedBy` before the cancellation ticket,
    then `GiveUpShare` takes it off the line's frozen share and off the order's `DiscountAmount`. Discarding everything
    a line has left unbilled takes everything its bills left of its share, so the line's frozen share ends equal to what
    its bills took.
  - Before the first bill nothing is read or given up: the amount stays whole and re-spreads over what is left.
- **Application.** `VoidPosOrderItemsCommandHandler` passes `PosOrderBilling.LoadAsync(...).Lines` and settles from its
  `Invoiced`.
- **Order page.** `discardCarriesShare(order, line)` is true when the order has an amount, the line carries a share,
  and some line is invoiced (the shares are frozen exactly then: they freeze at a bill and are released when every bill
  is voided). The panel then shows the dish's share and the rule. `panelNotice` takes the order as it was before the
  request, and a discard's message names `before.discountAmount − saved.discountAmount` and the new amount.

## 2. Scope decisions

The user's (2026-10-11):

- **Decision A: the share leaves with the dish** (recommended).
  - Every remaining dish costs what it did, as in phase 68 (rates) and phase 77 (shares): the frozen spread is not
    re-opened.
  - It equals bill-and-refund. A refund gives back a billed dish at its net, its share included, so the guest's discount
    on that dish was lost there too.
  - *Rejected: re-spread over the tab.* The guest keeps the whole promised amount, but dishes already frozen (some
    part-billed) are re-priced after the first bill. If nothing unbilled remains, the share vanishes anyway.
  - *Rejected: the share stays on the line's remaining units.* Those units get cheaper than the same dish already
    billed, and discarding the last unbilled unit would still have to be refused.
- **Decision B: no live read** (recommended). Phase 74 § Live read 7 (with writes): the vendor's split bill puts the
  whole amount on the first bill and returns the order with `discount: 0`, so a discard after it has no discount to
  touch. Its model has no per-dish share. Ours is a decision, not a copy.

Decided in the session, not asked:

- **Decision C: no new key.** Discard stays `Pos.Order.Void`. It lowers a total only by removing food, and each dish
  left keeps its price, so no till price key applies (phase 76's keys are about a price, and a discard changes none).
- **Decision D: before the first bill, a discard is unchanged.** The amount is a term the cashier can still edit (the
  `PUT .../discount` is open until a bill), spread live. Making a pre-bill discard lower it too would take a promised
  discount away from a tab nobody has paid any of.
- **Decision E: no browser preview of the figure.** The share a quantity carries depends on the dish's bills
  (phase 77's remainder rule). The panel states the rule and the dish's whole share; the server's answer gives the
  figure.
- **Decision F: a void of every bill afterwards does not restore the amount.** `ReleaseDiscountShares` unfreezes the
  spread, and the amount stays what the discards left. With nothing billed the cashier may set it again. Phase 77's
  transfers already behave this way.

## 3. Bugs found and fixed

None in the product. Two in the session's tools, both known gotchas:

- A Python heredoc in the Bash tool turned the spec's `\'` into a bare `'`, breaking three string literals
  (CLAUDE.md's heredoc gotcha). Fixed with the Edit tool, using double-quoted strings.
- `nvm use 24.11.0` deleted `C:\nvm4w\nodejs` again; it was recreated with `mklink /J` as CLAUDE.md says.

## 4. Evidence

**Tests.**
- Domain (`PosAmountDiscountTests`, 5 new and 1 rewritten):
  - Before the first bill a discard keeps 52, spread 45.22 / 6.78 over what is left.
  - After the first bill (one momo billed), one Coke of two takes 6 of its 12; the bills then take 20 and 6, and the
    order's 46 is what they took.
  - Both dishes' unbilled rest in one discard takes 20 and 6; the order keeps 26 and settles.
  - The rounding case: 10 over three momo, 3.33 billed. The discarded one takes 3.34 (3.335 away from zero), and the
    last bill takes the 3.33 left, not a second 3.34.
  - A discard leaves the rest billed exactly as billing the Coke alone and refunding it would (same quantities, figures
    and shares on the last bill).
  - The phase 74 test keeps its fixed-amount and later-dish assertions; a later dish's discard leaves the amount at 52.
- Application (`PosAmountDiscountHandlerTests`): on the InMemory store, one of each billed, the other momo discarded
  (32; shares 20 / 12, still Open; the rest's preview 6), then the last Coke discarded (26, Settled). The stored amount
  equals the sum of the invoices' discounts.
- Angular (`pos-order-page.spec.ts`, 2 new): the note and its `aria-describedby` on a billed tab, the request sent, and
  "fell by 20.00 to 32.00"; no note and no `aria-describedby` before the first bill.

**E2E on SQL Server** (fresh organization *Phase 82 Discard Share bb7580*: HeadOffice in Restaurant mode with a
warehouse, four tables, momo 200 with 10% service charge, Coke 60, cash on a Cash account). Two earlier runs of the
script made organizations *a3c8af* and *587357* and stopped at the seed (a payment mode needs a Cash or Bank account; a
restaurant bill needs a location warehouse); they hold no orders.

- **A (T1):** 2 momo, 2 Coke, Rs 52 off; live shares 40 / 12. The first bill (one of each) was 285 and took 26.
  Discarding the other momo returned `discountAmount` 32, still Open; SQL showed shares 20.00 / 12.00. The rest
  billed 61 with 6 off. SQL: ORD0001 Settled at 32.00; invoices `0001` 26.0000 (total 285, round-off .24) and `0002`
  6.0000 (61, −.02), each total equal to its lines' shares.
- **B (T2):** 3 momo, Rs 10 off, one billed (3.33). The discard took 3.34 and left 6.66 (SQL share 6.66); the last bill
  took 3.33. Invoices 3.3300 + 3.3300 = 6.66, the order's amount.
- **C (T3):** Rs 52 off, nothing billed. Discarding a Coke kept 52, live shares 45.22 / 6.78, SQL shares NULL.
- **The 403-not-404 pair.** `POST /pos/orders/{nonexistent}/discard` with a valid body (one item with a random
  `lineId`, quantity 1, a reason) was **404** "Order not found." as Admin. From a role holding `Pos.Order.View`,
  `Pos.Order.Operate` and `Tenancy.BillingLocation.View` it was **403** "…(Pos.Order.Void)", beside that role's 200 on
  `GET /billing-locations`. Admin was restored (200 on the order).
- **Keyboard (browser pass, https dev server at 1440×900, cookie transplanted through `web/public`; the file was deleted
  at once).** On ORD0004 (T4, Rs 52 off, one of each billed), focus was put on *Take Away Chicken Momo*, and from there
  only keys were used:
  - Tab to *Discard Chicken Momo*, then Enter. The panel opened with focus on the quantity (1), whose
    `aria-describedby` names the note: "This item carries 40.00 of the order's discount, fixed at the first bill. What
    you discard takes its part of that with it, so the order's discount falls by as much."
  - Tab, typed "Sent back cold", then Enter. The discard returned 200. The banner read "Discarded; the kitchen has a
    cancellation ticket. The order's discount fell by 20.00 to 32.00." Focus went back to *Discard*, and the summary
    showed *Discount (32.00 on the order)*.
  - Read back in SQL: ORD0004 at 32.00 with shares 20.00 / 12.00, and one Cancellation ticket of −1 with the reason
    "Sent back cold".
- No PDF changed.

## 5. Left open, and for later phases

- **The discard panel's maximum is the line's whole quantity**, not what is unbilled ("Discard (of 2 on the order)"
  with one billed). The server refuses the excess with a 409 naming what is billed. This is phase 65's behaviour,
  noticed in the keyboard pass and not changed here.
- **A discount on one guest's part after splitting has begun** (phase 72 § 5) is unchanged.
- **Carried unchanged:** phase 81 § 5, phase 80 § 5, phase 79 § 5, phase 77 § 5's other items (the vendor's server-side
  transfer handling), phase 76 § 5, phase 75 § 5, phase 72 § 5, phase 71 § 5, phase 70 § 5, phase 69 § 5, phase 67 § 5,
  phase 66 § 5, phase 63 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The vendor was not opened this phase.
