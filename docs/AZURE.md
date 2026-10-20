# Running Reno Budget on Azure

## What changed to make it cloud-ready

The app started as a laptop app: SQLite file, uploads on local disk, local LLM. Each of those was turned
into a configuration switch rather than a fork of the code, so the same image runs everywhere.

| Concern | Laptop | Azure | Where |
|---|---|---|---|
| Database | SQLite | Azure SQL (serverless), EF Core retry on transient errors | `Program.cs` `Database:Provider` |
| File uploads | `Files:Root` folder | Blob Storage via `IFileStore` | `Services/Storage/` |
| Auth cookie keys | in-memory/user profile | Persisted to Blob so restarts/redeploys don't log users out | `Program.cs` Data Protection |
| Secrets | appsettings / env | Key Vault reference on the Container App | `infra/main.bicep` |
| Telemetry | console logs | OpenTelemetry → Application Insights | `UseAzureMonitor()` |
| Health | – | `/healthz` (liveness), `/healthz/ready` (DB reachable) | `Program.cs`, container probes |
| HTTPS | Kestrel | TLS ends at Container Apps ingress; `ForwardedHeaders` restores scheme/IP | `Program.cs` |

Also fixed on the way: `BudgetItems`, `Categories` and `CategoryActivities` endpoints had no `[Authorize]`,
so budget data was readable and writable anonymously. Harmless on localhost, a real hole on the internet.
The smoke test now asserts an anonymous request gets `401`.

## Design decisions (and the trade-offs)

**Container Apps over App Service.** Receipt OCR shells out to Tesseract, a native binary. A container
makes that dependency explicit and reproducible (`Dockerfile`). Container Apps also scales to zero, so an
idle demo costs nothing, and it gives revisions for instant rollback.

**Managed Identity everywhere, zero stored credentials.** The app's user-assigned identity is used for
pulling the image, reading/writing blobs, reading Key Vault, and signing in to SQL. Storage has shared-key
access disabled and SQL has Entra-only auth, so even a leaked connection string grants nothing.
GitHub Actions authenticates with OIDC federation, so the repo holds no Azure secret either.
*Trade-off:* the identity is the SQL server admin so EF can create the schema. In production I would make
an Entra group the admin and give the app a contained user with only the roles it needs, and run schema
changes as migrations in the pipeline.

**One image serves API and SPA.** Same origin means no CORS in production and cookie auth just works.
*Trade-off:* frontend and backend deploy together. Static Web Apps + separate API would let them ship
independently, at the cost of CORS and cross-site cookies.

**Serverless SQL with the free offer.** Auto-pauses after an hour idle and stays free up to
100k vCore-seconds/month. *Trade-off:* the first request after a pause waits for resume, which is why EF
has `EnableRetryOnFailure` and the readiness probe tolerates a slow start.

**Single replica.** A `BackgroundService` generates the daily briefing; two replicas would generate it twice.
Scaling out would mean moving that to a Container Apps Job (cron) and keeping the web tier stateless,
which it already is now that files and cookie keys live in Blob.

**`EnsureCreated` instead of migrations.** Inherited from the laptop version. Fine for a demo; for
production I would switch to EF migrations applied by a pipeline step.

**AI assistant in the cloud.** Locally it uses Ollama. In Azure it uses the Anthropic API when
`ANTHROPIC_API_KEY` is supplied at deploy time (stored in Key Vault); without a key the core app works
and the AI features report that they're unavailable.

## Deploy

Prerequisites: Azure subscription, [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli).

```bash
az login
scripts/azure-deploy.sh rg-renobudget eastus2            # ~8 minutes on first run
MIN_REPLICAS=1 scripts/azure-deploy.sh                   # keep warm before a live demo
az group delete -n rg-renobudget --yes --no-wait         # tear down; stops all charges
```

The script deploys infrastructure, builds the image in Azure Container Registry, deploys the
Container App, waits for readiness, and runs the smoke test against the live URL.

### Expected cost (idle demo, scale-to-zero)

| Resource | Cost |
|---|---|
| Container Apps | Free grant covers a demo (180k vCPU-s/month) |
| Azure SQL serverless | Free offer (pauses rather than bills when exhausted) |
| Storage, Key Vault, Log Analytics | Cents |
| Container Registry Basic | ~$5/month, the only fixed cost |

## CI/CD (GitHub Actions)

* `ci.yml`, on every push/PR: build API, type-check and build web, lint Bicep, then start the real
  container against SQL Server and Azurite with `docker compose` and run `scripts/smoke-api.sh`.
* `deploy.yml`, after CI passes on `main`: OIDC login, `az acr build`, roll out a new Container App
  revision, smoke test production.

One-time setup for `deploy.yml`:

```bash
APP_ID=$(az ad app create --display-name renobudget-github --query appId -o tsv)
az ad sp create --id "$APP_ID"
az ad app federated-credential create --id "$APP_ID" --parameters '{
  "name": "main", "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<owner>/<repo>:environment:production", "audiences": ["api://AzureADTokenExchange"]}'
az role assignment create --assignee "$APP_ID" --role Contributor \
  --scope "$(az group show -n rg-renobudget --query id -o tsv)"
```

Then in GitHub: create environment `production`; add secrets `AZURE_CLIENT_ID` (=`$APP_ID`),
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; add variables `AZURE_RESOURCE_GROUP`, `AZURE_ACR_NAME`.

## Interview demo script (~5 minutes)

1. **The app** (live URL): log in, add an expense, upload a receipt, show the Reports tab.
2. **Where the receipt went:** Portal → Storage account → `uploads` container. Point out shared keys are
   disabled; the app reached it with its identity.
3. **No secrets:** Container App → Containers → Environment variables. Connection string has no password;
   the AI key is a Key Vault reference.
4. **Observability:** Application Insights → Transaction search. Find the upload request and show the
   end-to-end trace: HTTP → SQL → Blob.
5. **Infrastructure as code:** walk through `infra/main.bicep` top to bottom (the diagram at the top).
6. **Pipeline:** show a green CI run where the container was tested against SQL Server + Azurite.
7. **Talk trade-offs:** pick two from "Design decisions" above.
