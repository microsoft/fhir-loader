param fhirUrl string
param fhirType string
param fhirContributorRoleAssignmentId string
param principalId string
param principalType string = 'ServicePrincipal'
param subscriptionId string = subscription().subscriptionId

var fhirUrlClean = replace(split(fhirUrl, '.')[0], 'https://', '')
var fhirUrlCleanSplit = split(fhirUrlClean, '-')

resource fhirService 'Microsoft.HealthcareApis/workspaces/fhirservices@2021-06-01-preview' existing = if (fhirType == 'FhirService') {
  #disable-next-line prefer-interpolation
  name: concat(fhirUrlCleanSplit[0], '/', join(skip(fhirUrlCleanSplit, 1), '-'))
  
}
resource apiForFhir 'Microsoft.HealthcareApis/services@2021-11-01' existing = if (fhirType == 'APIforFhir') {
  name: fhirUrlClean
  
}
resource roleAssignmentFhirService 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' =  if (fhirType == 'FhirService') {
  name: guid(principalId,fhirService.id,fhirContributorRoleAssignmentId)
  scope: fhirService
  properties: {
    roleDefinitionId: '/subscriptions/${subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${fhirContributorRoleAssignmentId}'
    principalId: principalId
    principalType: principalType
  }
}
resource roleAssignmenApiforFhir 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' =  if (fhirType == 'APIforFhir') {
  name: guid(principalId,apiForFhir.id,fhirContributorRoleAssignmentId)
  scope: apiForFhir
  properties: {
    roleDefinitionId: '/subscriptions/${subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/${fhirContributorRoleAssignmentId}'
    principalId: principalId
    principalType: principalType
  }
}

//StorageBlobDataOwner
resource roleAssignment1 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(principalId,apiForFhir.id,'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b')
    principalId: principalId
  }
}

//StorageAccountContributor
resource roleAssignment2 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(principalId,apiForFhir.id,'17d1049b-9a84-46fb-8f53-869881c3d3ab')
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '17d1049b-9a84-46fb-8f53-869881c3d3ab')
    principalId: principalId
  }
}

//StorageQueueDataContributor
resource roleAssignment3 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(principalId,apiForFhir.id,'974c5e8b-45b9-4653-ba55-5f855dd0fb88')
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', '974c5e8b-45b9-4653-ba55-5f855dd0fb88')
    principalId: principalId
  }
}
