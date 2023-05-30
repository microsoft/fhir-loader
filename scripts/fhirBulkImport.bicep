@description('Prefix for all resources')
param prefix string

@description('Location for all resources.')
param location string = resourceGroup().location

@description('The full URL of the FHIR Service to import data')
param fhirUrl string

@description('Resource for connection to the FHIR Server. Leave blank to use the FHIR url.')
param fhirResource string = ''

@allowed(['B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P1v2', 'P2v2', 'P3v2', 'P1v3', 'P2v3', 'P3v3'])
@description('Size of the app service to run loader function')
param appServiceSize string = 'B1'

@description('Tenant ID where resources are deployed')
var tenantId  = subscription().tenantId

@description('Tags for all Azure resources in the solution')
var appTags = {
    AppID: 'fhir-loader-function'
}

@description('Name of the Log Analytics workspace. Leave blank to create a new one. (Needed for App Insights)')
param logAnalyticsName string

var prefixNameClean = replace(prefix, '-', '')
var prefixNameCleanShort = length(prefixNameClean) > 16 ? substring(prefixNameClean, 0, 16) : prefixNameClean

@description('Name of the NDJSON import function. Used for setting up the Storage to Event Grid subscription')
var importNDJsonFunctionName='ImportNDJSON'

@description('Name of the bundle import function. Used for setting up the Storage to Event Grid subscription')
var importBundleFunctionName='ImportBundleEventGrid'

@description('URL to the FHIR Loader repo for git integration')
var loaderRepoUrl = 'https://github.com/microsoft/fhir-loader'

@description('Branch of the FHIR Loader repo for git integration')
param loaderRepoBranch string = 'main'

@description('Transform transaction bundles to batch budles.')
param transformTransactionBundles bool = false

@allowed([
  'FhirService'
  'APIforFhir'
  'none'
])
@description('What type of FHIR Server to setup a managed identity connection for.')
param setManagedIdentityForFhir string = 'FhirService'

@description('Storage account used for loading files')
resource storageAccount 'Microsoft.Storage/storageAccounts@2021-08-01' = {
  name: '${prefixNameCleanShort}stor'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'

  resource service 'blobServices' = {
    name: 'default'

    resource bundles 'containers' = {
      name: 'bundles'
    }

    resource ndjson 'containers' = {
      name: 'ndjson'
    }

    resource zip 'containers' = {
      name: 'zip'
    }

    resource export 'containers' = {
      name: 'export'
    }

    resource exportTrigger 'containers' = {
      name: 'export-trigger'
    }
  }
}

@description('App Service used to run Azure Function')
resource hostingPlan 'Microsoft.Web/serverfarms@2021-03-01' = {
  name: '${prefixNameCleanShort}-app'
  location: location
  kind: 'functionapp'
  sku: {
    name: appServiceSize
  }

  properties: {
    targetWorkerCount: 2
  }
  tags: appTags
}

@description('Azure Function used to run toolkit compute')
resource functionApp 'Microsoft.Web/sites@2021-03-01' = {
  name: '${prefixNameCleanShort}-func'
  location: location
  kind: 'functionapp'

  identity: {
    type: 'SystemAssigned'
  }

  properties: {
    httpsOnly: true
    enabled: true
    serverFarmId: hostingPlan.id
    clientAffinityEnabled: false
    siteConfig: {
      alwaysOn: true
      appSettings: [
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'Project'
          value: 'src/FhirLoader.BulkFunction/FhirLoader.BulkFunction.csproj'
        }
      ]
    }
  }

  dependsOn: [
    storageAccount
  ]

  tags: appTags
}

var storageAccountConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'

resource fhirProxyAppSettings 'Microsoft.Web/sites/config@2020-12-01' = {
  name: 'appsettings'
  parent: functionApp
  properties: {
    AzureWebJobsStorage: storageAccountConnectionString
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet'
    Project: 'src/FhirLoader.BulkFunction'
    APPINSIGHTS_INSTRUMENTATIONKEY: appInsights.properties.InstrumentationKey
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    SCM_DO_BUILD_DURING_DEPLOYMENT: 'true'
    'AzureWebJobs.ImportBundleBlobTrigger.Disabled': '1'
    
    // Storage account to setup import from
    'FBI-STORAGEACCT': storageAccountConnectionString

    // URL for the FHIR endpoint
    'FS-URL': fhirUrl

    // Resource for the FHIR endpoint.
    'FS-RESOURCE': fhirResource

    // Tenant of FHIR Server
    'FS-TENANT-NAME': tenantId

    // When loading bundles, convert transaction to batch bundles. Transform UUIDs and resolve ifNoneExist
    TRANSFORMBUNDLES: '${transformTransactionBundles}'

    // ADVANCED
    // Max number of resources in a bundle
    'FBI-MAXBUNDLESIZE': '500'
    // When loading NDJSON, how many resources to put in a single bundle
    'FBI-MAXRESOURCESPERBUNDLE': '500'
    // Max HTTP retries on the FHIR Server
    'FBI-POLLY-MAXRETRIES': '3'
    // Retry delay for FHIR Server requests
    'FBI-POLLY-RETRYMS': '500'
    // ResponseDrainTimeout
    'FBI-POOLEDCON-RESPONSEDRAINSECS': '60'
    // PooledConnectionLifetime
    'FBI-POOLEDCON-LIFETIME': '5'
    // PooledConnectionIdleTimeout
    'FBI-POOLEDCON-IDLETO': '2'
    // MaxConnectionsPerServer
    'FBI-POOLEDCON-MAXCONNECTIONS': '20'
    // Max file size to load. -1 disables this.
    'FBI-MAXFILESIZEMB': '-1'
    // Max number of concurrent exports
    'FBI-MAXEXPORTS': '-1'
    // How long to leave exports on the storage account
    'FBI-EXPORTPURGEAFTERDAYS': '30'
  }

  dependsOn: [
    sourcecontrol
  ]
}

@description('Git integration for the function app code')
resource sourcecontrol 'Microsoft.Web/sites/sourcecontrols@2022-03-01' = {
  parent: functionApp
  name: 'web'
  properties: {
    repoUrl: loaderRepoUrl
    branch: loaderRepoBranch
    isManualIntegration: true
  }
}

@description('Subscription to ndjson container')
resource ndjsoncreated 'Microsoft.EventGrid/eventSubscriptions@2022-06-15' = {
  name: 'ndjsoncreated'
  scope: storageAccount
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: '${functionApp.id}/functions/${importNDJsonFunctionName}'
      }
    }
    filter: {
      advancedFilters: [
        {
          key: 'data.api'
          operatorType: 'StringIn'
          values: ['CopyBlob', 'PutBlob', 'PutBlockList', 'FlushWithClose']
        }
      ]
      subjectBeginsWith: '/blobServices/default/containers/ndjson'
      subjectEndsWith: '.ndjson'
    }
    eventDeliverySchema: 'EventGridSchema'
  }

  dependsOn: [ sourcecontrol ]
}

@description('Subscription to bundle container')
resource bundlecreated 'Microsoft.EventGrid/eventSubscriptions@2022-06-15' = {
  name: 'bundlecreated'
  scope: storageAccount
  properties: {
    destination: {
      endpointType: 'AzureFunction'
      properties: {
        resourceId: '${functionApp.id}/functions/${importBundleFunctionName}'
        maxEventsPerBatch: 10
      }
    }
    filter: {
      advancedFilters: [
        {
          key: 'data.api'
          operatorType: 'StringIn'
          values: ['CopyBlob', 'PutBlob', 'PutBlockList', 'FlushWithClose']
        }
      ]
      subjectBeginsWith: '/blobServices/default/containers/bundles'
      subjectEndsWith: '.json'
    }
    eventDeliverySchema: 'EventGridSchema'
    
  }

  dependsOn: [ sourcecontrol ]
}

@description('Logging workspace for FHIR Loader - use new if specified')
resource logAnalyticsWorkspaceNew 'Microsoft.OperationalInsights/workspaces@2020-03-01-preview' = if (length(logAnalyticsName) == 0) {
  name: '${prefixNameCleanShort}-la'
  location: location
  properties: {
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
  }
  tags: appTags
}

var logAnalyticsWorkspaceId = resourceId('Microsoft.OperationalInsights/workspaces', (length(logAnalyticsName) == 0) ? '${prefixNameCleanShort}-la' : logAnalyticsName)

@description('Monitoring for Function App')
resource appInsights 'Microsoft.Insights/components@2020-02-02-preview' = {
  name: '${prefixNameCleanShort}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspaceId
  }
  tags: appTags

  dependsOn: [logAnalyticsWorkspaceNew]
}

var fhirUrlClean = replace(split(fhirUrl, '.')[0], 'https://', '')
var fhirUrlCleanSplit = split(fhirUrlClean, '-')


resource fhirService 'Microsoft.HealthcareApis/workspaces/fhirservices@2021-06-01-preview' existing = if (setManagedIdentityForFhir == 'FhirService') {
  #disable-next-line prefer-interpolation
  name: concat(fhirUrlCleanSplit[0], '/', join(skip(fhirUrlCleanSplit, 1), '-'))
}

resource apiForFhir 'Microsoft.HealthcareApis/services@2021-11-01' existing = if (setManagedIdentityForFhir == 'APIforFhir') {
  name: fhirUrlClean
}

@description('Setup access between FHIR and the deployment script managed identity')
module functionFhirServiceRoleAssignment './roleAssignment.bicep'= if (setManagedIdentityForFhir == 'FhirService') {
  name: 'functionFhirServiceRoleAssignment'
  params: {
    resourceId: fhirService.id
    // FHIR Contributor
    roleId: '5a1fc7df-4bf1-4951-a576-89034ee01acd'
    principalId: functionApp.identity.principalId
  }
}

@description('Setup access between FHIR and the deployment script managed identity')
module functionApiForFhirRoleAssignment './roleAssignment.bicep'= if (setManagedIdentityForFhir == 'APIforFhir') {
  name: 'functionApiForFhirRoleAssignment'
  params: {
    resourceId: apiForFhir.id
    // FHIR Contributor
    roleId: '5a1fc7df-4bf1-4951-a576-89034ee01acd'
    principalId: functionApp.identity.principalId
  }
}

// Connection string
/*@description('Setup access between FHIR and the deployment script managed identity')
module functionStorageRoleAssignment './roleAssignment.bicep'= {
  name: 'fhirServiceRoleAssignment'
  params: {
    resourceId: storageAccount.id
    // Storage blob data contributor
    roleId: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    principalId: functionApp.identity.principalId
  }
}*/
