using 'main.bicep'

param location = 'westeurope'
param resourceGroupName = 'claude-devbox'
param vmName = 'claude-devbox'
param vmSize = 'Standard_D4s_v5'
param adminUsername = 'azureuser'

// La cle publique SSH est passee en ligne de commande par deploy.sh (--parameters adminPublicKey=...)
// pour ne pas committer de cle dans le repo. Tu peux aussi la coller ici si tu preferes.
param adminPublicKey = ''

// Restreins a ton IP en prod, ex : '203.0.113.4/32'. '*' = ouvert a tous (pratique mais moins sur).
param allowedSshSource = '*'

param osDiskSizeGB = 128

// Optionnel : prefixe DNS unique dans la region -> donne un FQDN claude-devbox-xxx.westeurope.cloudapp.azure.com
param dnsLabelPrefix = ''
