param resourceId string
param roleDefinitionId string
param principalId string
param principalType string = 'ServicePrincipal'


resource roleAssignment 'Microsoft.Authorization/roleAssignments@2020-04-01-preview' =  {
  name: guid(resourceId, principalId, roleDefinitionId)
  properties: {
    roleDefinitionId: roleDefinitionId
    principalId: principalId
    principalType: principalType
  }
}
