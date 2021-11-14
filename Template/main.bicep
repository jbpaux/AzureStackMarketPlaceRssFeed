@description('Storage account name where Azure Function will be stored and where RSS Feed will be available')
param storageAccountName string

@description('Key vault name used to store connection string securely')
param keyVaultName string

@description('Azure Function Name')
param functionAppName string

@description('Location for all resources.')
param location string = resourceGroup().location

@description('CRON expression for Timer Trigger of Azure Function')
param scheduleTrigger string = '0 0 0 */1 * *'

@description('GitHub URL of the MarketPlace changelog')
param marketPlaceUpdatesURL string = 'https://raw.githubusercontent.com/MicrosoftDocs/azure-stack-docs/master/azure-stack/operator/azure-stack-marketplace-changes.md'

@description('Output file for RSS Feed (container/file.xml format)')
param rssPath string = 'rss/feed.xml'

var storageConnectionSecretName = 'StorageConnection'
var appInsightsSecretName = 'appInsightsKey'
var containerName = first(split(rssPath, '/'))

resource sa 'Microsoft.Storage/storageAccounts@2021-04-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    supportsHttpsTrafficOnly: true
    accessTier: 'Hot'
    encryption: {
      services: {
        file: {
          keyType: 'Account'
          enabled: true
        }
        blob: {
          keyType: 'Account'
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
  }
}

resource container 'Microsoft.Storage/storageAccounts/blobServices/containers@2021-04-01' = {
  name: '${sa.name}/default/${containerName}'
  properties: {
    publicAccess: 'Blob'
  }
}

resource functionAppServerFarm 'Microsoft.Web/serverfarms@2021-02-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
}

resource appinsight 'Microsoft.Insights/components@2020-02-02' = {
  name: functionAppName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
}

resource functionApp 'Microsoft.Web/sites@2021-02-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionAppServerFarm.id
  }
}

resource appsettings 'Microsoft.Web/sites/config@2021-02-01' = {
  parent: functionApp
  name: 'appsettings'
  properties: {
    WEBSITE_ENABLE_SYNC_UPDATE_SITE: 'true'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${appInsightsSecret.properties.secretUriWithVersion})'
    AzureWebJobsDashboard: '@Microsoft.KeyVault(SecretUri=${storageConnectionSecret.properties.secretUriWithVersion})'
    AzureWebJobsStorage: '@Microsoft.KeyVault(SecretUri=${storageConnectionSecret.properties.secretUriWithVersion})'
    WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: '@Microsoft.KeyVault(SecretUri=${storageConnectionSecret.properties.secretUriWithVersion})'
    WEBSITE_CONTENTSHARE: toLower(functionAppName)
    FUNCTIONS_WORKER_RUNTIME: 'dotnet'
    StorageConnection: '@Microsoft.KeyVault(SecretUri=${storageConnectionSecret.properties.secretUriWithVersion})'
    ScheduleTriggerTime: scheduleTrigger
    MarketPlaceUpdatesURL: marketPlaceUpdatesURL
    RSSPath: rssPath
  }
}

resource kv 'Microsoft.KeyVault/vaults@2021-04-01-preview' = {
  name: keyVaultName
  location: location
  properties: {
    enabledForDeployment: false
    enabledForTemplateDeployment: false
    enabledForDiskEncryption: false
    tenantId: subscription().tenantId
    accessPolicies: [
      {
        tenantId: functionApp.identity.tenantId
        objectId: functionApp.identity.principalId
        permissions: {
          secrets: [
            'get'
          ]
        }
      }
    ]
    sku: {
      name: 'standard'
      family: 'A'
    }
  }
}

resource storageConnectionSecret 'Microsoft.KeyVault/vaults/secrets@2021-06-01-preview' = {
  parent: kv
  name: storageConnectionSecretName
  properties: {
    value: 'DefaultEndpointsProtocol=https;AccountName=${sa.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${sa.listKeys().keys[0].value}'
  }
}

resource appInsightsSecret 'Microsoft.KeyVault/vaults/secrets@2021-06-01-preview' = {
  parent: kv
  name: appInsightsSecretName
  properties: {
    value: appinsight.properties.InstrumentationKey
  }
}

output rssfeed string = '${sa.properties.primaryEndpoints.blob}${rssPath}'
