#!/usr/bin/env bash
# Phase 95 -- logical reads for the till's scan (phase 62 § 5: the barcode lookup is unindexed). A scan is
# GET .../pos/tills/{location}/products?code=, which matches a product's barcode, code or SKU exactly among the products
# the till may sell; a code that matches none is then tried as a serial in stock.
#
# Same method as probe-phase91-io.sh: per path, clear the plan cache, issue the request three times, then sum the
# per-execution average logical reads, CPU and elapsed time of every cached statement touching the product table.
#
# Run tools/scale/seed-phase95.sql against the tenant first: without barcodes every scan is a miss.
#
# Usage:  bash tools/scale/probe-phase95-io.sh <label> [api]
# Writes: tools/scale/probe-phase95-<label>.csv

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
LABEL="${1:-run}"
API="${2:-https://localhost:7104}"
SQLSRV='DESKTOP-H0R00ME\SQLEXPRESS'
JAR="$HERE/.cookies-probe"
CSV="$HERE/probe-phase95-$LABEL.csv"

# shellcheck source=/dev/null
source "$HERE/.seed-ids.env"

secret() { dotnet user-secrets list --project "$ROOT/src/Api" 2>/dev/null | sed -n "s/^$1 = //p"; }
EMAIL="$(secret 'Testing:Email')"
PASSWORD="$(secret 'Testing:Password')"

rm -f "$JAR"
login_code="$(curl -sk -o /dev/null -w '%{http_code}' -X POST "$API/api/auth/login" -c "$JAR" \
  -H 'Content-Type: application/json' -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}")"
[[ "$login_code" == "200" ]] || { echo "FATAL: login returned $login_code" >&2; exit 1; }

q() { sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -Q "SET NOCOUNT ON; $1" | tr -d ' \r'; }

labelled="$(q "SELECT COUNT(*) FROM catalog.Products WHERE OrganizationId = '$ORG' AND Barcode IS NOT NULL;")"
[[ "$labelled" -gt 1000 ]] || { echo "FATAL: $labelled products carry a barcode on $ORG -- run seed-phase95.sql first" >&2; exit 1; }

LOCATION="$(q "SELECT Id FROM tenancy.BillingLocations WHERE OrganizationId = '$ORG' AND PosMode = 'Retail';")"
# A barcode from the middle of the catalogue, a product code, and a code nothing carries.
BARCODE="$(q "SELECT Barcode FROM (SELECT Barcode, ROW_NUMBER() OVER (ORDER BY Code) AS n FROM catalog.Products WHERE OrganizationId = '$ORG') x WHERE n = 10000;")"
CODE="$(q "SELECT Code FROM (SELECT Code, ROW_NUMBER() OVER (ORDER BY Code) AS n FROM catalog.Products WHERE OrganizationId = '$ORG') x WHERE n = 10000;")"

B="/api/organizations/$ORG/pos/tills/$LOCATION/products"
P="/api/organizations/$ORG/products"

# The scan paths, and the product table's other readers (phase 34c: an index added for one path changes the plan for
# every other path on the table, so measure the ones it was not added for too).
PATHS=$(cat <<LIST
scan.barcode	$B?code=$BARCODE&pageSize=10
scan.code	$B?code=$CODE&pageSize=10
scan.miss	$B?code=NO-SUCH-CODE-95&pageSize=10
till.grid	$B?page=1&pageSize=48
till.search	$B?search=Product%201&page=1&pageSize=48
products.list	$P?page=1&pageSize=50
products.search	$P?search=Product%201&page=1&pageSize=50
products.picker	$P?locationId=$LOCATION&page=1&pageSize=50
till.open	/api/organizations/$ORG/pos/tills/$LOCATION
LIST
)

printf 'path,status,statements,avg_logical_reads,avg_cpu_ms,avg_elapsed_ms,median_wall_ms\n' >"$CSV"
printf '%-14s %6s %10s %18s %11s %14s %14s\n' path status statements avg_logical_reads avg_cpu_ms avg_elapsed_ms median_wall_ms

while IFS=$'\t' read -r name path; do
  [[ -n "$name" ]] || continue

  sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -Q "DBCC FREEPROCCACHE WITH NO_INFOMSGS;" >/dev/null 2>&1

  walls=()
  status=""
  for _ in 1 2 3; do
    out="$(curl -sk -o /dev/null -w '%{http_code} %{time_total}' -b "$JAR" "$API$path")"
    status="${out%% *}"
    walls+=("$(awk -v t="${out##* }" 'BEGIN { printf "%d", t * 1000 }')")
  done
  median="$(printf '%s\n' "${walls[@]}" | sort -n | sed -n 2p)"

  IFS=',' read -r stmts reads cpu elapsed <<<"$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -s',' -Q "
    SET NOCOUNT ON;
    SELECT COUNT(*),
           ISNULL(SUM(qs.total_logical_reads / qs.execution_count), 0),
           CAST(ISNULL(SUM(qs.total_worker_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1)),
           CAST(ISNULL(SUM(qs.total_elapsed_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1))
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
    WHERE t.text NOT LIKE '%dm_exec_query_stats%'
      AND t.text LIKE '%[[]Products]%';" 2>/dev/null | tr -d ' \r')"

  printf '%-14s %6s %10s %18s %11s %14s %14s\n' "$name" "$status" "$stmts" "$reads" "$cpu" "$elapsed" "$median"
  printf '%s,%s,%s,%s,%s,%s,%s\n' "$name" "$status" "$stmts" "$reads" "$cpu" "$elapsed" "$median" >>"$CSV"
done <<<"$PATHS"
