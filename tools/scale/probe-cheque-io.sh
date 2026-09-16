#!/usr/bin/env bash
# Phase 50 -- the logical-reads probe for the Cheque Register's paths.
#
# Why this exists beside run-measurement.sh. Wall time on this machine moves by 3x between two
# passes of the identical configuration (phase 34c's README says so, and phase 42 discarded two
# passes over it). Logical reads do not: they are a property of the plan, not of what else the
# machine was doing, which is why both 34c and 42 quote reads rather than milliseconds for every
# claim about an index. This phase is gated on a comparison, so it needs the stable number.
#
# For each path: clear the plan cache, issue the request three times, then read the per-statement
# average logical reads back out of sys.dm_exec_query_stats for every cached statement that touches
# the Cheques table. That attributes reads to a path rather than to a pass.
#
# Usage:  bash tools/scale/probe-cheque-io.sh <label> [api]
# Writes: tools/scale/probe-cheque-<label>.csv

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
LABEL="${1:-run}"
API="${2:-https://localhost:7104}"
SQLSRV='DESKTOP-H0R00ME\SQLEXPRESS'
JAR="$HERE/.cookies-probe"
CSV="$HERE/probe-cheque-$LABEL.csv"

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

PATHS=$(cat <<LIST
cheques.page1	$B/cheques?page=1&pageSize=50
cheques.last	$B/cheques?page=1000&pageSize=50
cheques.received	$B/cheques?page=1&pageSize=50&direction=Received
cheques.status	$B/cheques?page=1&pageSize=50&status=Pending
cheques.daterange	$B/cheques?page=1&pageSize=50&fromDate=2025-01-01&toDate=2025-03-31
cheques.search.hit	$B/cheques?page=1&pageSize=50&search=CHQ-0004
cheques.search.miss	$B/cheques?page=1&pageSize=50&search=ZZQQXX
cheques.dashboard	$B/cheques/dashboard-summary
LIST
)

printf 'path,statements,avg_logical_reads,avg_cpu_ms,avg_elapsed_ms\n' >"$CSV"
printf '%-24s %12s %20s %12s %14s\n' path statements avg_logical_reads avg_cpu_ms avg_elapsed_ms

while IFS=$'\t' read -r name path; do
  [[ -n "$name" ]] || continue

  sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -Q "DBCC FREEPROCCACHE WITH NO_INFOMSGS;" >/dev/null 2>&1

  for _ in 1 2 3; do
    curl -sk -o /dev/null -b "$JAR" "$API$path"
  done

  # Sum the per-execution averages across every cached statement that named the Cheques table --
  # a paginated list is two statements (the count and the page), and the number that matters to the
  # caller is what one request cost, not what one statement did.
  IFS=',' read -r stmts reads cpu elapsed <<<"$(sqlcmd -S "$SQLSRV" -d ErpApp -E -C -h -1 -W -s',' -Q "
    SET NOCOUNT ON;
    SELECT COUNT(*),
           ISNULL(SUM(qs.total_logical_reads / qs.execution_count), 0),
           CAST(ISNULL(SUM(qs.total_worker_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1)),
           CAST(ISNULL(SUM(qs.total_elapsed_time / qs.execution_count), 0) / 1000.0 AS decimal(10,1))
    FROM sys.dm_exec_query_stats qs
    CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) t
    WHERE t.text LIKE '%Cheques%' AND t.text NOT LIKE '%dm_exec_query_stats%';" 2>/dev/null | tr -d ' ')"

  printf '%-24s %12s %20s %12s %14s\n' "$name" "$stmts" "$reads" "$cpu" "$elapsed"
  printf '%s,%s,%s,%s,%s\n' "$name" "$stmts" "$reads" "$cpu" "$elapsed" >>"$CSV"
done <<<"$PATHS"

echo
echo "wrote $CSV  (org=$ORG)"
