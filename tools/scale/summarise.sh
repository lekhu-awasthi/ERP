#!/usr/bin/env bash
# Phase 34c -- turns the raw results-*.csv files into the markdown table docs/phase-34c-status.md
# carries, and applies Decision A's per-class p95 budget so the table says pass/fail rather than
# leaving a reader to do it.
#
# Usage:  bash tools/scale/summarise.sh baseline scaled indexed
#         (any number of labels; the first is the reference column)

set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Decision A -- the p95 budget per class of screen, in milliseconds. A list page and the search box
# are inside an interaction someone is having; a statement or a register is a thing they asked for
# and expect to wait a moment on. See docs/phase-34c-status.md Decision A for the reasoning.
budget_for() {
  case "$1" in
    stmt.*|reg.*|rep.*) echo 2000 ;;
    search.*)           echo 500 ;;
    *)                  echo 500 ;;
  esac
}

labels=("$@")
[[ ${#labels[@]} -gt 0 ]] || { echo "usage: summarise.sh <label> [label...]" >&2; exit 1; }

header="| endpoint | budget |"
divider="|---|---|"
for l in "${labels[@]}"; do header+=" $l p95 |"; divider+="---|"; done
header+=" verdict |"; divider+="---|"
echo "$header"; echo "$divider"

# Endpoint order comes from the first label's file, which is the order run-measurement.sh issues.
while IFS=, read -r name _runs _http _bytes _p50 _p95 _max; do
  [[ "$name" != "endpoint" ]] || continue
  budget="$(budget_for "$name")"
  row="| \`$name\` | ${budget} |"
  values=()
  for l in "${labels[@]}"; do
    v="$(awk -F, -v n="$name" '$1 == n { print $6 }' "$HERE/results-$l.csv")"
    row+=" ${v:--} |"
    [[ -n "$v" ]] && values+=("$v")
  done
  # The verdict is taken over EVERY pass after the first, not just the newest one -- and where those
  # passes straddle the budget it says so rather than picking a side. On this machine the report
  # class swings 2-4x between whole passes while the list class is stable within 30%, so a verdict
  # read off one pass would be an artefact of which pass ran last.
  verdict="—"
  if [[ ${#values[@]} -gt 1 ]]; then
    verdict="$(printf '%s\n' "${values[@]:1}" | awk -v b="$budget" '
      { if ($1 <= b) pass++; else fail++ }
      END {
        if (fail == 0) print "PASS";
        else if (pass == 0) print "**FAIL**";
        else print "MIXED";
      }')"
  fi
  echo "$row $verdict |"
done < "$HERE/results-${labels[0]}.csv"
