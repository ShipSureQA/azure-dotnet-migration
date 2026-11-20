// Azure resources for the migrated Orders platform.
// NOT DEPLOYED: this demo runs fully on emulators. The template is compiled (az bicep build)
// in CI to keep it valid. Deploy with:
//   az deployment group create -g rg-orders-demo -f infra/main.bicep -p apimPublisherEmail=you@example.com
// All service-to-service access uses managed identities + RBAC; no connection strings or keys.

targetScope = 'resourceGroup'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short prefix used in resource names.')
@maxLength(10)
param namePrefix string = 'orders'

@description('Publisher e-mail shown in API Management.')
param apimPublisherEmail string

@description('Publisher name shown in API Management.')
param apimPublisherName string = 'ShipSure QA demo'

@description('Entra ID tenant used by the APIM validate-jwt policy.')
param entraTenantId string = tenant().tenantId

var suffix = uniqueString(resourceGroup().id)
var storageName = toLower('st${namePrefix}${take(suffix, 8)}')

// Built-in role definition ids.
var roles = {
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
}

// ---------------------------------------------------------------------------
// Observability: Log Analytics + Application Insights (workspace based)
// ---------------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${namePrefix}-${suffix}'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${namePrefix}-${suffix}'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------------
// Storage: receipts (Blob), notifications (Queue), Functions deployment package
// ---------------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource receiptsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'receipts'
}

resource deploymentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobService
  name: 'deployments'
}

resource queueService 'Microsoft.Storage/storageAccounts/queueServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource notificationsQueue 'Microsoft.Storage/storageAccounts/queueServices/queues@2023-05-01' = {
  parent: queueService
  name: 'order-notifications'
}

// ---------------------------------------------------------------------------
// Service Bus: order-created queue (API -> Function)
// ---------------------------------------------------------------------------
resource serviceBus 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'sb-${namePrefix}-${suffix}'
  location: location
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
  }
}

resource orderCreatedQueue 'Microsoft.ServiceBus/namespaces/queues@2024-01-01' = {
  parent: serviceBus
  name: 'order-created'
  properties: {
    maxDeliveryCount: 5
    lockDuration: 'PT1M'
    defaultMessageTimeToLive: 'P7D'
    deadLetteringOnMessageExpiration: true
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT10M'
  }
}

// ---------------------------------------------------------------------------
// Key Vault (RBAC mode). Secrets named "OrdersApi--Section--Key" become "Section:Key".
// ---------------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${namePrefix}-${take(suffix, 10)}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ---------------------------------------------------------------------------
// Orders API: App Service (Linux, .NET 10)
// ---------------------------------------------------------------------------
resource apiPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-api-${namePrefix}-${suffix}'
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
  }
  properties: {
    reserved: true
  }
}

resource apiApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'app-${namePrefix}-api-${suffix}'
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: apiPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        {
          name: 'KeyVault__Uri'
          value: keyVault.properties.vaultUri
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'Messaging__Transport'
          value: 'ServiceBus'
        }
        {
          name: 'Messaging__ServiceBus__FullyQualifiedNamespace'
          value: '${serviceBus.name}.servicebus.windows.net'
        }
      ]
    }
  }
}

// ---------------------------------------------------------------------------
// Order processor: Azure Functions Flex Consumption (.NET 10 isolated worker)
// ---------------------------------------------------------------------------
resource functionPlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: 'plan-func-${namePrefix}-${suffix}'
  location: location
  kind: 'functionapp'
  sku: {
    tier: 'FlexConsumption'
    name: 'FC1'
  }
  properties: {
    reserved: true
  }
}

resource functionApp 'Microsoft.Web/sites@2023-12-01' = {
  name: 'func-${namePrefix}-${suffix}'
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: functionPlan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentsContainer.name}'
          authentication: {
            type: 'SystemAssignedIdentity'
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: 40
        instanceMemoryMB: 2048
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '10.0'
      }
    }
    siteConfig: {
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'ServiceBusConnection__fullyQualifiedNamespace'
          value: '${serviceBus.name}.servicebus.windows.net'
        }
        {
          name: 'Messaging__Storage__BlobServiceUri'
          value: storage.properties.primaryEndpoints.blob
        }
        {
          name: 'Messaging__Storage__QueueServiceUri'
          value: storage.properties.primaryEndpoints.queue
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
      ]
    }
  }
}

// ---------------------------------------------------------------------------
// RBAC: least privilege per workload
// ---------------------------------------------------------------------------
resource apiCanSend 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(orderCreatedQueue.id, apiApp.id, roles.serviceBusDataSender)
  scope: orderCreatedQueue
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataSender)
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource apiCanReadSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, apiApp.id, roles.keyVaultSecretsUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: apiApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionCanReceive 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(orderCreatedQueue.id, functionApp.id, roles.serviceBusDataReceiver)
  scope: orderCreatedQueue
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.serviceBusDataReceiver)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionBlobOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, roles.storageBlobDataOwner)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataOwner)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource functionQueueContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, functionApp.id, roles.storageQueueDataContributor)
  scope: storage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageQueueDataContributor)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------------
// API Management (Consumption): public front door with JWT, rate limit and header policies
// ---------------------------------------------------------------------------
resource apim 'Microsoft.ApiManagement/service@2023-05-01-preview' = {
  name: 'apim-${namePrefix}-${suffix}'
  location: location
  sku: {
    name: 'Consumption'
    capacity: 0
  }
  properties: {
    publisherEmail: apimPublisherEmail
    publisherName: apimPublisherName
  }
}

resource apimTenantId 'Microsoft.ApiManagement/service/namedValues@2023-05-01-preview' = {
  parent: apim
  name: 'entra-tenant-id'
  properties: {
    displayName: 'entra-tenant-id'
    value: entraTenantId
  }
}

resource ordersApi 'Microsoft.ApiManagement/service/apis@2023-05-01-preview' = {
  parent: apim
  name: 'orders-api'
  properties: {
    displayName: 'Orders API'
    path: 'orders'
    protocols: [
      'https'
    ]
    serviceUrl: 'https://${apiApp.properties.defaultHostName}'
    subscriptionRequired: true
  }
}

var apiOperations = [
  {
    name: 'get-any'
    method: 'GET'
  }
  {
    name: 'post-any'
    method: 'POST'
  }
  {
    name: 'patch-any'
    method: 'PATCH'
  }
]

resource ordersApiOperations 'Microsoft.ApiManagement/service/apis/operations@2023-05-01-preview' = [for op in apiOperations: {
  parent: ordersApi
  name: op.name
  properties: {
    displayName: '${op.method} /*'
    method: op.method
    urlTemplate: '/*'
  }
}]

resource ordersApiPolicy 'Microsoft.ApiManagement/service/apis/policies@2023-05-01-preview' = {
  parent: ordersApi
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('apim/orders-api-policy.xml')
  }
  dependsOn: [
    apimTenantId
  ]
}

output apiUrl string = 'https://${apiApp.properties.defaultHostName}'
output apimGatewayUrl string = apim.properties.gatewayUrl
output functionAppName string = functionApp.name
output keyVaultUri string = keyVault.properties.vaultUri
output serviceBusNamespace string = '${serviceBus.name}.servicebus.windows.net'
output receiptsContainerName string = receiptsContainer.name
output notificationsQueueName string = notificationsQueue.name
