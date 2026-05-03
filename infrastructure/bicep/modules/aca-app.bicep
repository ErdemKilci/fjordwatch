@description('Azure region.')
param location string

@description('Parent Container Apps Environment id.')
param envId string

@description('App name (1-32 chars).')
param name string

@description('Container image (e.g. `myacr.azurecr.io/core-api:abc123`).')
param image string

@description('Whether ingress is enabled at all.')
param ingressEnabled bool = false

@description('When `ingressEnabled` is true, whether the ingress is internet-facing.')
param external bool = false

@description('Container port that ingress + healthchecks target.')
param targetPort int = 8080

@description('Min replicas. 0 enables scale-to-zero on an HTTP scale rule.')
param minReplicas int = 0

@description('Max replicas. KEDA-driven autoscaling lives behind ingress on HTTP request rate.')
param maxReplicas int = 1

@description('Plain environment variables.')
param envVars array = []

@description('Optional secret-backed environment variables.')
param secretRefs array = []

@description('Optional secret values (only used when ingress + Key Vault references are not set up).')
param secrets array = []

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  properties: {
    managedEnvironmentId: envId
    configuration: {
      ingress: ingressEnabled ? {
        external: external
        targetPort: targetPort
        transport: 'auto'
        traffic: [
          { latestRevision: true, weight: 100 }
        ]
      } : null
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: name
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat(envVars, secretRefs)
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/healthz', port: targetPort }
              initialDelaySeconds: 15
              periodSeconds: 20
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/readyz', port: targetPort }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
        rules: ingressEnabled && external ? [
          {
            name: 'http-rule'
            http: {
              metadata: { concurrentRequests: '50' }
            }
          }
        ] : null
      }
    }
  }
}

output id string = app.id
output fqdn string = ingressEnabled ? app.properties.configuration.ingress.fqdn : ''
