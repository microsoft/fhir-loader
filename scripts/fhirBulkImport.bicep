@description('Prefix for all resources')
param prefix string = 'bulk'

@description('Location for all resources.')
param location string = resourceGroup().location

@allowed([ 'FhirService', 'APIforFhir', 'FhirServer' ])
@description('Type of FHIR instance to integrate the loader with.')
param fhirType string = 'FhirService'

@description('Name of the FHIR Service to load resources into. Format is "workspace/fhirService".')
param fhirServiceName string = ''

@description('Name of the API for FHIR to load resources into.')
param apiForFhirName string = ''

@description('The full URL of the OSS FHIR Server to load resources.')
param fhirServerUrl string = ''

@allowed([ 'managedIdentity', 'servicePrincipal' ])
@description('Type of FHIR instance to integrate the loader with.')
param authenticationType string = 'managedIdentity'

@allowed([ 'B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P1v2', 'P2v2', 'P3v2', 'P1v3', 'P2v3', 'P3v3' ])
@description('Size of the app service to run loader function')
param appServiceSize string = 'B1'

@description('If not using MSI, client ID of the service account used to connect to the FHIR Server')
param serviceAccountClientId string = ''

@description('If not using MSI, client secret of the service account used to connect to the FHIR Server')
@secure()
param serviceAccountSecret string = ''

@description('Audience used for FHIR Server tokens. Leave blank to use the FHIR url which will work for default FHIR deployments.')
param fhirAudience string = ''

@description('Automatically create a role assignment for the function app to access the FHIR service.')
param createRoleAssignment bool = true

@description('The Bulk Loader function app needs to access the FHIR service. This is the role assignment ID to use.')
param fhirContributorRoleAssignmentId string = '5a1fc7df-4bf1-4951-a576-89034ee01acd'

@description('Transform transaction bundles to batch budles.')
param transformTransactionBundles bool = false

var repoUrl = 'https://github.com/microsoft/fhir-loader/'

var fhirUrl = fhirType == 'FhirService' ? 'https://${replace(fhirServiceName, '/', '-')}.fhir.azurehealthcareapis.com' : fhirType == 'APIforFhir' ? 'https://${apiForFhirName}.azurehealthcareapis.com' : fhirServerUrl

@description('Tenant ID where resources are deployed')
var tenantId = subscription().tenantId

@description('Tags for all Azure resources in the solution')
var appTags = {
  AppID: 'fhir-loader-function'
}

var uniqueResourceIdentifier = substring(uniqueString(resourceGroup().id, prefix), 0, 4)
var prefixNameClean = '${replace(prefix, '-', '')}${uniqueResourceIdentifier}'
var prefixNameCleanShort = length(prefixNameClean) > 16 ? substring(prefixNameClean, 0, 8) : prefixNameClean

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
@description('Storage Queue Service')
resource storageQueues 'Microsoft.Storage/storageAccounts/queueServices@2022-09-01' = {
  name: 'default'
  parent: storageAccount
 
}
@description('Storage Queue for Bundle Blob processing')
resource storageQueueBundle 'Microsoft.Storage/storageAccounts/queueServices/queues@2022-09-01' = {
  name: 'bundlequeue'
  parent: storageQueues
  properties: {
    metadata: {}
  }
}
@description('Storage Queue for NDJSON Blob processing')
resource storageQueueNDjson 'Microsoft.Storage/storageAccounts/queueServices/queues@2022-09-01' = {
  name: 'ndjsonqueue'
  parent: storageQueues
  properties: {
    metadata: {}
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
      ]
    }
  }

  dependsOn: [
    storageAccount
  ]

  tags: appTags

  resource config 'config' = {
    name: 'web'
    properties: {
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
    }
  }

  resource ftpPublishingPolicy 'basicPublishingCredentialsPolicies' = {
    name: 'ftp'
    // Location is needed regardless of the warning.
    #disable-next-line BCP187
    location: location
    properties: {
      allow: false
    }
  }

  resource scmPublishingPolicy 'basicPublishingCredentialsPolicies' = {
    name: 'scm'
    // Location is needed regardless of the warning.
    #disable-next-line BCP187
    location: location
    properties: {
      allow: false
    }
  }
}

var storageAccountConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storageAccount.listKeys().keys[0].value}'

resource fhirProxyAppSettings 'Microsoft.Web/sites/config@2021-03-01' = {
  name: 'appsettings'
  parent: functionApp
  properties: {
    AzureWebJobsStorage: storageAccountConnectionString
    FUNCTIONS_EXTENSION_VERSION: '~4'
    FUNCTIONS_WORKER_RUNTIME: 'dotnet'
    APPINSIGHTS_INSTRUMENTATIONKEY: appInsights.properties.InstrumentationKey
    APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
    SCM_DO_BUILD_DURING_DEPLOYMENT: 'true'
    'AzureWebJobs.ImportBundleBlobTrigger.Disabled': '1'
    AzureFunctionsJobHost__functionTimeout: '23:00:00'

    // Storage account to setup import from
    'FBI-STORAGEACCT': storageAccountConnectionString

    // URL for the FHIR endpoint
    'FS-URL': fhirUrl

    // Resource for the FHIR endpoint.
    'FS-RESOURCE': empty(fhirAudience) ? fhirUrl : fhirAudience

    // Tenant of FHIR Server
    'FS-TENANT-NAME': tenantId

    'FS-ISMSI': authenticationType == 'managedIdentity' ? 'true' : 'false'

    'FS-CLIENT-ID': authenticationType == 'servicePrincipal' ? serviceAccountClientId : ''

    'FS-SECRET': authenticationType == 'servicePrincipal' ? serviceAccountSecret : ''

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
    // Period to run the poision queue function.
    'FBI-POISONQUEUE-TIMER-CRON': '0 */2 * * * *'
  }
}

@description('Uses source control deploy if requested')
resource functionAppDeployment 'Microsoft.Web/sites/sourcecontrols@2021-03-01' = {
  name: 'web'
  parent: functionApp
  properties: {
    repoUrl: repoUrl
    branch: 'main'
    isManualIntegration: true
  }
}

@description('Subscription to ndjson container')
resource ndjsoncreated 'Microsoft.EventGrid/eventSubscriptions@2022-06-15' = {
  name: 'ndjsoncreated'
  scope: storageAccount
  properties: {
    destination: {
      endpointType: 'StorageQueue'
      properties: {
        resourceId: storageAccount.id
		queueName:'ndjsonqueue'
      }
    }
    filter: {
      advancedFilters: [
        {
          key: 'data.api'
          operatorType: 'StringIn'
          values: [ 'CopyBlob', 'PutBlob', 'PutBlockList', 'FlushWithClose' ]
        }
      ]
      subjectBeginsWith: '/blobServices/default/containers/ndjson'
      subjectEndsWith: '.ndjson'
    }
    eventDeliverySchema: 'EventGridSchema'
  }
  
}

@description('Subscription to bundle container')
resource bundlecreated 'Microsoft.EventGrid/eventSubscriptions@2022-06-15' = {
  name: 'bundlecreated'
  scope: storageAccount
  properties: {
    destination: {
      endpointType: 'StorageQueue'
      properties: {
         resourceId: storageAccount.id
		 queueName: 'bundlequeue'
      }
    }
    filter: {
      advancedFilters: [
        {
          key: 'data.api'
          operatorType: 'StringIn'
          values: [ 'CopyBlob', 'PutBlob', 'PutBlockList', 'FlushWithClose' ]
        }
      ]
      subjectBeginsWith: '/blobServices/default/containers/bundles'
      subjectEndsWith: '.json'
    }
    eventDeliverySchema: 'EventGridSchema'

  }

}
@description('Monitoring for Function App')
resource appInsights 'Microsoft.Insights/components@2020-02-02-preview' = {
  name: '${prefixNameCleanShort}-ai'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
  }
  tags: appTags
}
module roleAssignmentFhirService './roleAssignment.bicep' = if (createRoleAssignment == true) {
  name: 'role-assign-fhir'
  scope: resourceGroup('ahdschallenge')
  params: {
    fhirUrl: fhirUrl
    fhirType: fhirType
	fhirContributorRoleAssignmentId: fhirContributorRoleAssignmentId
    principalId: functionApp.identity.principalId
  }
}
