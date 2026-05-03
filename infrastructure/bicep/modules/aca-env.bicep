@description('Azure region.')
param location string

@description('Container Apps Environment name.')
param name string

@description('Log Analytics workspace customer ID.')
param workspaceCustomerId string

@secure()
@description('Log Analytics workspace primary shared key.')
param workspacePrimaryKey string

resource env 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: name
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspaceCustomerId
        sharedKey: workspacePrimaryKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

output id string = env.id
output name string = env.name
output defaultDomain string = env.properties.defaultDomain
