@description('Azure region.')
param location string

@description('Globally unique ACR name (lowercase alphanumeric, 5-50 chars).')
param name string

resource acr 'Microsoft.ContainerRegistry/registries@2023-11-01-preview' = {
  name: name
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    zoneRedundancy: 'Disabled'
  }
}

output id string = acr.id
output loginServer string = acr.properties.loginServer
output name string = acr.name
