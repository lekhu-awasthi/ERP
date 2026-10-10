# Phase 77 — The share moves with the food, and the export carries the till's money

## TL;DR

**Two things, finished from phase 74 § 5 and the export gap carried since phase 61.**

1. **An order's Amount discount now moves with its food.** Phase 74 refused to transfer items to or from an order
   carrying a rupee discount, and refused to parcel a dish with a frozen share once the order was billed. Now:
   - A transferred quantity takes its share of the discount. The source's amount falls by that share and the
     target's rises by it, so the two discounts still add up to what was given.
   - After the first bill, the share comes out of the dish's frozen share: the quantity's part of what the dish's
     bills have not yet taken.
   - *Mark as Take Away* moves the share to the parcel, and the order's amount is unchanged.
   - Discard is still refused for a dish carrying a frozen share. Its share would have nowhere honest to go.
2. **The full-tenant export carries every part of a sale's money and every rupee a till handled.**
   - *Sales Documents* gains nine columns, appended after phase 38's fourteen: Channel, Order No, Against Invoice,
     Discount per Unit, Document Discount %, Bill Discount, Service Charge, Packaging Fee and Round Off. Each
     document's round-off is a row of its own.
   - *Payments* gains till tenders, the change a sale gave back, and refund payouts, with a Source column saying which
     kind of row each is.

The user's decisions (2026-10-11), each the recommended option:

- **A:** the share moves with the food.
- **B:** the parcel is included; discard stays refused.
- **C:** both sheets (Sales Documents and Payments).
- **D:** a restaurant bill names its order on the sheet. There is no POS Orders category.

| | |
|---|---|
| Live read taken | **bundle only**, 2026-10-11, abcagro1 (14 days left). Its restaurant has no tables, so no transfer could be run without creating a floor; § Live read |
| Migration | **none** (no schema change) |
| New permission keys | **none**. A transfer is still `Pos.Order.Operate`, and the export keeps its own key |
| Changed endpoints | none in shape. `POST /pos/orders/{id}/transfers` and `.../take-away` now accept what phase 74 refused. `POST /export-jobs` produces wider *Sales Documents* and *Payments* sheets |
| Changed screens | none. The order page's Transfer Items and Take Away were never gated in the browser; the server's 409 was the gate |
| Changed print output | none |
| Initial bundle | **652.58 kB**, unchanged |

**Three things worth carrying forward.**

1. **A till refund names its sale through `ReferrerType`/`ReferrerId`, not `AgainstInvoiceId`.** Phase 69's field is
   for an ERP note's picked invoice. A reader joining a credit note to "its invoice" by that field alone misses every
   till refund. The first export test caught it: the *Against Invoice* column was empty for a refund.
2. **A moved share is read from the bills, not prorated over the whole line.** Once a line has been part-billed, what
   its unbilled food carries is the frozen share less what its bills took (`PosOrderLineBilled.BillDiscountAmount`).
   Moving all of that food takes all of that remainder. That is the rule a bill's parts already follow. Prorating
   over the whole line instead leaves the line's share a paisa away from its bills.
3. **Every phase that adds money to a line owes the export a column.** Service charge (61), round-off (61), packaging
   (70) and the Amount discounts (74) each arrived without one, and each status doc recorded the gap. A reflection
   guard now makes the next one decide: `TillExportTests` requires every `decimal` on `InvoiceLine` and
   `CreditNoteLine` to be in `SalesDocumentExportReader.ExportedLineMoney` or named with a reason.

Tests: Domain **995** (+4), Application.UnitTests **1714** (+6), Infrastructure.UnitTests 13, Api.IntegrationTests 57,
Angular 833 (101 files). `dotnet build`, all four .NET suites (Docker up, one at a time), `ng build` and `ng test` are
green.

---

## Live read, 2026-10-11 (abcagro1, *Abc Agro Trading*)

The vendor tenant had 14 days left. The POS app was reached as in phases 74–76: `window.open` was replaced, *Open
Pos* clicked, and the captured URL followed in the same tab; its hash was never printed. The page's XHR was logged
and 1440x900 emulated.

1. **The restaurant has no tables** ("No tables created yet"). A transfer needs two tables. Creating a floor and
   tables would have been three writes for one behaviour, so the bundle was read instead. The user was told this
   when the plan was put to them.
2. **The client sends no discount on a transfer.** `POST {ORDERS}/{id}/transfer-items` carries
   `{order_id, table_id, area_id, items: [{id, quantity}]}`. On success the page stores the *source* order's own
   `discount` and `discount_type` from the response. So the vendor's discount is a figure of the whole order, and it
   stays where it is. That fits phase 74's reading that its whole amount goes on the first bill.
3. **What its server does with an amount on a transfer is unread.** Ours is a decision (A), not a copy. The vendor's
   model has no per-dish share to move.

No write was made to the vendor tenant.

## 1. What shipped

- **Domain (`PosOrder`).**
  - `Transfer` takes the source's billed state (`IReadOnlyDictionary<Guid, PosOrderLineBilled>`: quantity *and*
    discount share per line) where it took quantities. The amount refusal is gone.
  - Each item's share is read before the tickets move anything. Then `GiveUpShare` takes it off the source and
    `TakeShare` puts it on the destination line. Both adjust the order's `DiscountAmount`, and the line's frozen
    `OrderDiscountShare` too when that order's shares are frozen.
  - `ShareCarriedBy` gives what a quantity carries. Before the first bill it is the quantity's part of the line's live
    share. After it, it is the quantity's part of (frozen share − billed share), and moving all the unbilled food
    takes all of that.
  - `TakeTermsFor` treats an Amount as the same terms as another Amount (the figures need not agree). An empty target
    takes the source's kind, and the food brings the figure. `DescribeTerms` names "a discount of 30.00".
  - `MarkTakeAway` takes the billed state too. It moves the parcelled quantity's share inside the order and no longer
    calls `EnsureShareStays`. Discard still does.
- **Application.** `CreatePosOrderTransferCommandHandler` and `UpdatePosOrderTakeAwayCommandHandler` pass
  `PosOrderBilling.LoadAsync(...).Lines` (which already carried each line's billed share).
- **`SalesDocumentExportReader`.**
  - Four queries, each ordered and capped on its own: invoice lines, credit-note lines, invoice round-offs and
    credit-note round-offs. They are merged in memory by the sheet's ordering, with a document's round-off row after
    its lines, and the total is the sum of the four counts. No set operation is used: the round-off rows would need
    typed null constants in a `Concat`, which this codebase has never proven on SQL Server.
  - A credit note's figures are negated; its rates and percentages are not.
  - *Order No* comes from the bill's `PosOrderId`. For a credit note it comes from the invoice it names.
  - *Against Invoice* is phase 69's number, else the referrer invoice's code.
  - `ExportedLineMoney` lists the line properties the sheet carries.
- **`PaymentExportReader`.**
  - Payments, tenders, change and payouts are read separately and merged.
  - A tender is a Received row numbered by its invoice, in its mode.
  - Change is a Paid row in the mode of the sale's cash tender: change only ever comes out of cash tendered.
  - A payout is a Paid row numbered by its credit note.
  - The *Source* column says `Payment`, `Till Sale`, `Till Change` or `Till Refund`.

## 2. Scope decisions

The user's (2026-10-11), each the recommended option:

- **Decision A: the share moves with the food.** A dish costs the same on either table. That is phase 68's rule for
  rates, applied to the order's discount. "The same terms" for an amount is the kind:
  - a target with a percentage, or a non-empty target with no discount, is a 409 naming both orders (phase 72's
    rule);
  - an empty target takes the source's kind and waiver.
  - Before the first bill, a target's amount is spread over its dishes as always. So the moved dish's own share may
    shift on the new tab, but the tab's discount rises by exactly what the food brought.
  - *Rejected: the amount stays on its tab.* The vendor's apparent behaviour. A merged table would lose its discount
    whenever the emptied order voided.
  - *Rejected: keep refusing.*
- **Decision B: the parcel too; discard stays refused.** A parcel is a move inside one order, so the same mechanism
  applies and the order's amount is unchanged.
  - A discard has no destination. Its share could only vanish (the guest loses discount on food they did not eat,
    which may be right but is a policy) or pile onto the rest of the line.
  - *Bill it and refund it* still works for a discard.
- **Decision C: Sales Documents and Payments both.** The Payments sheet was missing every rupee a till took. A tenant
  exporting "money received" saw only `Payment` rows, while Ledger Transactions had the tenders as account lines.
- **Decision D: Order No on the bills; no POS Orders category.** An order's terms are already in its bills' lines:
  each line's Bill Discount, and a zero service charge when waived. An unbilled tab is open for a day at most.

Decided in the session, not asked:

- **Decision E: new columns are appended**, not interleaved, so a reader that located phase 38's columns by position
  still finds them. The cost is that the money columns are not adjacent.
- **Decision F: round-off is a row of its own.** On a line it would make that line say something untrue; repeated on
  every line its column would sum wrong. The row has no product, and every line figure is zero.
- **Decision G: *Against Invoice* is added too.** It was phase 69 § 5's "the export has no invoice column", and it
  costs one join.
- **Decision H: change is a Paid row in the cash tender's mode.** The alternative was netting it into the cash tender.
  That would have shown a tender of 396 that nobody handed over (500 tendered, 104 back).

## 3. Bugs found and fixed

- **The *Against Invoice* column was empty for a till refund.** Phase 63's refund records its sale as
  `ReferrerType = Invoice` / `ReferrerId` (as a conversion does) and leaves phase 69's `AgainstInvoiceId` null. The
  reader joined on `AgainstInvoiceId ?? (ReferrerType == Invoice ? ReferrerId : null)`. Caught by the first run of
  `TillExportTests`; the E2E shows `0006` on the refund's rows.
- Tooling: the cookie file for the browser pass 404'd because `ng serve` serves `public/` as it was at start-up (phase
  71's gotcha). The preview was restarted, and the file deleted as soon as the cookie was set.

## 4. Evidence

**Tests.**
- Domain: a transfer from an unbilled tab (`32 + 20`, the dish's figures identical, the two estimates adding to the
  original); the kinds rule (no discount and percentage refused both ways, an amount joining an amount); a transfer
  from a billed tab (shares `20/12`, then the bills `26 + 6 + 20 = 52`); a partial move into a billed target (frozen
  20 arriving, target `26 + 20 = 46`); a parcel after the first bill (`20/20`, order still 52, bills total 52).
- Application:
  - Both handlers on the InMemory store, the transfer's three bills `26, 6, 20`.
  - The export on a real till sale and refund. Σ(Amount + SC + PF + VAT + Round Off) is 633 on the invoice and −68 on
    the refund, and the refund names its sale.
  - Tenders, change and payouts: received less paid is 565.
  - A restaurant bill's Order No and Bill Discount shares (12, 40).
  - The reflection guard.

**E2E on SQL Server** (fresh organization *Phase 77 Shares and Export*, one Restaurant location with six tables,
momo 200 with 10% service charge, Coke 60, packaging 20 a unit):

- **A (T1):** 2 momo, 2 Coke, Rs 52 off; live shares 40/12. The first bill (one of each, 285) took 26.
- **The transfer.** The other momo moved to T2 and opened ORD0002 with `discountAmount` 20 and a total of 223.74. A
  fell to 32.
  - In SQL, A's lines are 20.00 and 12.00 and ORD0002's 20.00.
  - The bills are `0001` 26, `0002` 6 and `0003` 20: 52 over both orders.
- **C (T3):** Rs 52 off. One Coke was billed, then a momo parcelled. The order kept 52, with shares 20 / 12 / 20
  (parcel). The parcel's bill line was 180 + 0 SC + 20 packaging + 26 VAT, and C's bills sum to 52.
- **Refusals.** Rs 30 off into a tab with no discount was a **409**: "Order ORD0006 is priced with no discount and
  service charge, and order ORD0007 with a discount of 30.00 and service charge…". A 5% tab into an amount tab was a
  **409** too.
- **The 403-not-404 pair.** `POST /pos/orders/{nonexistent}/transfers` was **404** "Order not found." as Admin. From a
  role holding only `Pos.Order.View` + `Tenancy.BillingLocation.View` it was **403** "…(Pos.Order.Operate)", beside
  that role's 200 on `GET /billing-locations`. Admin was restored afterwards.
- **The export.** `POST /export-jobs?categories=SalesDocuments&categories=Payments` completed: 27 rows, no truncation.
  The downloaded .xlsx was read from its XML.
  - *Sales Documents:* 18 rows. Σ(Amount + Service Charge + Packaging Fee + VAT + Round Off) = **1,674.00**, and SQL's
    invoices less credit notes (lines plus round-off) = **1,674.0000**. Bill Discount 130.54, packaging 20.00,
    round-off 0.30. Every bill names its order, and the refund names `0006`.
  - *Payments:* tenders 285, 61, 224, 61, 511 and 200 (card) + 500 (cash) on `0006`. Change of 104 is Paid in Cash 77
    and the refund payout of 64 is Paid. Received less paid = **1,674.00**, and SQL's tenders − change − payouts =
    **1,674.0000**.
- **Keyboard (browser pass, https dev server, cookie transplanted and the file deleted at once).**
  - On ORD0007 (T2, Rs 30 off), the keys were: Tab to *Transfer Items*, Enter (focus lands on *Transfer to*),
    ArrowDown to "T1 · free: opens a new order", Tab, Space on *Chicken Momo*, Tab, Tab to *Transfer*, Enter.
  - The page moved to ORD0008 at T1 carrying *Discount (30.00)* and the same 211.31 estimate, with the banner
    "Everything moved to T1 (ORD0008): ORD0007 is closed." Focus stayed on *Transfer Items*, with its ring visible.
- No PDF changed.

## 5. Left open, and for later phases

- **Discarding a dish that carries a frozen share** is still refused (Decision B). If a restaurant wants it, decide
  whether the share leaves with the food (the order's discount drops) or stays on the line.
- **On a target not yet billed, a transferred dish's own share re-spreads** with the target's amount (Decision A).
  The tab's figure is exact; the dish's is not frozen until the first bill, as for any dish.
- **The vendor's server-side handling of an amount on a transfer** is unread (§ Live read 3).
- **The export's other gaps.**
  - A drawer's cash in/out (`PosSession` movements) is not on the Payments sheet. These are transfers between
    accounts with no contact; Ledger Transactions has them.
  - The Purchase Documents sheet still has phase 38's fourteen columns.
- **Carried unchanged:** phase 76 § 5, phase 75 § 5 (and the phase 73 § 5 it carries), phase 74 § 5's remaining
  items (a dish carrying a share cannot be discarded; older refunds are matched by key), phase 72 § 5 (a discount on
  one guest's part after splitting), phase 71 § 5, phase 70 § 5 (less its export item, done here), phase 69 § 5 (less
  its export column, done here), phase 67 § 5, phase 66 § 5, phase 63 § 5 and phase 62 § 5.

## Writes made to the vendor tenant

None. The POS app's dine-in screen and its JavaScript bundle were read; nothing was created, saved or deleted.
