#!/usr/bin/env bash
# Put the project on GitHub, then (later) connect it to Azure. Everything here is free.
#
#   scripts/github-setup.sh repo [name] [--private]   create the repo, push main + azure, open a pull request
#   scripts/github-setup.sh azure [resource-group]    after the first Azure deploy: OIDC login for the Deploy workflow
#
# Installs the GitHub CLI into ~/.local/bin if it is missing (no sudo).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
export PATH="$HOME/.local/bin:$PATH"

install_gh() {
  command -v gh >/dev/null && return
  echo "==> Installing GitHub CLI into ~/.local/bin"
  local ver tmp
  ver=$(curl -fsSL https://api.github.com/repos/cli/cli/releases/latest | python3 -c "import sys,json; print(json.load(sys.stdin)['tag_name'].lstrip('v'))")
  tmp=$(mktemp -d)
  curl -fsSL "https://github.com/cli/cli/releases/download/v${ver}/gh_${ver}_linux_amd64.tar.gz" | tar -xz -C "$tmp"
  mkdir -p "$HOME/.local/bin"
  mv "$tmp/gh_${ver}_linux_amd64/bin/gh" "$HOME/.local/bin/gh"
  gh --version | head -1
}

login_gh() {
  if ! gh auth status >/dev/null 2>&1; then
    echo "==> Sign in to GitHub (a browser code will be shown)"
    gh auth login --hostname github.com --git-protocol https --web
  fi
  gh auth setup-git   # let `git push` use the same login
}

cmd_repo() {
  local name="${1:-RenoProjectBudgetControl}" visibility="--public"
  [ "${2:-}" = "--private" ] && visibility="--private"
  install_gh
  login_gh
  local owner; owner=$(gh api user --jq .login)

  if git remote get-url origin >/dev/null 2>&1; then
    echo "==> Remote 'origin' already set: $(git remote get-url origin)"
  else
    echo "==> Creating github.com/$owner/$name ($visibility)"
    gh repo create "$name" $visibility --source . --remote origin \
      --description "Renovation budget manager: ASP.NET Core + React on Azure (Container Apps, Azure SQL, Blob, Document Intelligence, Bicep, GitHub Actions)"
  fi

  echo "==> Pushing main and azure"
  git push -u origin main
  git push -u origin azure

  if [ -z "$(gh pr list --head azure --base main --json number --jq '.[].number')" ]; then
    echo "==> Opening pull request azure -> main (CI runs on it)"
    gh pr create --base main --head azure \
      --title "Run on Azure: Container Apps, Azure SQL, Blob, Document Intelligence, Claude, Bicep, CI/CD" \
      --body "$(cat <<'EOF'
Makes the app cloud-ready and deployable to Azure at $0 by default. Design notes, costs and the demo script are in docs/AZURE.md.

- Same image runs on a laptop (SQLite, local disk, Ollama), in Docker (SQL Server + Azurite) and in Azure
- Managed identity for SQL, Blob, Document Intelligence and Claude on Foundry: no passwords or keys
- Receipts and invoices read by Azure AI Document Intelligence, with confidence shown to the user
- AI assistant on Claude via the Anthropic SDK (optional; off by default because it bills per token)
- Bicep for all infrastructure, budget alert, GitHub Actions CI with a containerized smoke test

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
  fi

  cat <<EOF

Done: https://github.com/$owner/$name
  - Watch CI:      gh run watch
  - Merge the PR when CI is green: gh pr merge azure --merge
  - Deploy to Azure (free): see docs/AZURE.md, then run: scripts/github-setup.sh azure
EOF
}

cmd_azure() {
  local rg="${1:-rg-renobudget}"
  install_gh
  login_gh
  command -v az >/dev/null || { echo "Azure CLI not found: https://learn.microsoft.com/cli/azure/install-azure-cli"; exit 1; }
  az account show >/dev/null 2>&1 || az login
  az group show -n "$rg" >/dev/null 2>&1 || { echo "Resource group $rg not found. Deploy once with scripts/azure-deploy.sh first."; exit 1; }

  local repo app_id rg_id
  repo=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
  echo "==> Azure app registration for $repo (OIDC: GitHub gets short-lived tokens, no stored secret)"
  app_id=$(az ad app list --display-name renobudget-github --query '[0].appId' -o tsv)
  [ -n "$app_id" ] || app_id=$(az ad app create --display-name renobudget-github --query appId -o tsv)
  az ad sp show --id "$app_id" >/dev/null 2>&1 || az ad sp create --id "$app_id" -o none

  if ! az ad app federated-credential list --id "$app_id" --query "[?name=='production']" -o tsv | grep -q .; then
    az ad app federated-credential create --id "$app_id" --parameters "{
      \"name\": \"production\", \"issuer\": \"https://token.actions.githubusercontent.com\",
      \"subject\": \"repo:$repo:environment:production\", \"audiences\": [\"api://AzureADTokenExchange\"]}" -o none
  fi

  rg_id=$(az group show -n "$rg" --query id -o tsv)
  echo "==> Contributor on $rg only (not the whole subscription)"
  az role assignment create --assignee "$app_id" --role Contributor --scope "$rg_id" -o none 2>/dev/null || true

  echo "==> GitHub environment, secrets and variable"
  gh api -X PUT "repos/$repo/environments/production" >/dev/null
  gh secret set AZURE_CLIENT_ID --env production --body "$app_id"
  gh secret set AZURE_TENANT_ID --env production --body "$(az account show --query tenantId -o tsv)"
  gh secret set AZURE_SUBSCRIPTION_ID --env production --body "$(az account show --query id -o tsv)"
  gh variable set AZURE_RESOURCE_GROUP --body "$rg"

  cat <<EOF

Connected. Every green CI run on main now deploys to Azure.
  - Deploy now:  gh workflow run "Deploy to Azure" && gh run watch
  - Make the image public once (so Azure can pull it for free):
      https://github.com/users/$(gh api user --jq .login)/packages/container/renobudget/settings
EOF
}

case "${1:-}" in
  repo)  shift; cmd_repo "$@" ;;
  azure) shift; cmd_azure "$@" ;;
  *) sed -n '2,7p' "$0"; exit 1 ;;
esac
