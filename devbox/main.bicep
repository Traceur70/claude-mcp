// Deploiement au niveau SOUSCRIPTION : cree le groupe de ressources puis la VM de dev.
//
// But : une VM Linux Ubuntu 24.04 hebergeant Docker + Node/npm + Angular CLI (>21) + .NET 10,
//       pour faire du remote dev (avec Claude Code en --remote/--teleport, VS Code Remote-SSH, etc.).
//
// Commande :
//   az deployment sub create \
//     --name claude-devbox-infra \
//     --location westeurope \
//     --template-file devbox/main.bicep \
//     --parameters devbox/main.bicepparam \
//     --parameters adminPublicKey="$(cat ~/.ssh/id_ed25519.pub)"

targetScope = 'subscription'

@description('Region Azure des ressources.')
param location string = 'westeurope'

@description('Nom du groupe de ressources a creer.')
param resourceGroupName string = 'claude-devbox'

@description('Nom de la VM (sert aussi de prefixe aux ressources reseau).')
param vmName string = 'claude-devbox'

@description('Taille de la VM. Levier de scalabilite verticale : on redimensionne sans recreer. Ex : Standard_D2s_v5, Standard_D4s_v5, Standard_D8s_v5.')
param vmSize string = 'Standard_D4s_v5'

@description('Nom de l utilisateur admin Linux.')
param adminUsername string = 'azureuser'

@description('Cle publique SSH (contenu de ~/.ssh/id_ed25519.pub). OBLIGATOIRE.')
@secure()
param adminPublicKey string

@description('Plage source autorisee pour SSH (CIDR). Mets ton IP /32 plutot que * en prod. Ex : 203.0.113.4/32.')
param allowedSshSource string = '*'

@description('Taille du disque OS en Go (images Docker, build .NET/Angular).')
param osDiskSizeGB int = 128

@description('Prefixe DNS public optionnel (doit etre unique dans la region). Vide = pas de FQDN.')
param dnsLabelPrefix string = ''

@description('Tags appliques a toutes les ressources.')
param tags object = {
  project: 'claude-mcp'
  workload: 'remote-devbox'
}

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

module vm 'vm.bicep' = {
  scope: rg
  name: 'devbox-resources'
  params: {
    location: location
    vmName: vmName
    vmSize: vmSize
    adminUsername: adminUsername
    adminPublicKey: adminPublicKey
    allowedSshSource: allowedSshSource
    osDiskSizeGB: osDiskSizeGB
    dnsLabelPrefix: dnsLabelPrefix
    tags: tags
  }
}

@description('IP publique de la VM.')
output publicIp string = vm.outputs.publicIp

@description('FQDN public (si dnsLabelPrefix fourni).')
output fqdn string = vm.outputs.fqdn

@description('Commande SSH prete a copier.')
output sshCommand string = vm.outputs.sshCommand

@description('Nom du groupe de ressources cree.')
output resourceGroupName string = rg.name
