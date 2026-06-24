targetScope = 'resourceGroup'

@description('Azure region for the managed deployment.')
param location string = resourceGroup().location

@description('Workload name segment used in Azure resource names.')
param workloadName string = 'openjibo'

@description('Deployment environment name segment used in Azure resource names.')
param environmentName string = 'managed'

@description('Name of the Container Apps environment.')
param managedEnvironmentName string = 'cae-${workloadName}-${environmentName}'

@description('Name of the Azure Container Apps resource.')
param containerAppName string = 'ca-${workloadName}-${environmentName}'

@description('Login server for Azure Container Registry, for example myregistry.azurecr.io.')
param registryLoginServer string

@description('Name of the Key Vault that stores managed secrets.')
param keyVaultName string

@description('Tag for the managed Open Jibo image.')
param imageTag string = 'managed'

@description('Minimum number of replicas for the runtime container.')
param minReplicas int = 1

@description('Maximum number of replicas for the runtime container.')
param maxReplicas int = 2

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: 'log-${workloadName}-${environmentName}'
}

var logAnalyticsWorkspaceKey = listKeys(logAnalyticsWorkspace.id, '2022-10-01').primarySharedKey
var registryName = split(registryLoginServer, '.')[0]
var registryCredentials = listCredentials(resourceId('Microsoft.ContainerRegistry/registries', registryName), '2023-07-01')
var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource managedEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: managedEnvironmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspaceKey
      }
    }
  }
}

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: managedEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: registryLoginServer
          username: registryName
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: registryCredentials.passwords[0].value
        }
        {
          name: 'state-connection-string'
          keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/openjibo-state-connection-string'
          identity: 'system'
        }
        {
          name: 'personal-memory-connection-string'
          keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/openjibo-personal-memory-connection-string'
          identity: 'system'
        }
        {
          name: 'media-connection-string'
          keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/openjibo-media-connection-string'
          identity: 'system'
        }
        {
          name: 'open-weather-api-key'
          keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/openjibo-openweather-api-key'
          identity: 'system'
        }
        {
          name: 'news-api-key'
          keyVaultUrl: 'https://${keyVaultName}.vault.azure.net/secrets/openjibo-newsapi-key'
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'openjibo-cloud'
          image: '${registryLoginServer}/openjibo-cloud:${imageTag}'
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8080'
            }
            {
              name: 'OpenJibo__State__Backend'
              value: 'AzureBlob'
            }
            {
              name: 'OpenJibo__PersonalMemory__Backend'
              value: 'AzureBlob'
            }
            {
              name: 'OpenJibo__Media__Backend'
              value: 'AzureBlob'
            }
            {
              name: 'OpenJibo__State__ConnectionString'
              secretRef: 'state-connection-string'
            }
            {
              name: 'OpenJibo__PersonalMemory__ConnectionString'
              secretRef: 'personal-memory-connection-string'
            }
            {
              name: 'OpenJibo__Media__ConnectionString'
              secretRef: 'media-connection-string'
            }
            {
              name: 'OPENWEATHER_API_KEY'
              secretRef: 'open-weather-api-key'
            }
            {
              name: 'NEWSAPI_KEY'
              secretRef: 'news-api-key'
            }
          ]
          resources: {
            cpu: 1
            memory: '2Gi'
          }
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}


resource keyVaultContainerAppSecretsUserRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, containerApp.identity.principalId, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    principalId: containerApp.identity.principalId
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalType: 'ServicePrincipal'
  }
}

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
