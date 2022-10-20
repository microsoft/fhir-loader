@description('Prefix for all resources')
param prefix string

@description('Location for all resources.')
param location string = resourceGroup().location

@description('The full URL of the FHIR Service to import data')
param fhirUrl string

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
    name: 'B1'
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
          value: 'FHIRBulkImport'
        }
      ]
    }
  }

  dependsOn: [
    storageAccount
  ]

  tags: appTags
}

resource fhirProxyAppSettings 'Microsoft.Web/sites/config@2020-12-01' = {
  name: 'appsettings'
  parent: functionApp
  properties: {
    AzureWebJobsStorage: 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet'
    Project: 'FHIRBulkImport'
    APPINSIGHTS_INSTRUMENTATIONKEY: appInsights.properties.InstrumentationKey
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    SCM_DO_BUILD_DURING_DEPLOYMENT: 'true'
    'AzureWebJobs.ImportBundleBlobTrigger.Disabled': '1'
    'FBI-STORAGEACCT': storageAccount.name
    TRANSFORMBUNDLES: 'true'
    'FBI-POOLEDCON-MAXCONNECTIONS': '20'
    'FS-URL': fhirUrl
    'FS-TENANT-NAME': tenantId
    'FS-RESOURCE': fhirUrl
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
