param resourceId string
param roleDefinitionId string
param principalId string


resource roleAssignment 'Microsoft.Authorization/roleAssignments@2021-04-01-preview' =  {
  name: guid(resourceId, principalId, resourceId)
  properties: {
    roleDefinitionId: resourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionID)
    principalId: principalId
  }
}
