targetScope = 'resourceGroup'

@description('Azure region for the managed foundation.')
param location string = resourceGroup().location

@description('Workload name segment used in Azure resource names.')
param workloadName string = 'openjibo'

@description('Deployment environment name segment used in Azure resource names.')
param environmentName string = 'managed'

var uniqueSuffix = uniqueString(resourceGroup().id)
var compactName = replace('${workloadName}${environmentName}', '-', '')
var resolvedLogAnalyticsWorkspaceName = empty(logAnalyticsWorkspaceName) ? 'log-${workloadName}-${environmentName}' : logAnalyticsWorkspaceName
var resolvedContainerRegistryName = empty(containerRegistryName) ? 'cr${compactName}${uniqueSuffix}' : containerRegistryName
var resolvedKeyVaultName = empty(keyVaultName) ? 'kv-${take(workloadName, 7)}-${take(environmentName, 5)}-${take(uniqueSuffix, 6)}' : keyVaultName
var resolvedStorageAccountName = empty(storageAccountName) ? 'st${take(compactName, 11)}${take(uniqueSuffix, 11)}' : storageAccountName

@description('Name of the Log Analytics workspace. Leave blank to use the standard Open Jibo generated name.')
param logAnalyticsWorkspaceName string = ''

@description('Name of the Azure Container Registry. Leave blank to use the standard Open Jibo generated name.')
param containerRegistryName string = ''

@description('Name of the Key Vault used by Open Jibo managed. Leave blank to use the standard Open Jibo generated name.')
param keyVaultName string = ''

@description('Name of the storage account used by Open Jibo managed. Leave blank to use the standard Open Jibo generated name.')
param storageAccountName string = ''

@description('Object ID of the principal that seeds Key Vault secrets after deployment. Leave blank to skip bootstrap secret access policy.')
param keyVaultSecretSeederPrincipalId string = ''

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: resolvedLogAnalyticsWorkspaceName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: resolvedContainerRegistryName
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    adminUserEnabled: true
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: resolvedKeyVaultName
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: false
    accessPolicies: empty(keyVaultSecretSeederPrincipalId) ? [] : [
      {
        tenantId: subscription().tenantId
        objectId: keyVaultSecretSeederPrincipalId
        permissions: {
          secrets: [
            'get'
            'list'
            'set'
            'delete'
          ]
        }
      }
    ]
    softDeleteRetentionInDays: 30
    publicNetworkAccess: 'Enabled'
  }
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: resolvedStorageAccountName
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

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output storageAccountName string = storageAccount.name
