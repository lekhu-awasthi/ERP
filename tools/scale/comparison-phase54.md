# Phase 54 — the two outstanding measurement debts, paid

Both were plan changes shipped with no number, and phase 34c's rule is that whoever next touches the
area re-measures. Phase 54 touches both areas.

The instrument is phase 50's: **logical reads**, not the wall clock. Wall time on this machine moves
by 3× between two passes of the identical configuration; logical reads are a property of the plan.

## The fixture

`seed-phase54.sql` on the phase-34c organization (`7d0c1dfe…`), because that dataset has 20,001
products and no stock at all — its products are Service-typed on purpose, which is right for the GL
measurements and measures neither debt.

| | |
|---|---|
| Stock layers | **200,000**, concentrated over 2,000 products in one warehouse (100 layers deep each) |
| Serialised layers | 1 in 20 (10,000), which is what puts content in the phase-51 filtered index |
| Product secondary units | **20,000** — two on each of the first 10,000 products |
| Statistics | `UPDATE STATISTICS … WITH FULLSCAN` on `StockLedgerEntries`, `ProductSecondaryUnits` and `Products` before every pass |

Concentration is deliberate. The FIFO walk reads the layers of **one** `(product, warehouse)`, so
what the walk costs depends on how deep that stack is, while what a *mistaken plan* would cost
depends on how big the table is. 200k rows spread evenly over 20k products gives a 10-row walk
against a big table and measures neither well.

## The numbers

Logical reads for one request. `products.*` are HTTP paths read from `sys.dm_exec_query_stats`;
`stock.*` are the FIFO walk's own query shapes, transcribed from `StockLedgerService`, read from
`SET STATISTICS IO` (an ad-hoc batch is cached as a plan *stub* and has no `query_stats` row).

| path | **shipped** | without phase 52's `Include` | without phase 51's index |
|---|---:|---:|---:|
| `products.picker200` | 5,901 | 5,524 | 5,901 |
| `products.grid50` | 779 | 443 | 779 |
| `products.search.hit` | 452 | 452 | 452 |
| `products.search.miss` | 452 | 452 | 452 |
| `stock.walk` | 319 | 319 | 319 |
| `stock.walk.serial` | **5** | 5 | **319** |
| `stock.available` | 319 | 319 | 319 |
| `stock.shortfalls` | 319 | 319 | 319 |

## Debt 1 — phase 51's filtered unique index on `StockLedgerEntry`: **no cost to any other path**

`IX_StockLedgerEntries_OrganizationId_ProductId_SerialNo`, unique, filtered on
`SerialNo IS NOT NULL AND QuantityRemaining > 0`.

- Its **own** path — a serialised issue, which is `LoadLayersOldestFirstAsync` with the `SerialNo`
  narrowing composed on — costs **5 reads with it and 319 without**: **64× cheaper**, because the
  filtered index is a seek over 10,000 rows rather than a walk of that product's whole stack.
- The three paths it did **not** target — the ordinary FIFO walk, the availability sum and the
  shortfall scan — read **319 either way, to the read**. Dropping the index changed nothing about
  them, which is the question phase 34c's rule actually asks ("an index added for one path changes
  the plan for every other path on the table; re-measure the paths you did not touch").

Phase 50's own refusal came from exactly this comparison going the other way — an index that fixed
one path and took a sibling tab from 2,143 logical reads to 83,308. This one does not.

## Debt 2 — phase 52's `.Include(x => x.SecondaryUnits)`: **a small fixed cost, and the premise was wrong**

- **The premise the phase-52 comment recorded was wrong.** It says `listAllProducts` "asks for a very
  large page, so a tenant with thousands of products pays for it". `MAX_PAGE_SIZE` is **200**
  (`web/src/app/core/common/paged-result.ts`), so the join is over 200 rows, not 20,001. Recorded
  here because a wrong premise carried forward is how phase 44's reports ended up narrowing only
  half of themselves.
- **The cost is small and close to flat**: **+377 reads (+6.8 %) on the 200-row picker page** and
  **+336 reads on the 50-row grid page** — the same ~340–380 reads for four times the child rows,
  which is the shape of a seek into the child table rather than a per-row lookup.
- **The paths it did not target are untouched**: both search paths read 452 either way, because a
  search page carries few products and therefore few child rows.

**Kept.** 377 reads on a page that already costs 5,901 is 6.8 %, and it buys the thing the control
cannot work without: every document line picker needs a product's unit matrix the moment the product
is chosen, and the alternative is a second round trip per line.

## Reproducing

```bash
sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/seed-phase54.sql \
  -v OrgId="<guid>" NumLayers=200000 NumLayerProducts=2000 NumSecondaryUnits=20000
sqlcmd -S <server> -d ErpApp -E -C -i tools/scale/refresh-stats.sql
bash tools/scale/probe-phase54-io.sh shipped
```

The fixture is re-runnable and removable: every layer it writes carries
`SourceDocumentId = 54545454-…`, and its secondary units are the rows at conversion rate 12 or 24.
**It writes no documents, no GL and no movements**, so this tenant must not be used for a
conservation-law claim afterwards — that claim belongs to the manual E2E's own fresh organization.
