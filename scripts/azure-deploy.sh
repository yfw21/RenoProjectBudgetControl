#!/usr/bin/env bash
# Provision Azure + build + deploy + smoke test, in one command.
#
#   az login
#   scripts/azure-deploy.sh [resource-group] [location]
#
# Optional env: ANTHROPIC_API_KEY (enables the AI assistant; stored in Key Vault),
#               MIN_REPLICAS=1 (keep the app warm before an interview demo),
#               DOC_INTEL_SKU=S0 (if the subscription already has its one free F0 Document Intelligence).
set -euo pipefail

RG="${1:-rg-renobudget}"
LOCATION="${2:-eastus2}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TAG="$(git -C "$ROOT" rev-parse --short HEAD 2>/dev/null || date +%s)"

deploy() {  # $1 = container image ('' = infrastructure only)
  az deployment group create -g "$RG" -n "renobudget-$(date +%s)" \
    --template-file "$ROOT/infra/main.bicep" \
    --parameters containerImage="$1" anthropicApiKey="${ANTHROPIC_API_KEY:-}" minReplicas="${MIN_REPLICAS:-0}" docIntelSku="${DOC_INTEL_SKU:-F0}" \
    --query properties.outputs -o json
}
out() { python3 -c "import sys,json; print(json.load(sys.stdin)['$1']['value'])"; }

echo "==> Resource group $RG ($LOCATION)"
az group create -n "$RG" -l "$LOCATION" -o none

echo "==> 1/3 Infrastructure (registry, SQL, storage, Key Vault, monitoring)"
OUT=$(deploy "")
ACR=$(echo "$OUT" | out acrName)
SERVER=$(echo "$OUT" | out acrLoginServer)
IMAGE="$SERVER/renobudget:$TAG"

echo "==> 2/3 Build image $IMAGE"
if ! az acr build -r "$ACR" -t "renobudget:$TAG" "$ROOT" --no-logs -o none; then
  # Some new/trial subscriptions block ACR Tasks; fall back to a local Docker build + push.
  echo "    ACR build unavailable, building locally with Docker"
  docker build -t "$IMAGE" "$ROOT"
  az acr login -n "$ACR"
  docker push "$IMAGE"
fi

echo "==> 3/3 Deploy container app"
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
Tear down (stops all charges): az group delete -n $RG --yes --no-wait
EOF
