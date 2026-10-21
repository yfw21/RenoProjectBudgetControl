# Running Reno Budget on Azure

## What changed to make it cloud-ready

The app started as a laptop app: SQLite file, uploads on local disk, local LLM. Each of those was turned
into a configuration switch rather than a fork of the code, so the same image runs everywhere.

| Concern | Laptop | Azure | Where |
|---|---|---|---|
| Database | SQLite | Azure SQL (serverless), EF Core retry on transient errors | `Program.cs` `Database:Provider` |
| File uploads | `Files:Root` folder | Blob Storage via `IFileStore` | `Services/Storage/` |
| Auth cookie keys | in-memory/user profile | Persisted to Blob so restarts/redeploys don't log users out | `Program.cs` Data Protection |
| Receipt/invoice reading | Tesseract OCR + local LLM | Azure AI Document Intelligence prebuilt models, local pipeline as fallback | `Services/Receipts/DocumentIntelligenceExtractor.cs` |
| AI assistant | Ollama (free, local) | Claude via the Anthropic SDK: Microsoft Foundry with managed identity, or Claude API; off by default to stay at $0 | `Services/Llm/AnthropicLlmClient.cs` |
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

**Azure AI Document Intelligence for receipts and invoices.** Receipts go to `prebuilt-receipt`
(merchant, date, total, items); contractor invoices go to `prebuilt-invoice` (vendor, invoice number, line
items with quantity, unit price and amount). Each field comes back with a confidence score, and the UI shows
it beside the field and flags anything under 80% for the user to check: the model proposes, a person confirms
before anything is saved (human in the loop). Why a prebuilt model and not an LLM prompt: it is purpose-trained
for these documents, returns typed values (dates, currency) instead of free text to parse, gives calibrated
confidence, and needs no prompt engineering. API keys are disabled on the resource; the app calls it with its
Managed Identity (Cognitive Services User role). If the service is unreachable the app falls back to local
Tesseract OCR and tells the user, so an outage degrades the feature instead of breaking uploads.
*Trade-off:* each page costs money beyond the free 500 pages/month, and documents leave the app for Azure
(same region, not used for training), which matters for the privacy conversation.

**AI assistant: Claude, behind one interface.** The assistant answers questions by calling the app's own tools
(list projects, add an expense, schedule a task...). `ILlmClient` hides the model, so the same loop runs on:

* **Ollama** on a laptop: free and local.
* **Claude on Microsoft Foundry** (`claudeMode=foundry`): Claude deployed in my Azure subscription and billed through
  Azure. The app signs in with its managed identity (Entra ID token, scope `https://ai.azure.com/.default`,
  *Cognitive Services User* role); keys are disabled on the account.
* **Claude API** (`claudeMode=anthropic`): an API key held in Key Vault.

Both Claude paths use the official Anthropic C# SDK (`Anthropic.Foundry` for Azure). Details that matter:
the system prompt and tool list are marked for prompt caching (they are identical on every turn); Claude's thinking
blocks are replayed unchanged between tool calls, which current models require; rate limits, outages and sign-in
failures become a 503 "assistant unavailable" instead of a crash; a refusal gets a polite reply.
*Trade-off:* Claude is billed per token on both paths, so it is **off by default** and the deployed demo stays free.
Claude on Foundry also needs a pay-as-you-go subscription (not free trial or credit-only).

## Deploy

Prerequisites: Azure subscription, [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), and a
public image in GitHub Container Registry (the Deploy workflow pushes one; or set `GHCR_OWNER` and the script builds it
with Docker).

```bash
az login
export BUDGET_EMAIL=you@example.com                      # cost alert, strongly recommended
IMAGE=ghcr.io/<owner>/renobudget:<tag> scripts/azure-deploy.sh rg-renobudget eastus2   # ~8 minutes
MIN_REPLICAS=1 IMAGE=... scripts/azure-deploy.sh         # keep warm for the hour of a live demo, then redeploy with 0
az group delete -n rg-renobudget --yes --no-wait         # tear down
```

The script deploys the infrastructure and the Container App, waits for readiness, and runs the smoke test against
the live URL.

### Cost: designed for $0

| Resource | Why it is free |
|---|---|
| Container Apps | Scales to zero; the monthly free grant (180k vCPU-s, 2M requests) covers a demo. Don't leave `MIN_REPLICAS=1` on: always-on uses up the grant in about 4 days |
| Azure SQL serverless | Free offer, 100k vCore-s/month; set to pause, not bill, when it runs out |
| AI Document Intelligence F0 | 500 pages/month (one F0 per subscription; `DOC_INTEL_SKU=S0` is paid) |
| Log Analytics + App Insights | First 5 GB/month free; ingestion capped at 0.15 GB/day in the template |
| Storage, Key Vault | Pay per use; a demo's few MB and few reads round to $0.00 |
| Container image | GitHub Container Registry, free for public images (replaced Azure Container Registry, ~$5/month) |
| Budget alert | Free; emails at 50% of a $1 budget and when spend is forecast to pass it |
| Claude (optional) | **Paid per token**, so `claudeMode=off` by default. A Claude.ai subscription does not include API usage |

## CI/CD (GitHub Actions)

* `ci.yml`, on every push/PR: build API, type-check and build web, lint Bicep, then start the real
  container against SQL Server and Azurite with `docker compose` and run `scripts/smoke-api.sh`.
* `deploy.yml`, after CI passes on `main`: build and push the image to GitHub Container Registry (with the
  workflow's own token), OIDC login to Azure, roll out a new Container App revision, smoke test production.

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
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`; add variable `AZURE_RESOURCE_GROUP`. After the first push, make the
`renobudget` package public (GitHub → Packages → Settings) so Container Apps can pull it without credentials.

## Interview demo script (~5 minutes)

1. **The app** (live URL): log in, **Smart upload** a photo of a receipt. Point at the confidence badges and
   the amber "check" flag: Document Intelligence proposes, a person confirms. Then **Smart invoice** a
   contractor PDF to show line items extracted, and the Reports tab.
   Portal → Document Intelligence resource → Keys and Endpoint shows keys are disabled.
2. **Where the receipt went:** Portal → Storage account → `uploads` container. Point out shared keys are
   disabled; the app reached it with its identity.
3. **No secrets:** Container App → Containers → Environment variables. Connection string has no password;
   the AI key is a Key Vault reference.
4. **Observability:** Application Insights → Transaction search. Find the upload request and show the
   end-to-end trace: HTTP → Document Intelligence → SQL → Blob.
5. **Infrastructure as code:** walk through `infra/main.bicep` top to bottom (the diagram at the top).
6. **Pipeline:** show a green CI run where the container was tested against SQL Server + Azurite.
7. **Cost:** Cost Management → Budgets shows the $1 alert; walk the "designed for $0" table.
8. **AI assistant:** show `AnthropicLlmClient.cs` (one SDK, Foundry or Claude API) and the tests that check the
   request carries an Entra token and no key. Run the assistant live on the laptop with Ollama.
9. **Talk trade-offs:** pick two from "Design decisions" above.

## Testing Document Intelligence

* `dotnet test tests/BudgetApi.Tests` maps receipt and invoice responses in the real REST wire format
  (`tests/BudgetApi.Tests/Fixtures/*.json`) through the SDK's own deserializer.
* Against the real service from a laptop: `az login`, give yourself the *Cognitive Services User* role on the
  resource, then `DocumentIntelligence__Endpoint=https://<name>.cognitiveservices.azure.com/ dotnet run` in
  `BudgetApi/`. `DefaultAzureCredential` uses your CLI login, so there is still no key.

## Testing the Claude client (no API spend)

* `dotnet test tests/BudgetApi.Tests` runs the real Anthropic SDK against a stub HTTP handler (`ClaudeClientTests`):
  Foundry requests go to `https://<resource>.services.ai.azure.com/anthropic/v1/messages` with an Entra bearer token
  and no API key; the tool loop replays Claude's thinking and `tool_use` blocks and batches the tool results;
  refusals, 429s and 5xx are handled.
* Against Claude on Foundry from a laptop: `az login`, give yourself *Cognitive Services User* on the Foundry account,
  then `Llm__Provider=foundry Foundry__Resource=<account> dotnet run` in `BudgetApi/`.
