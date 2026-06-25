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
var resolvedPostgresServerName = empty(postgresServerName) ? 'psql-${take(workloadName, 12)}-${take(environmentName, 8)}-${take(uniqueSuffix, 8)}' : postgresServerName

@description('Name of the Log Analytics workspace. Leave blank to use the standard Open Jibo generated name.')
param logAnalyticsWorkspaceName string = ''

@description('Name of the Azure Container Registry. Leave blank to use the standard Open Jibo generated name.')
param containerRegistryName string = ''

@description('Name of the Key Vault used by Open Jibo managed. Leave blank to use the standard Open Jibo generated name.')
param keyVaultName string = ''

@description('Name of the storage account used by Open Jibo managed. Leave blank to use the standard Open Jibo generated name.')
param storageAccountName string = ''

@description('Name of the Azure Database for PostgreSQL flexible server. Leave blank to use the standard Open Jibo generated name.')
param postgresServerName string = ''

@description('Administrator login name for the managed PostgreSQL server.')
param postgresAdministratorLogin string = 'openjiboadmin'

@secure()
@description('Administrator password for the managed PostgreSQL server.')
param postgresAdministratorPassword string

@description('PostgreSQL engine major version for the managed server.')
param postgresVersion string = '16'

@description('PostgreSQL compute SKU name.')
param postgresSkuName string = 'Standard_B1ms'

@description('PostgreSQL compute SKU tier.')
param postgresSkuTier string = 'Burstable'

@minValue(32)
@description('PostgreSQL storage size in GiB.')
param postgresStorageSizeGb int = 32

@description('Database name used by the Open Jibo state snapshot store.')
param postgresStateDatabaseName string = 'openjibo_state'

@description('Database name used by the Open Jibo personal memory snapshot store.')
param postgresPersonalMemoryDatabaseName string = 'openjibo_memory'

@description('Allow public Azure services, including the managed Container App, to reach the PostgreSQL server through a 0.0.0.0 firewall rule.')
param postgresAllowAzureServices bool = true

@description('Optional public IPv4 address for the deployment runner. When provided, the foundation adds a narrow firewall rule so deploy-time migrations can reach PostgreSQL.')
param postgresDeploymentRunnerFirewallIpAddress string = ''

@description('Object ID for the principal that seeds foundation Key Vault secrets. Leave blank to skip adding a secret seed access policy.')
param seedPrincipalObjectId string = ''

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
    softDeleteRetentionInDays: 30
    accessPolicies: []
    enableRbacAuthorization: false
    publicNetworkAccess: 'Enabled'
  }
}

resource keyVaultSecretSeedAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = if (!empty(seedPrincipalObjectId)) {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: subscription().tenantId
        objectId: seedPrincipalObjectId
        permissions: {
          secrets: [
            'get'
            'list'
            'set'
          ]
        }
      }
    ]
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

resource postgresServer 'Microsoft.DBforPostgreSQL/flexibleServers@2023-06-01-preview' = {
  name: resolvedPostgresServerName
  location: location
  sku: {
    name: postgresSkuName
    tier: postgresSkuTier
  }
  properties: {
    version: postgresVersion
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
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
    storage: {
      storageSizeGB: postgresStorageSizeGb
    }
  }
}

resource postgresStateDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  parent: postgresServer
  name: postgresStateDatabaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource postgresPersonalMemoryDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-06-01-preview' = {
  parent: postgresServer
  name: postgresPersonalMemoryDatabaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource postgresAllowAzureServicesFirewallRule 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-06-01-preview' = if (postgresAllowAzureServices) {
  parent: postgresServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource postgresDeploymentRunnerFirewallRule 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-06-01-preview' = if (!empty(postgresDeploymentRunnerFirewallIpAddress)) {
  parent: postgresServer
  name: 'AllowDeploymentRunner'
  properties: {
    startIpAddress: postgresDeploymentRunnerFirewallIpAddress
    endIpAddress: postgresDeploymentRunnerFirewallIpAddress
  }
}

output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output storageAccountName string = storageAccount.name
output postgresServerName string = postgresServer.name
output postgresFullyQualifiedDomainName string = postgresServer.properties.fullyQualifiedDomainName
output postgresStateDatabaseName string = postgresStateDatabase.name
output postgresPersonalMemoryDatabaseName string = postgresPersonalMemoryDatabase.name
output postgresAdministratorLogin string = postgresAdministratorLogin
