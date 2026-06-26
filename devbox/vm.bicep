// Ressources de la VM de dev (deployees dans le groupe de ressources cible) :
//   - Reseau virtuel + sous-reseau
//   - NSG (SSH uniquement ; expose tes ports de dev via tunnels SSH)
//   - IP publique Standard (statique)
//   - Interface reseau
//   - VM Linux Ubuntu 24.04 LTS, auth par cle SSH
//   - cloud-init : installe Docker, Node 22/npm, Angular CLI (>21) et .NET 10 au 1er boot

@description('Region Azure.')
param location string

@description('Nom de la VM.')
param vmName string

@description('Taille de la VM.')
param vmSize string

@description('Utilisateur admin Linux.')
param adminUsername string

@description('Cle publique SSH.')
@secure()
param adminPublicKey string

@description('Plage source autorisee pour SSH (CIDR ou *).')
param allowedSshSource string = '*'

@description('Taille du disque OS en Go.')
param osDiskSizeGB int = 128

@description('Prefixe DNS public optionnel.')
param dnsLabelPrefix string = ''

@description('Tags.')
param tags object = {}

// ----------------------------------------------------------------------------
// cloud-init : provisionne tout l'outillage au premier demarrage.
// Le marqueur /var/lib/devbox-ready permet de savoir quand l'install est finie.
// ----------------------------------------------------------------------------
var cloudInitTemplate = '''
#cloud-config
package_update: true
packages:
  - ca-certificates
  - curl
  - git
  - unzip
  - apt-transport-https
write_files:
  - path: /etc/profile.d/dotnet.sh
    permissions: '0644'
    content: |
      export DOTNET_ROOT=/usr/share/dotnet
      export PATH=$PATH:/usr/share/dotnet:$HOME/.dotnet/tools
runcmd:
  - set -eux
  # --- Docker (depot officiel) ---
  - curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
  - sh /tmp/get-docker.sh
  - usermod -aG docker __ADMIN_USER__
  - systemctl enable --now docker
  # --- Node.js 22 + npm ---
  - curl -fsSL https://deb.nodesource.com/setup_22.x | bash -
  - DEBIAN_FRONTEND=noninteractive apt-get install -y nodejs
  # --- Angular CLI (derniere version, >21) ---
  - npm install -g @angular/cli@latest
  # --- .NET 10 SDK ---
  - curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  - bash /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet
  - ln -sf /usr/share/dotnet/dotnet /usr/local/bin/dotnet
  # --- Marqueur de fin ---
  - 'echo "devbox provisioning OK $(date -u)" > /var/lib/devbox-ready'
'''

// Le multi-ligne '''...''' est brut (pas d'interpolation), ce qui protege $PATH/$HOME/$(date).
// On injecte juste le nom d'utilisateur via replace().
var cloudInit = replace(cloudInitTemplate, '__ADMIN_USER__', adminUsername)

resource nsg 'Microsoft.Network/networkSecurityGroups@2024-05-01' = {
  name: '${vmName}-nsg'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'Allow-SSH'
        properties: {
          priority: 1000
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '22'
          sourceAddressPrefix: allowedSshSource
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${vmName}-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [ '10.20.0.0/16' ]
    }
    subnets: [
      {
        name: 'default'
        properties: {
          addressPrefix: '10.20.1.0/24'
          networkSecurityGroup: {
            id: nsg.id
          }
        }
      }
    ]
  }
}

resource publicIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${vmName}-pip'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: empty(dnsLabelPrefix) ? null : {
      domainNameLabel: dnsLabelPrefix
    }
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: '${vmName}-nic'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: vnet.properties.subnets[0].id
          }
          publicIPAddress: {
            id: publicIp.id
          }
        }
      }
    ]
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: vmName
  location: location
  tags: tags
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: vmName
      adminUsername: adminUsername
      customData: base64(cloudInit)
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: adminPublicKey
            }
          ]
        }
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        createOption: 'FromImage'
        diskSizeGB: osDiskSizeGB
        managedDisk: {
          storageAccountType: 'Premium_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

@description('IP publique de la VM.')
output publicIp string = publicIp.properties.ipAddress

@description('FQDN public (vide si pas de dnsLabelPrefix).')
output fqdn string = empty(dnsLabelPrefix) ? '' : publicIp.properties.dnsSettings.fqdn

@description('Commande SSH prete a copier.')
output sshCommand string = 'ssh ${adminUsername}@${empty(dnsLabelPrefix) ? publicIp.properties.ipAddress : publicIp.properties.dnsSettings.fqdn}'
