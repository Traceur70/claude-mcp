// Deploiement au niveau SOUSCRIPTION : cree le groupe de ressources puis les ressources.
//
// Commande :
//   az deployment sub create \
//     --name claude-mcp-infra \
//     --location westeurope \
//     --template-file infra/main.bicep \
//     --parameters infra/main.bicepparam

targetScope = 'subscription'

@description('Region Azure des ressources.')
param location string = 'westeurope'

@description('Nom du groupe de ressources a creer.')
param resourceGroupName string = 'claude-mcp'

@description('Nom de la Web App (unique sur tout Azure). Determine l URL et l URI de redirection.')
param webAppName string = 'claude-mcp-nadda'

@description('Nom de l App Service Plan.')
param appServicePlanName string = 'claude-mcp-plan'

@description('Tenant ID Entra. "consumers" pour un OneDrive personnel.')
param tenantId string = 'consumers'

@description('Tags appliques a toutes les ressources.')
param tags object = {
  project: 'claude-mcp'
  workload: 'onedrive-mcp'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module resources 'resources.bicep' = {
  scope: rg
  name: 'webapp-resources'
  params: {
    location: location
    webAppName: webAppName
    appServicePlanName: appServicePlanName
    tenantId: tenantId
    tags: tags
  }
}

@description('URL publique de la Web App.')
output webAppUrl string = resources.outputs.webAppUrl

@description('URI de redirection a enregistrer dans l app registration Entra.')
output redirectUri string = resources.outputs.redirectUri

@description('Nom du groupe de ressources cree.')
output resourceGroupName string = rg.name
