// One-click setup on Azure (the "Deploy to Azure" button in README.md, or `azd up`): the app as an
// Azure Container App from the published image, a PostgreSQL flexible server, a storage account for
// receipts and a second one for nightly backups, and a small scheduled job that starts the backup.
//
// Simpler than the App Service deployment in infra/main.bicep (no virtual network, no SQL
// managed-identity script, no code upload): everything is created and connected here. Admin sign-in
// (Google or Microsoft) and an SMTP email service still have to be set up first; see infra/SETUP.md.
//
// The app scales to zero when nobody is using it, so it costs little beyond the database:
// about $13/month for PostgreSQL (Burstable B1ms) + $4 for its 32 GB disk, and cents for the rest.
//
// After editing, regenerate the button's template:
//   az bicep build --file infra/container-apps/main.bicep --outfile infra/container-apps/azuredeploy.json

@description('Short name the resource names start with. Lowercase letters, digits and hyphens.')
@minLength(3)
@maxLength(20)
param baseName string = 'troop-finances'

@description('Azure region for everything.')
param location string = resourceGroup().location

@description('The app\'s container image. The default is published from this repository\'s main branch.')
param containerImage string = 'ghcr.io/sacparker09/troop-finances:latest'

// ---- Your troop (infra/HOSTING.md "Your troop's settings") ----

@description('The troop\'s name, shown on every page, email and PDF, e.g. "Troop 52".')
param troopName string

@description('Where families send questions, usually the treasurer, e.g. treasurer@troop52.org.')
param troopContactEmail string

@description('The troop\'s time zone, e.g. America/New_York, America/Chicago, America/Denver, America/Phoenix, America/Los_Angeles.')
param troopTimeZone string = 'America/Chicago'

@description('Optional. The site\'s own address once it has a domain, e.g. https://finance.troop52.org/. Empty uses the Container App\'s address.')
param siteUrl string = ''

// ---- Who runs it ----

@description('The system admin\'s email: the person looking after the site.')
param globalAdminEmail string

@description('The first Editor\'s email (usually the treasurer). They can add everyone else.')
param editorEmail string

// ---- Admin sign-in: Google, Microsoft, or both ----

@description('Google sign-in client id (infra/SETUP.md step 1). Leave empty to use only Microsoft sign-in.')
param googleClientId string = ''

@secure()
@description('Google sign-in client secret.')
param googleClientSecret string = ''

@description('Microsoft sign-in app (client) id (infra/DEPLOY.md step 2). Leave empty to use only Google sign-in.')
param microsoftClientId string = ''

@secure()
@description('Microsoft sign-in client secret.')
param microsoftClientSecret string = ''

// ---- Email: guardians' sign-in codes and notices ----

@description('The address emails come from, e.g. noreply@troop52.org. Its domain must be verified with the email service.')
param emailSenderAddress string

@description('The email service\'s SMTP host, e.g. smtp.resend.com, smtp-relay.brevo.com.')
param smtpHost string

@description('SMTP port: 587 (usual) or 465.')
param smtpPort int = 587

@description('SMTP username (Resend: resend).')
param smtpUsername string

@secure()
@description('SMTP password or API key.')
param smtpPassword string

// ---- Generated secrets. Leave these as they are. ----

@secure()
@minLength(32)
@description('Leave as is: generated once. Never change it afterwards (it unlinks every guardian\'s authenticator app), so change settings in the portal rather than deploying this template again.')
param loginCodePepper string = newGuid()

@secure()
@minLength(12)
@description('Leave as is: the database password, generated once.')
param databasePassword string = 'Tf-${newGuid()}'

@secure()
@minLength(32)
@description('Leave as is: lets the nightly job start the backup.')
param backupTriggerKey string = newGuid()

var suffix = uniqueString(resourceGroup().id, baseName)
var appName = take('${baseName}-app', 32)
var databaseName = 'troop_finances'
var databaseUser = 'troop'

// ---- Logs: the app's output, for Container App -> Log stream and Logs ----

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${baseName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// ---- Database: PostgreSQL flexible server, the smallest size. Azure keeps its own backups for 7
// days, alongside the app's nightly exports. Password sign-in over TLS; reachable from Azure
// services only (the app's outgoing address isn't fixed). ----

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: '${baseName}-db-${suffix}'
  location: location
  sku: {
    name: 'Standard_B1ms'
    tier: 'Burstable'
  }
  properties: {
    version: '16'
    administratorLogin: databaseUser
    administratorLoginPassword: databasePassword
    storage: {
      storageSizeGB: 32
      autoGrow: 'Enabled'
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
  }
}

resource postgresDatabase 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource postgresAllowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2024-08-01' = {
  parent: postgres
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ---- Receipts, and a separate account for backups so one mistake can't take both ----

resource receipts 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'tf${suffix}'
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

resource receiptsBlobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: receipts
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

resource backups 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'tfbak${suffix}'
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

resource backupsBlobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: backups
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

// ---- The app ----

resource appEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${baseName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

var receiptsConnection = 'DefaultEndpointsProtocol=https;AccountName=${receipts.name};AccountKey=${receipts.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var backupsConnection = 'DefaultEndpointsProtocol=https;AccountName=${backups.name};AccountKey=${backups.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'
var databaseConnection = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${databaseName};Username=${databaseUser};Password=${databasePassword};SSL Mode=Require'

// Secrets the app reads, and the settings that point at them. Empty optional ones are left out.
var secrets = concat([
  { name: 'finance-db', value: databaseConnection }
  { name: 'receipts-storage', value: receiptsConnection }
  { name: 'backup-storage', value: backupsConnection }
  { name: 'login-code-pepper', value: loginCodePepper }
  { name: 'backup-trigger-key', value: backupTriggerKey }
  { name: 'smtp-password', value: smtpPassword }
], empty(googleClientSecret) ? [] : [
  { name: 'google-client-secret', value: googleClientSecret }
], empty(microsoftClientSecret) ? [] : [
  { name: 'microsoft-client-secret', value: microsoftClientSecret }
])

var settings = concat([
  { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
  { name: 'Troop__Name', value: troopName }
  { name: 'Troop__ContactEmail', value: troopContactEmail }
  { name: 'Troop__TimeZone', value: troopTimeZone }
  { name: 'Auth__GlobalAdmins__0', value: globalAdminEmail }
  { name: 'Auth__BootstrapEditors__0', value: editorEmail }
  { name: 'Auth__LoginCodePepper', secretRef: 'login-code-pepper' }
  { name: 'Database__Provider', value: 'Postgres' }
  { name: 'ConnectionStrings__FinanceDb', secretRef: 'finance-db' }
  { name: 'ConnectionStrings__AzureStorage', secretRef: 'receipts-storage' }
  { name: 'ConnectionStrings__BackupStorage', secretRef: 'backup-storage' }
  { name: 'Backup__Schedule', value: 'External' }
  { name: 'Backup__TriggerKey', secretRef: 'backup-trigger-key' }
  { name: 'Email__Provider', value: 'Smtp' }
  { name: 'Email__SenderAddress', value: emailSenderAddress }
  { name: 'Email__Smtp__Host', value: smtpHost }
  { name: 'Email__Smtp__Port', value: string(smtpPort) }
  { name: 'Email__Smtp__Username', value: smtpUsername }
  { name: 'Email__Smtp__Password', secretRef: 'smtp-password' }
], empty(siteUrl) ? [] : [
  { name: 'Troop__SiteUrl', value: siteUrl }
], empty(googleClientId) ? [] : [
  { name: 'Authentication__Google__ClientId', value: googleClientId }
  { name: 'Authentication__Google__ClientSecret', secretRef: 'google-client-secret' }
], empty(microsoftClientId) ? [] : [
  { name: 'AzureAd__ClientId', value: microsoftClientId }
  { name: 'AzureAd__ClientSecret', secretRef: 'microsoft-client-secret' }
])

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  // azd: this is the app `azd` shows the address of.
  tags: {
    'azd-service-name': 'web'
  }
  dependsOn: [
    postgresDatabase
    postgresAllowAzure
    receiptsBlobs
    backupsBlobs
  ]
  properties: {
    managedEnvironmentId: appEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: 'app'
          image: containerImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: settings
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              failureThreshold: 30
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/healthz'
                port: 8080
              }
              periodSeconds: 30
            }
          ]
        }
      ]
      // Stopped when nobody is using it (an open page counts as use), and never more than one
      // copy: each open page is connected to the copy that drew it (infra/HOSTING.md).
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

// ---- The nightly backup: the app may be stopped at night, so a small job wakes it and asks for the
// backup (infra/HOSTING.md "Letting the app sleep"). 10:15 UTC is after 2 AM across the mainland US;
// a repeat the same day does nothing. ----

resource backupJob 'Microsoft.App/jobs@2024-03-01' = {
  name: take('${baseName}-backup', 32)
  location: location
  properties: {
    environmentId: appEnvironment.id
    configuration: {
      triggerType: 'Schedule'
      scheduleTriggerConfig: {
        cronExpression: '15 10 * * *'
        parallelism: 1
        replicaCompletionCount: 1
      }
      replicaTimeout: 1800
      replicaRetryLimit: 1
      secrets: [
        { name: 'backup-trigger-key', value: backupTriggerKey }
      ]
    }
    template: {
      containers: [
        {
          name: 'start-backup'
          image: 'docker.io/curlimages/curl:8.11.1'
          command: [
            '/bin/sh'
            '-c'
            'curl --fail-with-body --silent --show-error --retry 3 --retry-all-errors --retry-delay 30 --max-time 1500 -X POST -H "Authorization: Bearer $KEY" "$URL"'
          ]
          env: [
            { name: 'KEY', secretRef: 'backup-trigger-key' }
            { name: 'URL', value: 'https://${app.properties.configuration.ingress.fqdn}/internal/backup' }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
}

output appUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
output googleRedirectUri string = 'https://${app.properties.configuration.ingress.fqdn}/signin-google'
output microsoftRedirectUri string = 'https://${app.properties.configuration.ingress.fqdn}/signin-oidc'
output databaseServer string = postgres.properties.fullyQualifiedDomainName
