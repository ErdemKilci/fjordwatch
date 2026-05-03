// FjordWatch — Azure deployment entry point.
//
// Deploys (or what-if's) the full cloud topology to a single resource group:
//   - Container Registry
//   - Postgres Flexible Server with PostGIS + pgvector enabled
//   - Storage Account (sar-tiles, fixtures containers)
//   - Key Vault
//   - Log Analytics + Application Insights
//   - Container Apps Environment
//   - One Container App per FjordWatch service plus a Redis sidecar.
//
// `make deploy DRY_RUN=1` runs `az deployment group what-if` against this
// template; `make deploy` runs `az deployment group create`. The local
// docker compose stack is unaffected by any change here.

targetScope = 'resourceGroup'

@description('Short environment name (dev, staging, prod). Used as a suffix on resource names.')
param environment string = 'dev'

@description('Azure region. Must support Container Apps + Postgres Flexible Server.')
param location string = resourceGroup().location

@description('Postgres administrator login. Choose something other than `postgres` to avoid the default deny rules.')
param postgresAdminLogin string = 'fjordwatch_admin'

@secure()
@description('Postgres administrator password.')
param postgresAdminPassword string

@description('Image tag every Container App pulls. Set by the deploy workflow to the commit SHA; defaults to `dev`.')
param imageTag string = 'dev'

@description('Public LLM provider for the agent. Either `ollama` (in-cluster sidecar) or `azure_openai`.')
@allowed([
  'ollama'
  'azure_openai'
])
param llmProvider string = 'ollama'

var prefix = 'fjordwatch-${environment}'

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    location: location
    name: replace('${prefix}acr', '-', '')
  }
}

module postgres 'modules/postgres.bicep' = {
  name: 'postgres'
  params: {
    location: location
    name: '${prefix}-pg'
    administratorLogin: postgresAdminLogin
    administratorPassword: postgresAdminPassword
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    name: replace('${prefix}st', '-', '')
  }
}

module keyvault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    name: '${prefix}-kv'
  }
}

module insights 'modules/insights.bicep' = {
  name: 'insights'
  params: {
    location: location
    name: '${prefix}-ai'
  }
}

module acaEnv 'modules/aca-env.bicep' = {
  name: 'aca-env'
  params: {
    location: location
    name: '${prefix}-env'
    workspaceCustomerId: insights.outputs.workspaceCustomerId
    workspacePrimaryKey: insights.outputs.workspacePrimarySharedKey
  }
}

// Wire the FjordWatch services. Each app pulls its image from ACR and
// receives the same DATABASE_URL / REDIS_URL / S3_ENDPOINT trio so configuration
// is uniform.

var registryServer = registry.outputs.loginServer
var databaseUrl = 'postgres://${postgresAdminLogin}:${postgresAdminPassword}@${postgres.outputs.fqdn}:5432/fjordwatch?sslmode=require'
var redisUrl = 'redis://redis:6379/0'

module redis 'modules/aca-app.bicep' = {
  name: 'redis'
  params: {
    location: location
    envId: acaEnv.outputs.id
    name: '${prefix}-redis'
    image: 'redis:7-alpine'
    ingressEnabled: false
    targetPort: 6379
    minReplicas: 1
    maxReplicas: 1
    envVars: []
  }
}

module aisIngestion 'modules/aca-app.bicep' = {
  name: 'ais-ingestion'
  params: {
    location: location
    envId: acaEnv.outputs.id
    name: '${prefix}-ais'
    image: '${registryServer}/ais-ingestion:${imageTag}'
    ingressEnabled: false
    targetPort: 9100
    minReplicas: 1
    maxReplicas: 1
    envVars: [
      { name: 'DATABASE_URL', value: databaseUrl }
      { name: 'REDIS_URL', value: redisUrl }
      { name: 'AIS_STREAM', value: 'ais:positions' }
      { name: 'AIS_METRICS_LISTEN', value: '0.0.0.0:9100' }
    ]
  }
}

module coreApi 'modules/aca-app.bicep' = {
  name: 'core-api'
  params: {
    location: location
    envId: acaEnv.outputs.id
    name: '${prefix}-api'
    image: '${registryServer}/core-api:${imageTag}'
    ingressEnabled: true
    external: true
    targetPort: 8080
    minReplicas: 0
    maxReplicas: 3
    envVars: [
      { name: 'DATABASE_URL', value: databaseUrl }
      { name: 'REDIS_URL', value: redisUrl }
      { name: 'AIS_STREAM', value: 'ais:positions' }
      { name: 'ASPNETCORE_HTTP_PORTS', value: '8080' }
      { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
      { name: 'CORS_ORIGINS', value: 'https://${prefix}-web.${acaEnv.outputs.defaultDomain}' }
      { name: 'LLM_PROVIDER', value: llmProvider }
      { name: 'OLLAMA_HOST', value: 'http://ollama:11434/' }
      { name: 'EMBEDDING_URL', value: 'http://embedding:8004/' }
    ]
  }
}

module web 'modules/aca-app.bicep' = {
  name: 'web'
  params: {
    location: location
    envId: acaEnv.outputs.id
    name: '${prefix}-web'
    image: '${registryServer}/web:${imageTag}'
    ingressEnabled: true
    external: true
    targetPort: 8080
    minReplicas: 0
    maxReplicas: 2
    envVars: []
  }
}

output webFqdn string = web.outputs.fqdn
output coreApiFqdn string = coreApi.outputs.fqdn
output registryLoginServer string = registry.outputs.loginServer
output keyVaultUri string = keyvault.outputs.uri
output appInsightsConnectionString string = insights.outputs.connectionString
