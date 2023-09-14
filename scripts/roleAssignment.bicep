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