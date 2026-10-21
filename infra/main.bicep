// Reno Budget on Azure — everything the app needs, in one resource group.
//
//   Browser ──HTTPS──▶ Container App (ASP.NET Core API + React SPA)
//                         │  user-assigned Managed Identity (no passwords/keys anywhere)
//                         ├──▶ Azure SQL Database (serverless, Entra-only auth)
//                         ├──▶ Blob Storage (receipts + auth-cookie key ring; shared keys disabled)
//                         ├──▶ AI Document Intelligence (receipt + invoice extraction, keyless)
//                         ├──▶ Claude (optional, off by default: Microsoft Foundry keyless, or Anthropic API key)
//                         ├──▶ Key Vault (holds the Anthropic API key if one is supplied)
//                         └──▶ Application Insights / Log Analytics (traces, logs, metrics)
//   GitHub Container Registry ──public image──▶ Container App
//   Cost Management budget ──email──▶ you, before anything is billed
//
// Built to run at $0: every resource is on a free tier or free grant, the app scales to zero,
// log ingestion is capped under the free 5 GB/month, and the budget alert is the safety net.
// Deploy with scripts/azure-deploy.sh. containerImage='' creates the infrastructure only.

targetScope = 'resourceGroup'

@description('Short name used as a prefix for every resource.')
@minLength(3)
@maxLength(12)
param appName string = 'renobudget'

param location string = resourceGroup().location

@description('Public image, e.g. ghcr.io/<owner>/renobudget:abc123 (GitHub Container Registry is free). Empty = infrastructure only.')
param containerImage string = ''

@description('AI assistant: off = free (assistant reports unavailable); foundry = Claude in Microsoft Foundry via managed identity (billed per token, needs pay-as-you-go); anthropic = Claude API key (billed per token).')
@allowed([ 'off', 'foundry', 'anthropic' ])
param claudeMode string = 'off'

@description('Anthropic API key, used only when claudeMode = anthropic. Stored in Key Vault, never in app config.')
@secure()
param anthropicApiKey string = ''

@description('Claude model. With foundry this is also the deployment name.')
param claudeModel string = 'claude-opus-5-5'

@description('Foundry only: Anthropic requires the organization name, country and industry of the user (Marketplace attestation).')
param claudeOrganizationName string = ''
param claudeCountryCode string = 'US'
@allowed([ 'technology', 'finance', 'healthcare', 'education', 'retail', 'manufacturing', 'government', 'media', 'other' ])
param claudeIndustry string = 'technology'

@description('Email for the cost alert. Empty = no budget.')
param budgetEmail string = ''

@description('Monthly budget in your billing currency; alerts at 50% actual and 100% forecast.')
param budgetAmount int = 1

@description('First day of the current month; budgets cannot start in the past. Leave the default.')
param budgetStartDate string = utcNow('yyyy-MM-01')

@description('0 = scale to zero when idle (cheapest; ~10s cold start). 1 = always warm for a live demo.')
@minValue(0)
@maxValue(3)
param minReplicas int = 0

@description('F0 is free (500 pages/month) but only one F0 Document Intelligence resource is allowed per subscription.')
@allowed([ 'F0', 'S0' ])
param docIntelSku string = 'F0'

var suffix = uniqueString(resourceGroup().id, appName)
var tags = { app: appName, managedBy: 'bicep' }
var useFoundry = claudeMode == 'foundry'
var useAnthropicKey = claudeMode == 'anthropic' && !empty(anthropicApiKey)
var databaseName = 'renobudget'

// Built-in role definition IDs
var roleBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var roleKeyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var roleCognitiveServicesUser = 'a97b65f3-24c7-4388-baec-2e87135dc908'

// ---------------------------------------------------------------------------- identity
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}'
  location: location
  tags: tags
}

// ---------------------------------------------------------------------------- monitoring
resource logs 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${appName}-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30                                  // 31 days are included free
    workspaceCapping: { dailyQuotaGb: json('0.15') }     // ~4.5 GB/month, under the free 5 GB
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------------------------------------------------------------------------- blob storage
resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: take('st${appName}${suffix}', 24)
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    allowSharedKeyAccess: false       // identity-based access only
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 7 }   // soft delete: recover accidentally deleted receipts
  }
}

resource uploadsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'uploads'
}

resource blobDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, roleBlobDataContributor)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleBlobDataContributor)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------- key vault
resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: take('kv-${appName}-${suffix}', 24)
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

resource aiKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (useAnthropicKey) {
  parent: vault
  name: 'anthropic-api-key'
  properties: { value: anthropicApiKey }
}

resource vaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, identity.id, roleKeyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKeyVaultSecretsUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------- ai document intelligence
resource docIntel 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: 'di-${appName}-${suffix}'
  location: location
  tags: tags
  kind: 'FormRecognizer'
  sku: { name: docIntelSku }
  properties: {
    customSubDomainName: 'di-${appName}-${suffix}'   // required for Entra ID (token) auth
    disableLocalAuth: true                           // API keys off: Managed Identity only
    publicNetworkAccess: 'Enabled'
  }
}

resource docIntelUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: docIntel
  name: guid(docIntel.id, identity.id, roleCognitiveServicesUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleCognitiveServicesUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------- claude on microsoft foundry (optional)
// A Foundry (AIServices) account has no fixed fee; the Claude deployment bills per token through Azure Marketplace.
resource foundry 'Microsoft.CognitiveServices/accounts@2025-10-01-preview' = if (useFoundry) {
  name: 'ai-${appName}-${suffix}'
  location: location
  tags: tags
  kind: 'AIServices'
  sku: { name: 'S0' }
  identity: { type: 'SystemAssigned' }
  properties: {
    customSubDomainName: 'ai-${appName}-${suffix}'
    allowProjectManagement: true
    disableLocalAuth: true                           // API keys off: Managed Identity only
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-10-01-preview' = if (useFoundry) {
  parent: foundry
  name: appName
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {}
}

resource foundryUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useFoundry) {
  scope: foundry
  name: guid(foundry.id, identity.id, roleCognitiveServicesUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleCognitiveServicesUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource claude 'Microsoft.CognitiveServices/accounts/deployments@2025-10-01-preview' = if (useFoundry) {
  parent: foundry
  name: claudeModel
  sku: { name: 'GlobalStandard', capacity: 25 }      // 25k tokens/minute
  properties: {
    model: { format: 'Anthropic', name: claudeModel, version: '1' }
    // Not yet in Bicep's type definitions; same shape as Microsoft's Claude starter kit (Azure-Samples/claude).
    #disable-next-line BCP037
    modelProviderData: {
      organizationName: claudeOrganizationName
      countryCode: claudeCountryCode
      industry: claudeIndustry
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
    raiPolicyName: 'Microsoft.DefaultV2'
  }
  dependsOn: [ foundryProject, foundryUser ]
}

// ---------------------------------------------------------------------------- azure sql
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: 'sql-${appName}-${suffix}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    administrators: {
      // Entra ID only: SQL logins/passwords are disabled entirely. The app's identity is the
      // admin so EF Core can create the schema on first start. (Production hardening: make an
      // Entra group the admin and grant the app a contained user with narrower roles.)
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: identity.name
      sid: identity.properties.clientId
      principalType: 'Application'
      tenantId: subscription().tenantId
    }
  }
}

resource sqlAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: { startIpAddress: '0.0.0.0', endIpAddress: '0.0.0.0' }
}

resource sqlDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose' }   // serverless
  properties: {
    useFreeLimit: true                       // Azure SQL free offer: 100k vCore-seconds/month
    freeLimitExhaustionBehavior: 'AutoPause' // pause instead of billing when the free limit runs out
    autoPauseDelay: 60
    minCapacity: json('0.5')
  }
}

// ---------------------------------------------------------------------------- container app
resource containerEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-${appName}-${suffix}'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var sqlConnection = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${databaseName};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;Connect Timeout=60'

var baseEnv = [
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
  { name: 'Database__Provider', value: 'sqlserver' }
  { name: 'ConnectionStrings__DefaultConnection', value: sqlConnection }
  { name: 'Storage__Provider', value: 'azure' }
  { name: 'Storage__BlobEndpoint', value: storage.properties.primaryEndpoints.blob }
  { name: 'Storage__Container', value: uploadsContainer.name }
  { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
  { name: 'DocumentIntelligence__Endpoint', value: docIntel.properties.endpoint }
  { name: 'Anthropic__Model', value: claudeModel }
  // 'ollama' has no server in Azure, so with claudeMode=off the assistant answers 503 "unavailable".
  { name: 'Llm__Provider', value: useFoundry ? 'foundry' : (useAnthropicKey ? 'anthropic' : 'ollama') }
]
var aiEnv = useFoundry
  ? [ { name: 'Foundry__Resource', value: foundry.name } ]
  : (useAnthropicKey ? [ { name: 'Anthropic__ApiKey', secretRef: 'anthropic-api-key' } ] : [])

resource app 'Microsoft.App/containerApps@2024-03-01' = if (!empty(containerImage)) {
  name: 'ca-${appName}'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    managedEnvironmentId: containerEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: useAnthropicKey ? [ { name: 'anthropic-api-key', keyVaultUrl: aiKeySecret!.properties.secretUri, identity: identity.id } ] : []
    }
    template: {
      containers: [
        {
          name: 'api'
          image: containerImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(baseEnv, aiEnv)
          probes: [
            { type: 'Liveness', httpGet: { path: '/healthz', port: 8080 }, periodSeconds: 30 }
            // Serverless SQL can take up to a minute to resume, so be patient before routing traffic.
            { type: 'Readiness', httpGet: { path: '/healthz/ready', port: 8080 }, periodSeconds: 10, failureThreshold: 12 }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: 1   // the daily-briefing BackgroundService must run exactly once
        rules: [ { name: 'http', http: { metadata: { concurrentRequests: '50' } } } ]
      }
    }
  }
  dependsOn: [ blobDataContributor, vaultSecretsUser, docIntelUser, foundryUser, claude, sqlAllowAzure, sqlDb ]
}

// ---------------------------------------------------------------------------- cost guard
// Budgets are free. Azure emails before money is spent: at 50% of actual spend and at 100% forecast.
resource budget 'Microsoft.Consumption/budgets@2023-11-01' = if (!empty(budgetEmail)) {
  name: 'budget-${appName}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: { startDate: budgetStartDate }
    notifications: {
      actual50: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 50, thresholdType: 'Actual', contactEmails: [ budgetEmail ] }
      forecast100: { enabled: true, operator: 'GreaterThanOrEqualTo', threshold: 100, thresholdType: 'Forecasted', contactEmails: [ budgetEmail ] }
    }
  }
}

output containerAppName string = !empty(containerImage) ? app!.name : ''
output appUrl string = !empty(containerImage) ? 'https://${app!.properties.configuration.ingress.fqdn}' : ''
output identityClientId string = identity.properties.clientId
