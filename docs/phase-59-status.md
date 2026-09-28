# Phase 59 — POS scoping: the vendor's till, read and written, and the plan to build ours

## TL;DR

**No code was written. This phase read the reference product's POS end to end and planned phases
60–66 from what it does, not from its screens.** It covers the same backend, the same tenant (*Hamro
Samaan*, the phase 58 tenant) and a full restaurant service: a session opened with a note count, a
table, two KOT rounds, a split bill, multi-tender payment, a refund, a cash-out, and a session closed
9 rupees short. Every step was checked against what the ERP side shows and posts
(`erp-module-scan.md`, "POS, read and written").

| | |
|---|---|
| Live read taken | **yes**, 2026-09-28, *Hamro Samaan* trial, **with writes** (user-authorised; listed at the end) |
| How it is built | a **separate Next.js (React + Chakra UI) SPA** at `/pos`, 99 routes, **no service worker** (no offline mode) |
| How it integrates | the **same API** (`api-v2.tigg.app/api/v1/erp`, `?channel=POS`), same tenant, same numbering, same GL |
| New POS permission keys | **8** (`floor-plan-*`, `location-settings-*`); everything else reuses ERP keys |
| New POS aggregates in the vendor | session, area/table, KOT, print profile, payment mode, discount scheme, delivery partner |
| Documents a POS sale produces | **ERP ones**: Sales Order (the open order), Invoice (the sale), Credit Note (the refund) |
| Vendor defects found in one service | **9** (Decision list below). Two understate tax and revenue |
| Phases planned | **7** (60–66), plus 6 start-conditioned items outside the sequence |
| Code changed | **none** |

**The headline is that the vendor's POS is an ERP client, not a second product.** It has its own
screens and its own login, but no ledger or document types of its own. A dine-in order is a Sales
Order that is Approved and numbered the moment it is saved. Paying produces an ordinary Approved
Invoice whose GL entry holds the sale **and** its settlement. A refund is an ordinary Credit Note.
The POS adds only what a till needs and an ERP lacks: sessions and a cash drawer, tables, KOTs,
print stations, payment modes, per-location service charge and rounding, and promotions.

**So ours should be one app, not two.** Build the till as a lazy-loaded full-screen route tree
inside the existing Angular app, on the existing cookie and API, and reuse Invoice and Credit Note
for what they already are. Add aggregates only for the concepts an ERP genuinely lacks. That one
choice removes the vendor's worst integration seam: POS's own "Accounting" link lands on the ERP's
login page.

---

## 1. How the vendor's POS is built

- **A separate SPA.** It is served at `<tenant>.tigg.app/pos` with path routing (the ERP uses
  `/erp/#/`). It is a Next.js static export (`nextExport: true`) using React, Chakra UI, redux,
  redux-persist and react-query. The build manifest lists **99 routes** (full list in the scan
  appendix).
- **Entry is a signed handoff.** `me.tiggapp.com`'s organisation list has *Open Tigg* and *Open Pos*
  buttons. Each `window.open`s a `validate-login?email&identity&hash&expiry_timestamp&namespace` URL
  on the tenant's host, one for `/erp/#/` and one for `/pos/auth/`. **The two apps do not share a
  session.** POS's "Accounting" nav item is a plain link to `/erp#/`, which lands on the ERP sign-in
  page.
- **The same backend.** Every call goes to `https://api-v2.tigg.app/api/v1/erp` with `channel=POS` on
  the query string. POS-only resources live under `/pos/*` (sessions, orders, kots, floorplans,
  areas, print-profiles, delivery-partners, general-settings/…, invoices/…/split-bill, credit-notes).
  Everything else is the ERP's own endpoint (`/sales-orders`, `/invoices`, `/products`, `/contacts`,
  `/payment-modes`, `/document-numberings`, `/general-settings`).
- **No offline mode.** There is no service worker, only redux-persist in `localStorage`. The cart
  survives a reload, and it also **leaks**: the Retail till opened with a Coke line left over from a
  refund at another location (defect 8).
- **Device binding.** `device_fingerprint` in `localStorage` plus `/verify-device` and `/resend-otp`
  form an OTP-per-device step at POS login.
- **Printing is the browser's print dialog.** There is one "Print KOT" per ticket, auto-print on
  settle, and `print_count`/`first_print_time`/`printed_by` are tracked on the invoice. There is no
  ESC/POS or direct printer path.
- **Live updates: none.** The KOT board has a manual refresh button. The one server push is the
  NepalPay QR status (`/nepalpay/qr/events/{id}`, `text/event-stream`).

## 2. How it works: one restaurant service, end to end

Settings used: POS Restaurant with a **10% service charge** credited to Sales Service, **round-off**
credited to Sales Goods, and **cash verification** on. Products are Chicken Momo (Service, 200,
service-charge-applicable, KOT station *Kitchen*) and Coke 250ml (Goods, 60, no service charge).

| Step | Screen | Wire | Result |
|---|---|---|---|
| Open session | Start Session: Amount / **Denomination** tabs | `POST /pos/sessions {location_id, denominations:[{value:500,count:2}]}` | session *In Progress*, one per user × location |
| Seat T1, add 2 Momo + 1 Coke, *Save Orders* | tiles priced **incl. SC and VAT** (Momo 248.60 = 200 × 1.10 × 1.13) | `POST /sales-orders {order_type:"Dine In", table_id, area_id, customer_count, contact_id:<Cash Customer>, items}` | **SO0002/1002/83-84, Approved at save**, `channel: POS`; table row gets `order_id` → *Occupied* with timer |
| (server) | KOT dialog, split **by print profile** (Kitchen / Default) | none (created server-side) | **one KOT per save per station**; the id is the line's `order_batch_id`; no KOT number (shows the SO code) |
| Add 1 Coke, *Update Order* | line menu gains *Mark as Take Away* / *Mark as Served* | `POST /sales-orders/{id}` with the **whole** item list and `original_quantity` | a third KOT carrying **only the delta** (Coke ×1) |
| Kitchen board | Pending / Served / All; filters KOT type, area, table; Order Summary | `POST /pos/kots/mark-as-served {items:[{id,quantity,batch_id}]}` | partial serve by quantity |
| Proceed to Payment | method tabs Cash/Card/E-Payment/Credit/Other; keypad; Grand Total **633** (632.80 rounded) | | |
| Split Bill (1 Momo + 1 Coke), pay 500 cash | Received 500, **Change 183** | `POST /pos/invoices/{order}/split-bill {items, payments:[{payment_method_id, amount:317, type:"Cash"}], change_amount:183, abbreviated_bill:true}` | **INV0002/1002/83-84**, Approved, referencing a **new SO0003**; SO0002's lines **rewritten** to 1 + 1 |
| Pay the rest: 194 on credit + 100 cash | | `POST /pos/invoices {payments:[{type:"Credit",amount:194},{type:"Cash",…,amount:100}]}` | **INV0003**, 294 (see defect 1) |
| Refund the Coke | *Initiate Refund* → items/qty + **Remarks\*** → payout on the payment screen | `POST /pos/credit-notes {referrer_id:<invoice>, payments:[{Cash, 68}]}` | **CN0002/1002/83-84**, Approved |
| Cash Out 100 "Vegetables" | amount + note, **no account** | `POST /pos/sessions/{id}/transaction {type:"Cash Out",amount:100,note}` | stored as `amount: 100000` (scaled integer) |
| Close, count 1,240 | review: *Drawer is short by Rs. 9.00*, note required | `POST /pos/sessions/{id} {status:"Closed", closing_amt:1240, note}` | Closed. **Nothing posted** |

**What the ERP shows for INV0002** (overview → GL Transactions):

| Account | Debit | Credit |
|---|---|---|
| Cash In Hand | 317.00 | |
| Cash Customer | 317.00 | |
| Sales Service (momo 200 + service charge 20) | | 220.00 |
| Sales Goods (coke 60 + round-off 0.60) | | 60.60 |
| Value Added Tax | | 36.40 |
| Cash Customer | | 317.00 |

The sale and its settlement are **one GL entry**, and the Allocations panel lists the invoice paying
itself. There is no Customer Receipt document. Service charge sits **inside the VAT base** (taxable
280 = 260 + 20). The round-off credits whatever account the location names. **No COGS leg for the
Coke**: the tenant's negative-stock setting is *Do nothing*, and the product had no stock.

## 3. The vendor's defects: what we do better (each is a decision, not a copy)

1. **A split bill drops the service charge from the remainder.** After the split, the order header
   reads `service_charge: 0`. INV0003 charged the momo **no** service charge, so the table was billed
   20 instead of 40, and VAT was 2.60 short. The payment screen still offered *Disable Service
   Charge*, so the till believed it was on. **Revenue and output VAT are understated.** Ours:
   service charge is computed per line from persisted inputs, and a split moves quantities, never
   rates.
2. **A split mutates the order.** SO0002's lines were rewritten from 2/2 to 1/1, and a *new* SO0003
   was created for the settled half. There is no invoiced quantity per line, and the order's own
   history is overwritten. Ours: an open order keeps its lines, and each carries an invoiced counter
   (phase 58's per-line-counter carry, solved here first).
3. **The receipt is headed "ESTIMATE BILL"** even for the settled tax invoice. It prints **no
   service-charge line** (20 folded into *Taxable 280*) and no round-off line. The credit note
   prints an empty *Mode Of Payment*. Ours: the header follows the document (Tax Invoice /
   Abbreviated Tax Invoice / Estimate), and service charge and round-off are separate lines.
4. **`abbreviated_bill: true` is sent and `AbbreviatedBill: false` is stored.** The flag does
   nothing observable.
5. **Credit is extended to the anonymous walk-in.** 194 sits receivable on "Cash Customer", a
   contact every till shares, with `credit_limit: 0` and a credit-limit setting of *Do nothing*.
   Ours: a Credit tender requires a named customer, and phase 31's credit control applies to it.
6. **The drawer never reaches the GL.** The opening float, Cash In/Out (no account field) and the
   close's over/short all live only on the session. Cash In Hand in the GL moved 317 + 100 − 68;
   the drawer moved that, −100 and −9. Ours: cash movements name an account and post, and the close
   posts over/short to a tenant-default account.
7. **Two numbers for one day.** The Day Report's *Total Sales* is **610.20** (unrounded, and
   inheriting defect 1). The session's *Sales Transactions* is **611.00**, and the invoices sum to
   611. Ours: both read one shared reader (phase 36's rule), with a test reading both.
8. **Client-state bugs.** The persisted cart leaked a line across locations. Saving a Retail order
   navigated to the **Dine-In screen of a different location**. The session record's `no_of_txn`
   stays 0. "Unsaved changes" guards fire on the till's own primary action.
9. **The kitchen note is stored in the product's `description`.** The client posts whole product
   objects as order lines (the "Less spicy" note arrives as `items[].description`).

Beyond defects, there are also **absences** we can choose to fill: no modifiers or add-ons (only a
free-text note), no live kitchen display, no offline mode, no direct thermal printing, and no manager
override for discounts, voids or refunds.

## 4. Scope decisions for our build

**A. One app.** The till is a lazy-loaded `/pos` route tree in `web/` with its own full-screen layout.
It runs on the same cookie auth and the same API, with no second login and no SSO handoff. It stays
out of the initial bundle (phase 42's `build-budget.spec.ts` must stay green). The till gets its
own `NavigationCatalog` section, and phase 34b's rule that every URL resolves applies.

**B. A POS type is a *mode of a location*, not a *kind* of location.** Live: the Locations form offers
exactly two POS types, **"Bar / Restaurant"** (`Bar`) and **"Retail"**, and **HeadOffice itself is
typed Retail**. The seeded "POS Restaurant" / "POS Retail" rows are ordinary locations with those
names. So `BillingLocationType.PosRestaurant/PosRetail` (phase 32's reserved members) model the wrong
axis. Add `BillingLocation.PosMode` (`None | Retail | Restaurant`), gated by
`TenantFeature.PosRetail/PosRestaurant`. Retire the two reserved members, since nothing branches on
them (the phase-32 comment says so). The mode decides the shell: Restaurant adds Dine In, Take Away,
KOT and the floor plan; Retail shows Retail and Delivery only.

**C. A sale is an `Invoice`, a refund is a `CreditNote`.** The vendor proves it, and our reports,
VAT registers, Annex 13, ageing and Transaction List then include POS sales for free. The additions
are `Channel` (Erp/Pos), `PosSessionId`, `OrderType`, per-line service charge, `RoundOff`,
`ChangeAmount` and **tender lines**. A POS invoice is created Approved in one command (numbered
at approve, so immediately; location-wise numbering from phase 32 applies unchanged).

**D. Tenders post as a second GL entry against the same source document**, not as rows mixed into
the sale entry and not as a separate Customer Receipt document. Phase 36 already made "one GL entry
per document" a habit rather than an invariant (`SourceDocumentGlEntries`), so Void reverses both.
`OutstandingDocumentReader` treats tenders as allocations, so ageing, Customer Receivable Summary and
the invoice's balance agree **by construction**. *Credit* is not a tender that posts. It is the
unsettled remainder, allowed only for a named customer (defect 5).

**E. The restaurant's open order is its own aggregate (`PosOrder`), not a Sales Order.** Our
`SalesOrder` has no per-line conversion counters and is a commercial commitment, not an open tab
that is edited for hours. A `PosOrder` holds lines with **invoiced / served / discarded counters**,
the table, covers and order type. Settling or splitting creates Invoices *from* it and moves
counters (defect 2). **This is the one deliberate divergence from the vendor's document model.** It
owes a surface (phase 49's rule): the ERP gets a read-only *POS Orders* list, so an open tab is never
invisible to the back office. A Retail **hold** is a `PosOrder` of type Retail.

**F. KOT is a child of `PosOrder`**: `KitchenTicket` per (send × station), carrying only the delta.
The **station** is the product's print profile. This **reopens phase 47's drop of
`Product.PrintProfileId`**, which was dropped as "live-confirmed to do nothing observable". That was
true of the ERP, and the POS is where it acts. Discarding a sent item needs a reason and is audited.

**G. Service charge is a location rule times a product flag, inside the VAT base.** Settings: rate
plus income account per location; `Product.ServiceChargeApplicable`, **reopening phase 47's drop of
the Service Charge column**. It is computed per line, persisted, and printed as its own line. The
Nepal VAT treatment (service charge taxable) is what the vendor does and must be confirmed against
the rules before phase 61 ships (open question 1).

**H. The session is a cash drawer that posts.** `PosSession` (user × location; one open at a time)
holds: an opening float by denomination; Cash In/Out with an **account**, each posting its own GL
entry; and a close with counted cash that posts over/short to a new tenant-default *Cash Over/Short*
account. The X/Z report and the Day Report read one shared reader (defects 6, 7). Denominations are
per-location configuration (vendor default 1000/500/100/50/20/10/5/2/1).

**I. Tenant-default accounts: Service Charge Income, Rounding, Cash Over/Short.** Phase 29's rule
applies: grep `web/` for each field before calling it done.

**J. Permissions (derived per feature, not defaulted).** Selling and refunding at the till reuse the
Invoice and Credit Note keys, scoped per location by phase 32b. That is exactly the vendor's model,
and it means a cashier can be scoped to one branch today. New keys:

- `Pos.Session.Operate` — open, close, cash in/out. Admin+Member: routine daily working data.
- `Pos.Session.ViewAll` — see other users' sessions and the X/Z reports. Admin-only: cash
  accountability across staff.
- `Pos.FloorPlan.Manage` and `Pos.Settings.Manage` — Admin-only: configuration.
- `Pos.Kitchen.Operate` — the KOT board, serve and discard. Admin+Member.

A manager override (PIN) for discounts, voids and refunds above a threshold is an improvement
candidate, **not** in the first build.

**K. Deferred, each with a start condition** (listed in `roadmap.md`, not scheduled):

- **FonePay / NepalPay dynamic QR:** needs merchant credentials, and a third-party payment API is
  phase 22's "sending tenant data to a third party" decision.
- **IRD CBMS real-time bill sync:** the vendor has test-server credentials and a re-sync endpoint.
  Needs IRD test credentials, and the roadmap already defers IRD e-filing.
- **Discount schemes** (item / category / slab, dated, per location): a promotions engine, and a
  phase of its own once basic discounts are in the till.
- **Delivery partners and their statement:** the partner is tied to a supplier account; the
  settlement shape is unread.
- **Offline mode:** the vendor has none. A product decision, and it changes numbering (offline
  numbers cannot be assigned at approve).
- **Device binding:** OTP per device.

## 5. The plan: phases 60–66

Each phase is one session and follows the usual exit bar: status doc, tests, an E2E with a negative
403 path, and SQL proof of posted rows.

| Phase | Scope | Exit evidence |
|---|---|---|
| **60 — POS foundation** | `BillingLocation.PosMode` (B) and retiring the reserved enum members; `PosLocationSettings` (service charge rate + account, round-off + account, cash verification + denominations, default tab, print toggles); **phase 2's `PaymentMode` lookup extended** (today: name + `RequiresChequeDetails`, referenced by `Payment`) with a kind (Cash/Card/E-Payment/Other), a cash/bank account from phase 17, and a per-location link; the seeded walk-in customer; three tenant-default accounts (I); `Product.ServiceChargeApplicable` / `AvailableForSale`; the new keys (J); ERP-side *Configurations > POS* screens | settings round-trip; the feature gate refuses on a tenant without the flag; `web/` grep for the three accounts |
| **61 — POS sale engine (backend)** | Invoice gains channel, session, order type, per-line service charge, round-off, change, **tenders** (C, D); `PosSession` with denominations, cash in/out and close posting (H); one create-approved command; the X/Z shared reader | SQL: sale entry + tender entry per invoice; Void reverses both; over/short and cash-out rows in GL; the three-view law still holds for Goods lines; **the day total equals the session total in one test** |
| **62 — Retail till (UI)** | `/pos` shell, location/session picker, product grid by category, search + **barcode** (`Product.Barcode` exists), cart, line edit (unit, price, discount, note), payment screen (keypad, multi-tender, change), **hold/recall**, 80 mm receipt with the right header, service-charge and round-off lines, reprint marked *copy* | browser E2E: open session → sale → multi-tender → reprint → close with variance; keyboard census (phase 40) on the till |
| **63 — Returns at the till** | refund from an invoice (item/qty + required remark) → Credit Note with a payout tender against the open session; the refund permission path; lock-date and bank-reconciliation refusals inherited | SQL: CN + payout entry; session report shows the refund; a 403 on the refund key |
| **64 — Restaurant: floor, orders, KOT** | areas and tables with a layout editor; `PosOrder` (E) with covers, table transfer, item transfer; `KitchenTicket` per send × station (F), reopening `PrintProfileId`; KOT print; serve/discard with reason; Take Away and Delivery order types; ERP *POS Orders* list | two-round KOT delta test; an occupied table survives a reload; a discard needs a reason |
| **65 — Restaurant: kitchen display and settling** | live KOT board (push, not a refresh button); estimate bill; **split by item/qty and equal split** moving invoiced counters with service charge preserved (defect 1's regression test); settle to Invoice(s) | the split test: Σ service charge over the splits = the order's; Σ invoices = the order total |
| **66 — POS reports and dashboard** | Day Report, Payment Summary, Order Report, Product/Customer Sales, Sales Master/Summary, POS activity log; the home dashboard (sales/cash/credit, pending orders, sales figure, top products, payment stats) | every figure reconciles to the session reader and the Sales Register |

**Ordering rationale.** Retail first because it exercises the whole money path (session → invoice →
tenders → GL → reports) without the restaurant's order model. Every restaurant phase then sits on a
proven sale engine. Phases 60–63 alone are a shippable POS Retail. Phases 64–65 add POS Restaurant.

## 6. Open questions the next session must settle before coding

1. **Service charge and VAT in Nepal.** The vendor puts service charge in the VAT base. Confirm the
   rule, and whether service charge has its own statutory treatment (e.g. staff distribution),
   before phase 61 posts it.
2. **Abbreviated tax invoice.** The vendor's flag is inert. Confirm the conditions (buyer
   unregistered, amount threshold) from the VAT rules, not from the vendor. Phase 62's receipt
   header depends on it.
3. **Reprint marking.** The vendor tracks `print_count`/`first_print_time`. Confirm what the
   invoice-printing rules require on a reprint before phase 62 ships the receipt.
4. **Delivery partner settlement:** unread. Only matters if the delivery-partner item is promoted.

## Writes made to the tenant (user-authorised, trial tenant)

- POS Restaurant settings: service charge 10% → *Sales Service*; round-off on → *Sales Goods*;
  cash verification on.
- Area *Ground Floor*; tables T1 (rect, 4) and T2 (circle, 2).
- Print profile *Kitchen*.
- Products P0002 *Chicken Momo* and P0003 *Coke 250ml*.
- Documents: SO0002–SO0004, INV0002–INV0003 and CN0002 at location 1002. SO0004 was a Retail hold
  at 1003.
- Two sessions, both closed.

No payment-provider credentials or IRD credentials were entered.
