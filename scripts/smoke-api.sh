#!/usr/bin/env bash
# End-to-end API smoke test. Works against any deployment of the app:
#   scripts/smoke-api.sh http://localhost:5163            # local dotnet run
#   scripts/smoke-api.sh http://localhost:8080            # docker compose (Azure SQL + Blob emulators)
#   scripts/smoke-api.sh https://<app>.azurecontainerapps.io
# Exercises health probes, auth enforcement, DB writes (incl. decimal precision) and file
# upload/download round-trips through whichever storage provider is configured.
set -euo pipefail

BASE="${1:-http://localhost:5163}"
JAR="$(mktemp)"; TMP="$(mktemp -d)"
trap 'rm -rf "$JAR" "$TMP"' EXIT
PASS=0; FAIL=0

ok()   { echo "  PASS  $1"; PASS=$((PASS+1)); }
bad()  { echo "  FAIL  $1"; FAIL=$((FAIL+1)); }
check() { if [ "$2" = "$3" ]; then ok "$1"; else bad "$1 (expected '$3', got '$2')"; fi; }
code() { curl -s -o /dev/null -w '%{http_code}' -b "$JAR" -c "$JAR" "$@"; }
api()  { curl -sf -b "$JAR" -c "$JAR" "$@"; }
json() { python3 -c "import sys,json; d=json.load(sys.stdin); print($1)"; }

echo "Smoke testing $BASE"

check "liveness  /healthz"        "$(code "$BASE/healthz")" 200
check "readiness /healthz/ready"  "$(code "$BASE/healthz/ready")" 200
check "SPA served at /"           "$(code "$BASE/")" 200
check "anonymous budget read is rejected" "$(code "$BASE/api/budgetitems")" 401

USER="smoke_$(date +%s)_$RANDOM"
check "register user" "$(code -H 'Content-Type: application/json' \
  -d "{\"username\":\"$USER\",\"password\":\"Sm0ke-pass!\"}" "$BASE/api/auth/register")" 200
check "session cookie works" "$(code "$BASE/api/auth/me")" 200

PID=$(api -H 'Content-Type: application/json' -d "{\"name\":\"Smoke $USER\"}" "$BASE/api/projects" | json 'd["id"]')
[ -n "$PID" ] && ok "create project ($PID)" || bad "create project"

ITEM=$(api -H 'Content-Type: application/json' \
  -d "{\"projectId\":$PID,\"category\":\"Kitchen\",\"description\":\"Cabinets\",\"budget\":12500.75,\"actual\":0}" \
  "$BASE/api/budgetitems" | json 'd["id"]')
[ -n "$ITEM" ] && ok "create budget line ($ITEM)" || bad "create budget line"

printf 'receipt from %s\n' "$USER" > "$TMP/receipt.txt"
BLOB=$(api -F "file=@$TMP/receipt.txt;type=text/plain" "$BASE/api/budgetitems/$ITEM/receipt" | json 'd["receiptFileBlobId"]')
[ -n "$BLOB" ] && ok "attach receipt (blob $BLOB)" || bad "attach receipt"
api "$BASE/api/files/$BLOB" -o "$TMP/back.txt"
if cmp -s "$TMP/receipt.txt" "$TMP/back.txt"; then ok "receipt download matches upload"; else bad "receipt download matches upload"; fi

printf 'invoice %s\n' "$USER" > "$TMP/invoice.txt"
PAYLOAD="{\"projectId\":$PID,\"vendor\":\"Home Depot\",\"date\":\"2026-10-01\",\"items\":[{\"description\":\"Lumber\",\"actual\":412.37,\"budgetLineId\":$ITEM},{\"description\":\"Screws\",\"actual\":18.99,\"budgetLineId\":$ITEM}]}"
BATCH=$(api -F "payload=$PAYLOAD" -F "file=@$TMP/invoice.txt;type=text/plain" "$BASE/api/budgetitems/batch")
check "batch expenses with shared receipt" "$(echo "$BATCH" | json 'len(d["createdIds"])')" 2

TOTALS=$(api "$BASE/api/budgetitems?projectId=$PID" | json "'%.2f/%.2f/%d' % (sum(x['budget'] for x in d), sum(x['actual'] for x in d), len(d))")
check "money totals survive round-trip (budget/actual/rows)" "$TOTALS" "12500.75/431.36/3"

check "unknown file is 404" "$(code "$BASE/api/files/99999999")" 404

# Clean up so runs against a live demo don't leave test projects in the project picker.
for id in $(api "$BASE/api/budgetitems?projectId=$PID" | json "' '.join(str(x['id']) for x in d)"); do
  code -X DELETE "$BASE/api/budgetitems/$id" >/dev/null
done
check "cleanup test project" "$(code -X DELETE "$BASE/api/projects/$PID")" 204

echo "$PASS passed, $FAIL failed"
[ "$FAIL" -eq 0 ]
