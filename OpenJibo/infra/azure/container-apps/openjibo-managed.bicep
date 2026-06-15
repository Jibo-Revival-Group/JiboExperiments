targetScope = 'resourceGroup'

@description('Azure region for the managed deployment.')
param location string = resourceGroup().location

@description('Name of the Container Apps environment.')
param managedEnvironmentName string = 'openjibo-managed-env'

@description('Name of the Azure Container Apps resource.')
param containerAppName string = 'openjibo-cloud'

@description('Log Analytics workspace name used by the Container Apps environment.')
param logAnalyticsWorkspaceName string = 'openjibo-managed-logs'

@description('Login server for Azure Container Registry, for example myregistry.azurecr.io.')
param registryLoginServer string

@secure()
@description('Username for Azure Container Registry.')
param registryUsername string

@secure()
@description('Password for Azure Container Registry.')
param registryPassword string

@description('Repository name for the managed Open Jibo image.')
param imageRepository string = 'openjibo-cloud'

@description('Image tag for the managed Open Jibo image.')
param imageTag string = 'managed'

@secure()
@description('Azure SQL connection string for Open Jibo state persistence.')
param stateConnectionString string

@secure()
@description('Azure SQL connection string for Open Jibo personal memory persistence.')
param personalMemoryConnectionString string

@secure()
@description('Azure Blob Storage connection string for media persistence.')
param mediaConnectionString string

@secure()
@description('Optional OpenWeather API key.')
param openWeatherApiKey string = ''

@secure()
@description('Optional NewsAPI key.')
param newsApiKey string = ''

@description('Minimum number of replicas for the runtime container.')
param minReplicas int = 1

@description('Maximum number of replicas for the runtime container.')
param maxReplicas int = 2

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

var logAnalyticsWorkspaceKey = listKeys(logAnalyticsWorkspace.id, '2022-10-01').primarySharedKey

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
          username: registryUsername
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: registryPassword
        }
        {
          name: 'state-connection-string'
          value: stateConnectionString
        }
        {
          name: 'personal-memory-connection-string'
          value: personalMemoryConnectionString
        }
        {
          name: 'media-connection-string'
          value: mediaConnectionString
        }
        {
          name: 'open-weather-api-key'
          value: openWeatherApiKey
        }
        {
          name: 'news-api-key'
          value: newsApiKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'openjibo-cloud'
          image: '${registryLoginServer}/${imageRepository}:${imageTag}'
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
              value: 'AzureSql'
            }
            {
              name: 'OpenJibo__PersonalMemory__Backend'
              value: 'AzureSql'
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

output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
