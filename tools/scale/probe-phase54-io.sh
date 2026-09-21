#!/usr/bin/env bash
# Phase 54 -- the logical-reads probe for the two outstanding measurement debts.
#
# Same instrument and the same reason as probe-cheque-io.sh (phase 50): wall time on this machine
# moves by 3x between two passes of the identical configuration, logical reads do not, because they
# are a property of the plan rather than of what else the machine was doing.
#
# What it measures, and why these paths:
#
#   products.*  -- phase 52 added `.Include(x => x.SecondaryUnits)` to ListProductsQueryHandler and
#                  claimed no number for it. `listAllProducts` (MAX_PAGE_SIZE = 200) is what every
#                  document line picker calls, so it is the path that pays; page 1 at 50 is the
#                  Products grid, and the search paths are the ones phase 34c's rule says to
#                  re-measure because they were not the target.
#
#   stock.*     -- phase 51 added a FILTERED UNIQUE index to inventory.StockLedgerEntries and
#                  claimed no number either. The path it might have disturbed is the FIFO walk over
#                  the same table, which is a WRITE path with no endpoint of its own, so its exact
#                  query shape is issued directly (LoadLayersOldestFirstAsync and
#                  GetAvailableQuantityAsync, transcribed from StockLedgerService).
#
# Usage:  bash tools/scale/probe-phase54-io.sh <label> [api]
# Writes: tools/scale/probe-phase54-<label>.csv

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
LABEL="${1:-run}"
API="${2:-https://localhost:7104}"
SQLSRV='DESKTOP-H0R00ME\SQLEXPRESS'
JAR="$HERE/.cookies-probe54"
CSV="$HERE/probe-phase54-$LABEL.csv"

# shellcheck source=/dev/null
source "$HERE/.seed-ids.env"

secret() { dotnet user-secrets list --project "$ROOT/src/Api" 2>/dev/null | sed -n "s/^$1 = //p"; }
EMAIL="$(secret 'Testing:Email')"
PASSWORD="$(secret 'Testing:Password')"

rm -f "$JAR"
login_code="$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$API/api/auth/login" -c "$JAR" \
  -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")"
[[ "$login_code" == "200" ]] || { echo "FATAL: login returned $login_code" >&2; exit 1; }

B="/api/organizations/$ORG"

# A pass against an empty tenant looks spectacularly fast and means nothing (README). Refuse to
# start unless the fixture this probe needs is actually there.
guard="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -s',' -Q "
  SET NOCOUNT ON;
  SELECT (SELECT COUNT(*) FROM inventory.StockLedgerEntries WHERE OrganizationId = '$ORG'),
         (SELECT COUNT(*) FROM catalog.ProductSecondaryUnits s
          JOIN catalog.Products p ON p.Id = s.ProductId WHERE p.OrganizationId = '$ORG');" \
  2>/dev/null | tr -d ' ')"
IFS=',' read -r layers units <<<"$guard"
if [[ "${layers:-0}" -lt 100000 || "${units:-0}" -lt 5000 ]]; then
  echo "FATAL: fixture missing (layers=$layers units=$units). Run seed-phase54.sql first." >&2
  exit 1
fi
echo "fixture: $layers stock layers, $units secondary units"

# The stock.* rows report scan count in the statements column and STATISTICS TIME's ms in the
# two time columns; the products.* rows report cached statements and the DMV's per-execution
# averages. Both columns mean 'logical reads for one request', which is the number that matters.
printf 'path,statements_or_scans,logical_reads,cpu_ms,elapsed_ms\n' >"$CSV"
printf '%-26s %12s %20s %12s %14s\n' path statements avg_logical_reads avg_cpu_ms avg_elapsed_ms

# --- the HTTP half -----------------------------------------------------------------------------

PATHS=$(cat <<LIST
products.picker200	$B/products?page=1&pageSize=200&variantFilter=Transactable
products.grid50	$B/products?page=1&pageSize=50
products.search.hit	$B/products?page=1&pageSize=50&search=Bulk
products.search.miss	$B/products?page=1&pageSize=50&search=ZZQQXX
LIST
)

while IFS=$'\t' read -r name path; do
  [[ -n "$name" ]] || continue

  sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -Q "DBCC FREEPROCCACHE WITH NO_INFOMSGS;" >/dev/null 2>&1

  for _ in 1 2 3; do
    curl -sk -o /dev/null -b "$JAR" "$API$path"
  done

  # Sum the per-execution averages over every cached statement naming Products -- a paginated list
  # is a count, a key page and a fetch, and what matters to the caller is what one request cost.
  IFS=',' read -r stmts reads cpu elapsed <<<"$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -s',' -Q "
    SET NOCOUNT ON;
    SELECT COUNT(*),
           ISNULL(SUM(qs.total_logical_reads / qs.execution_count), 0),
           CAST(ISNULL(SUM(qs.total_worker_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1)),
           CAST(ISNULL(SUM(qs.total_elapsed_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1))
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
    WHERE t.text LIKE '%Products%' AND t.text NOT LIKE '%dm_exec_query_stats%';" 2>/dev/null | tr -d ' ')"

  printf '%-26s %12s %20s %12s %14s\n' "$name" "$stmts" "$reads" "$cpu" "$elapsed"
  printf '%s,%s,%s,%s,%s\n' "$name" "$stmts" "$reads" "$cpu" "$elapsed" >>"$CSV"
done <<<"$PATHS"

# --- the FIFO half -----------------------------------------------------------------------------
#
# No endpoint walks FIFO without writing, so the walk's own two query shapes are issued directly.
# Transcribed from StockLedgerService: LoadLayersOldestFirstAsync (with and without the batch and
# serial narrowings it composes) and GetAvailableQuantityAsync.

PRODUCT="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "
  SET NOCOUNT ON;
  SELECT TOP 1 CONVERT(varchar(36), ProductId) FROM inventory.StockLedgerEntries
  WHERE OrganizationId = '$ORG' AND SerialNo IS NOT NULL;" 2>/dev/null | tr -d ' \r')"
WH="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "
  SET NOCOUNT ON;
  SELECT TOP 1 CONVERT(varchar(36), WarehouseId) FROM inventory.StockLedgerEntries
  WHERE OrganizationId = '$ORG';" 2>/dev/null | tr -d ' \r')"
SERIAL="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "
  SET NOCOUNT ON;
  SELECT TOP 1 SerialNo FROM inventory.StockLedgerEntries
  WHERE OrganizationId = '$ORG' AND SerialNo IS NOT NULL;" 2>/dev/null | tr -d ' \r')"

# Read through SET STATISTICS IO rather than sys.dm_exec_query_stats, unlike the HTTP half above.
# The DMV reports nothing for these: an ad-hoc sqlcmd batch is cached as a compiled-plan *stub*, and
# a stub carries no query_stats row, so the probe came back 0 statements / 0 reads on every path
# while the queries were plainly running. STATISTICS IO is the same number measured at the source,
# and it does not depend on what the plan cache decided to keep.
run_sql_probe() {
  local name="$1" sql="$2"

  sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -Q "DBCC FREEPROCCACHE WITH NO_INFOMSGS;" >/dev/null 2>&1

  # Warm the buffer pool first, so the reading is logical reads and not disk luck.
  sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -Q "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; $sql" >/dev/null 2>&1

  local out reads scans cpu elapsed
  out="$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -Q "SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET STATISTICS IO ON; SET STATISTICS TIME ON; $sql" 2>&1)"

  # Anchored on "Scan count N, logical reads", NOT on ".*logical reads": the STATISTICS IO line also
  # contains "lob logical reads 0", and a greedy .* matches the LAST occurrence, so the unanchored
  # form reported 0 reads for every path while the scan count parsed correctly. Same family as
  # CLAUDE.md's lazy-.*-between-two-anchors gotcha.
  reads="$(sed -n "s/.*Table 'StockLedgerEntries'\. Scan count [0-9]*, logical reads \([0-9]*\),.*/\1/p" <<<"$out" | head -1)"
  scans="$(sed -n "s/.*Table 'StockLedgerEntries'\. Scan count \([0-9]*\),.*/\1/p" <<<"$out" | head -1)"
  cpu="$(sed -n 's/.*CPU time = \([0-9]*\) ms.*/\1/p' <<<"$out" | tail -1)"
  elapsed="$(sed -n 's/.*elapsed time = \([0-9]*\) ms.*/\1/p' <<<"$out" | tail -1)"

  printf '%-26s %12s %20s %12s %14s\n' "$name" "${scans:-0}" "${reads:-0}" "${cpu:-0}" "${elapsed:-0}"
  printf '%s,%s,%s,%s,%s\n' "$name" "${scans:-0}" "${reads:-0}" "${cpu:-0}" "${elapsed:-0}" >>"$CSV"
}

WALK="SELECT Id, QuantityRemaining, UnitCost, TransactionDate, CreatedAt
      FROM inventory.StockLedgerEntries
      WHERE OrganizationId = '$ORG' AND ProductId = '$PRODUCT' AND WarehouseId = '$WH'
        AND QuantityRemaining > 0
      ORDER BY TransactionDate, CreatedAt;"

run_sql_probe 'stock.walk' "$WALK"

run_sql_probe 'stock.walk.serial' "SELECT Id, QuantityRemaining, UnitCost, TransactionDate, CreatedAt
      FROM inventory.StockLedgerEntries
      WHERE OrganizationId = '$ORG' AND ProductId = '$PRODUCT' AND WarehouseId = '$WH'
        AND QuantityRemaining > 0 AND SerialNo = '$SERIAL'
      ORDER BY TransactionDate, CreatedAt;"

run_sql_probe 'stock.available' "SELECT SUM(QuantityRemaining) FROM inventory.StockLedgerEntries
      WHERE OrganizationId = '$ORG' AND ProductId = '$PRODUCT' AND WarehouseId = '$WH';"

run_sql_probe 'stock.shortfalls' "SELECT Id FROM inventory.StockLedgerEntries
      WHERE OrganizationId = '$ORG' AND ProductId = '$PRODUCT' AND WarehouseId = '$WH'
        AND QuantityRemaining < 0
      ORDER BY TransactionDate;"

echo
echo "wrote $CSV  (org=$ORG)"
