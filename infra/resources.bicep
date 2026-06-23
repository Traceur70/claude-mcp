// Ressources deployees dans le groupe de ressources 'claude-mcp' :
//   - App Service Plan B1 (Linux)
//   - Web App .NET 8 (Linux), HTTPS only, TLS 1.2, FTPS desactive
//   - App Settings pre-crees VIDES (a remplir dans le portail)

@description('Region Azure.')
param location string

@description('Nom de la Web App.')
param webAppName string

@description('Nom de l App Service Plan.')
param appServicePlanName string

@description('Tenant ID Entra (valeur par defaut des App Settings).')
param tenantId string = 'consumers'

@description('Tags.')
param tags object = {}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
    capacity: 1
  }
  properties: {
    reserved: true // requis pour Linux
  }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  tags: tags
  kind: 'app,linux'
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true
      // App Settings crees VIDES : vous les renseignez ensuite dans le portail Azure.
      appSettings: [
        {
          name: 'AzureAd__ClientId'
          value: ''
        }
        {
          name: 'AzureAd__ClientSecret'
          value: ''
        }
        {
          name: 'AzureAd__TenantId'
          value: tenantId
        }
        {
          name: 'Mcp__ApiKey'
          value: ''
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
      ]
    }
  }
}

@description('URL publique de la Web App.')
output webAppUrl string = 'https://${web.properties.defaultHostName}'

@description('URI de redirection OAuth a enregistrer dans Entra.')
output redirectUri string = 'https://${web.properties.defaultHostName}/signin-oidc'
