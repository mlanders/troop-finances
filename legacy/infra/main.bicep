// Infrastructure for the BSA troop finance tracker: a Storage Account (Table + Blob), a Linux
// App Service running the Blazor Server app, an Azure SQL database reachable only from that app's
// virtual network subnet, and Azure Communication Services Email (for the parent portal's
// one-time login codes).
//
// NOT covered here (must be done manually -- see infra/DEPLOY.md):
//   - The Entra ID (Azure AD) app registration for the treasurer/admin side (a Microsoft Graph
//     object, not an Azure resource -- Bicep/ARM can't create it).
//   - DNS verification for the noreply@<your-domain> sender address (a customer-managed domain
//     needs TXT/SPF/CNAME records added at that domain's DNS provider -- Bicep can create the
//     Domain resource but can't touch DNS outside Azure).

@description('Base name used to derive resource names (e.g. "troop-finances"). Keep it short and lowercase.')
@minLength(3)
@maxLength(16)
param baseName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('App Service Plan SKU. Ignored when useExistingAppServicePlan is true.')
param appServicePlanSku string = 'B1'

@description('Set true to deploy the web app into an existing App Service Plan instead of creating a new one.')
param useExistingAppServicePlan bool = false

@description('Name of the existing App Service Plan. Required when useExistingAppServicePlan is true.')
param existingAppServicePlanName string = ''

@description('Resource group containing the existing App Service Plan, if different from the resource group this template is deployed into.')
param existingAppServicePlanResourceGroup string = resourceGroup().name

@description('Storage account name. Must be globally unique across all of Azure, 3-24 lowercase letters/digits only.')
@minLength(3)
@maxLength(24)
param storageAccountName string = toLower('${replace(baseName, '-', '')}stor')

@description('Backup storage account name (nightly snapshots, see Services/BackupService.cs). Globally unique, 3-24 lowercase letters/digits.')
@minLength(3)
@maxLength(24)
param backupStorageAccountName string = toLower('${replace(baseName, '-', '')}bak')

@description('Web app (site) name. Must be globally unique across all of Azure -- it becomes <name>.azurewebsites.net.')
@minLength(2)
@maxLength(60)
param webAppName string = '${baseName}-app'

@description('Custom domain for the parent-portal login-code sender address (e.g. "troop52.org"). Requires manual DNS verification after this deploy -- see infra/DEPLOY.md.')
param emailSenderDomain string

@description('Sender username used for the parent-portal login-code address.')
param senderUsername string = 'noreply'

@description('Set true only after the customer-managed email domain has been DNS-verified.')
param linkEmailDomain bool = false

@description('Azure SQL logical server name (holds the finance database). Globally unique, lowercase letters, digits and hyphens.')
@minLength(1)
@maxLength(63)
param sqlServerName string = '${baseName}-sql'

@description('Finance database name.')
param sqlDatabaseName string = 'troopfinances'

@description('Microsoft Entra administrator of the SQL server: sign-in name (UPN) of a person or group.')
param sqlEntraAdminLogin string

@description('Object id of that Entra administrator.')
param sqlEntraAdminObjectId string

@description('Address range of the virtual network the web app uses to reach the SQL server.')
param vnetAddressPrefix string = '10.41.0.0/24'

@description('Address range of the web app\'s integration subnet, inside vnetAddressPrefix. /26 is the size App Service recommends.')
param appSubnetAddressPrefix string = '10.41.0.0/26'

var appServicePlanName = '${baseName}-plan'
var vnetName = '${baseName}-vnet'
var appSubnetName = 'app-integration'
var communicationServiceName = '${baseName}-acs'
var emailServiceName = '${baseName}-email'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

// Soft delete on the live account: a deleted receipt or container is recoverable for 14 days,
// covering the gap between nightly backups.
resource storageBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 14
    }
    containerDeleteRetentionPolicy: {
      enabled: true
      days: 14
    }
  }
}

// ---- Backups: a separate, delete-locked account so nothing aimed at the live account (a bad
// script, an accidental resource deletion) can take the backups with it. The app writes one
// snapshot per night and itself keeps the newest 14 (count-based, so failed nights never age out
// the last good copies). The 60-day lifecycle rule below is only a backstop if the app is gone.
// Geo-redundant (GRS): Azure also copies the backups to the paired region, the only copy of the
// data outside this one -- the live storage and the SQL server's own backups are local. ----
resource backupStorageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: backupStorageAccountName
  location: location
  sku: {
    name: 'Standard_GRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Cool'
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
  }
}

resource backupBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: backupStorageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource backupContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: backupBlobService
  name: 'backups'
  properties: {
    publicAccess: 'None'
  }
}

resource backupLifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2023-01-01' = {
  parent: backupStorageAccount
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'backstop-delete-old-backups'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                'backups/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterModificationGreaterThan: 60
                }
              }
            }
          }
        }
      ]
    }
  }
}

resource backupStorageLock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'backup-storage-no-delete'
  scope: backupStorageAccount
  properties: {
    level: 'CanNotDelete'
    notes: 'Holds the finance app nightly backups. Remove this lock deliberately before deleting the account.'
  }
}

resource newAppServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = if (!useExistingAppServicePlan) {
  name: appServicePlanName
  location: location
  sku: {
    name: appServicePlanSku
  }
  kind: 'linux'
  properties: {
    reserved: true
  }
}

resource existingAppServicePlan 'Microsoft.Web/serverfarms@2023-12-01' existing = if (useExistingAppServicePlan) {
  name: existingAppServicePlanName
  scope: resourceGroup(existingAppServicePlanResourceGroup)
}

// Azure-managed-domain email service, used purely to host the customer-managed domain below.
resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: emailServiceName
  location: 'global'
  properties: {
    dataLocation: 'United States'
  }
}

resource emailDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: emailSenderDomain
  location: 'global'
  properties: {
    domainManagement: 'CustomerManaged'
  }
}

resource emailSenderUsernameResource 'Microsoft.Communication/emailServices/domains/senderUsernames@2023-04-01' = if (linkEmailDomain) {
  parent: emailDomain
  name: senderUsername
  properties: {
    displayName: 'Troop Finances'
    username: senderUsername
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: communicationServiceName
  location: 'global'
  properties: {
    dataLocation: 'United States'
    linkedDomains: linkEmailDomain ? [emailDomain.id] : []
  }
}

// ---- Private path from the web app to the SQL server. The web app's outbound traffic to Azure
// SQL leaves through this subnet (a Microsoft.Sql service endpoint), and the SQL server accepts
// only that subnet (sqlAllowAppSubnet below) instead of every Azure service in every tenant.
// Costs nothing. Only this web app is joined; the other sites on a shared plan are untouched. ----
resource vnet 'Microsoft.Network/virtualNetworks@2023-11-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnetAddressPrefix
      ]
    }
    subnets: [
      {
        name: appSubnetName
        properties: {
          addressPrefix: appSubnetAddressPrefix
          serviceEndpoints: [
            {
              service: 'Microsoft.Sql'
              locations: [
                location
              ]
            }
          ]
          delegations: [
            {
              name: 'app-service'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  // Joined to the subnet only once the SQL server accepts it, so the site never sits on a
  // subnet the database turns away.
  dependsOn: [
    sqlAllowAppSubnet
  ]
  properties: {
    serverFarmId: useExistingAppServicePlan ? existingAppServicePlan.id : newAppServicePlan.id
    virtualNetworkSubnetId: vnet.properties.subnets[0].id
    httpsOnly: true
    // App settings are intentionally managed separately so infrastructure redeployments do not
    // overwrite production secrets or manually configured authentication values.
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      ftpsState: 'Disabled'
    }
  }
}

// Lets the web app authenticate to storage via managed identity as an alternative to the
// connection-string app settings above (see Storage:TableServiceUri / Storage:BlobServiceUri in
// Program.cs). Harmless to leave granted even if unused.
// ---- Finance database: Azure SQL, Basic tier (5 DTU, 2 GB) -- always on, about $5/month.
// Microsoft Entra sign-in only: there is no SQL password anywhere. The web app connects as its own
// managed identity, which is made a database user with a one-time T-SQL script (infra/DEPLOY.md
// step 8) -- ARM can't create database users. Azure keeps automatic backups with 7 days of
// point-in-time restore on Basic, alongside the app's own nightly exports. ----
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlEntraAdminLogin
      sid: sqlEntraAdminObjectId
      tenantId: subscription().tenantId
      principalType: 'User'
    }
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    capacity: 5
  }
  properties: {
    maxSizeBytes: 2147483648
    requestedBackupStorageRedundancy: 'Local'
  }
}

// The only way in: the web app's integration subnet. There is deliberately no "allow Azure
// services" (0.0.0.0) firewall rule, which would admit connections from any Azure tenant.
// publicNetworkAccess stays Enabled above because virtual network rules need it; with no firewall
// rules, nothing else on the internet can connect. Sign-in is still required, Entra identities
// only. A developer machine needs its own IP rule only while running the one-time scripts
// (DEPLOY.md step 8), removed afterwards.
resource sqlAllowAppSubnet 'Microsoft.Sql/servers/virtualNetworkRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'allow-app-subnet'
  properties: {
    virtualNetworkSubnetId: vnet.properties.subnets[0].id
    ignoreMissingVnetServiceEndpoint: false
  }
}

resource sqlServerLock 'Microsoft.Authorization/locks@2020-05-01' = {
  name: 'finance-sql-no-delete'
  scope: sqlServer
  properties: {
    level: 'CanNotDelete'
    notes: 'Holds the finance app database. Remove this lock deliberately before deleting the server.'
  }
}

var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource storageTableRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, webApp.id, storageTableDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorRoleId)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageBlobRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, webApp.id, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: webApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output signInRedirectUri string = 'https://${webApp.properties.defaultHostName}/signin-oidc'
output storageAccountName string = storageAccount.name
output backupStorageAccountName string = backupStorageAccount.name
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDatabase.name
output financeDbConnectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabase.name};Authentication=Active Directory Managed Identity;Encrypt=True;'
output webAppPrincipalId string = webApp.identity.principalId
output appServicePlanUsed string = useExistingAppServicePlan ? existingAppServicePlanName : appServicePlanName
output emailDomainVerificationHint string = 'After this deploy, check the Domains resource (${emailServiceName}/${emailSenderDomain}) in the Azure Portal for the TXT/SPF/CNAME records to add at your DNS provider, then verify the domain there.'
