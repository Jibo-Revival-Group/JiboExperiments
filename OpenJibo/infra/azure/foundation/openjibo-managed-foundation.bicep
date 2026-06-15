targetScope = 'resourceGroup'

@description('Azure region for the managed foundation.')
param location string = resourceGroup().location

@description('Name of the Log Analytics workspace.')
param logAnalyticsWorkspaceName string = 'openjibo-managed-logs'

@description('Name of the Azure Container Registry.')
param containerRegistryName string = 'openjiboacr'

@description('Name of the Key Vault used by Open Jibo managed.')
param keyVaultName string = 'openjibokv'

@description('Name of the storage account used by Open Jibo managed.')
param storageAccountName string = 'openjibostore'

@secure()
@description('Azure SQL connection string for Open Jibo state persistence.')
param stateConnectionString string

@secure()
@description('Azure SQL connection string for Open Jibo personal memory persistence.')
param personalMemoryConnectionString string

@secure()
@description('Azure Blob Storage connection string for Open Jibo media persistence.')
param mediaConnectionString string

@secure()
@description('Optional OpenWeather API key.')
param openWeatherApiKey string = ''

@secure()
@description('Optional NewsAPI key.')
param newsApiKey string = ''

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

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    adminUserEnabled: false
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    softDeleteRetentionInDays: 30
    publicNetworkAccess: 'Enabled'
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource stateConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'openjibo-state-connection-string'
  properties: {
    value: stateConnectionString
  }
}

resource personalMemoryConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'openjibo-personal-memory-connection-string'
  properties: {
    value: personalMemoryConnectionString
  }
}

resource mediaConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'openjibo-media-connection-string'
  properties: {
    value: mediaConnectionString
  }
}

resource openWeatherSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(openWeatherApiKey)) {
  parent: keyVault
  name: 'openjibo-openweather-api-key'
  properties: {
    value: openWeatherApiKey
  }
}

resource newsApiSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(newsApiKey)) {
  parent: keyVault
  name: 'openjibo-newsapi-key'
  properties: {
    value: newsApiKey
  }
}

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output registryLoginServer string = registry.properties.loginServer
output storageAccountName string = storageAccount.name
output storageConnectionString string = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${listKeys(storageAccount.id, '2023-05-01').keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
