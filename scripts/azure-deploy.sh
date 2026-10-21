#!/usr/bin/env bash
# Provision Azure + deploy + smoke test, in one command. Runs at $0 with the defaults.
#
#   az login
#   IMAGE=ghcr.io/<owner>/renobudget:<tag> BUDGET_EMAIL=you@example.com scripts/azure-deploy.sh [resource-group] [location]
#
# IMAGE is a public image in GitHub Container Registry (free). The Deploy workflow builds and pushes it;
# without IMAGE, the script builds and pushes one itself if Docker is available and GHCR_OWNER is set
# (after `docker login ghcr.io`).
#
# Optional env: BUDGET_EMAIL (cost alert; strongly recommended),
#               MIN_REPLICAS=1 (keep warm during a live demo only; always-on uses up the free grant),
#               DOC_INTEL_SKU=S0 (if the subscription already has its one free F0 Document Intelligence),
#               CLAUDE_MODE=foundry|anthropic (paid per token; default off), ANTHROPIC_API_KEY,
#               CLAUDE_ORG_NAME (required for foundry).
set -euo pipefail

RG="${1:-rg-renobudget}"
LOCATION="${2:-eastus2}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TAG="$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || date +%s)"

deploy() {  # $1 = container image ('' = infrastructure only)
  az deployment group create -g "$RG" -n "renobudget-$(date +%s)" \
    --template-file "$ROOT/infra/main.bicep" \
    --parameters containerImage="$1" minReplicas="${MIN_REPLICAS:-0}" docIntelSku="${DOC_INTEL_SKU:-F0}" \
                 budgetEmail="${BUDGET_EMAIL:-}" claudeMode="${CLAUDE_MODE:-off}" \
                 anthropicApiKey="${ANTHROPIC_API_KEY:-}" claudeOrganizationName="${CLAUDE_ORG_NAME:-}" \
    --query properties.outputs -o json
}
out() { python3 -c "import sys,json; print(json.load(sys.stdin)['$1']['value'])"; }

[ -n "${BUDGET_EMAIL:-}" ] || echo "!!  BUDGET_EMAIL not set: no cost alert will be created."

if [ -z "${IMAGE:-}" ]; then
  : "${GHCR_OWNER:?Set IMAGE=ghcr.io/<owner>/renobudget:<tag>, or GHCR_OWNER to build and push with local Docker}"
  IMAGE="ghcr.io/${GHCR_OWNER,,}/renobudget:$TAG"
  echo "==> Build and push $IMAGE (make the package public once in GitHub → Packages → Settings)"
  docker build -t "$IMAGE" "$ROOT"
  docker push "$IMAGE"
fi

echo "==> Resource group $RG ($LOCATION)"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> Deploy infrastructure and $IMAGE"
OUT=$(deploy "$IMAGE")
URL=$(echo "$OUT" | out appUrl)
echo "    $URL"

echo "==> Waiting for the app to become ready (serverless SQL may be resuming)"
for _ in $(seq 1 40); do
  curl -fs -o /dev/null --max-time 10 "$URL/healthz/ready" && break
  sleep 6
done
"$ROOT/scripts/smoke-api.sh" "$URL"

cat <<EOF

Deployed: $URL
Logs:     az containerapp logs show -g $RG -n ca-renobudget --follow
Tear down (removes everything): az group delete -n $RG --yes --no-wait
EOF
