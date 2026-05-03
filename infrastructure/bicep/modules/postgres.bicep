@description('Azure region.')
param location string

@description('Server name (must be globally unique within region).')
param name string

@description('Postgres administrator login.')
param administratorLogin string

@secure()
@description('Postgres administrator password.')
param administratorPassword string

@description('Compute SKU. Default `Standard_B1ms` is the cheapest burstable option that supports PostGIS.')
param skuName string = 'Standard_B1ms'

@description('Compute tier.')
param skuTier string = 'Burstable'

@description('Storage size in GB.')
param storageSizeGB int = 32

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    version: '16'
    storage: {
      storageSizeGB: storageSizeGB
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: 7
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// Pre-load the extensions FjordWatch needs. Container Apps cannot CREATE
// EXTENSION arbitrarily; the extension must first be in this allow-list.
resource azureExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: server
  name: 'azure.extensions'
  properties: {
    value: 'POSTGIS,VECTOR,PG_STAT_STATEMENTS'
    source: 'user-override'
  }
}

resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: server
  name: 'allow-azure-services'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource fjordwatchDb 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: server
  name: 'fjordwatch'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

output id string = server.id
output fqdn string = server.properties.fullyQualifiedDomainName
output name string = server.name
