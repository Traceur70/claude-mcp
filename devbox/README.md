# VM de remote dev (Azure)

VM Linux **Ubuntu 24.04** auto-configurée pour du remote dev avec Claude :
**Docker + Node 22/npm + Angular CLI (>21) + .NET 10 SDK**, installés au 1er boot via cloud-init.

> Indépendant de l'infra applicative (`infra/main.bicep` = App Service OneDriveMcp). Rien n'est partagé.

## Fichiers

| Fichier | Rôle |
|---|---|
| `main.bicep` | Scope souscription : crée le RG `claude-devbox` puis appelle `vm.bicep`. |
| `vm.bicep` | Réseau (VNet/Subnet/NSG/IP publique/NIC) + VM + cloud-init. |
| `main.bicepparam` | Valeurs par défaut (région, taille, user, etc.). |
| `deploy.sh` | validate → what-if → create via Azure CLI. Injecte la clé SSH. |

## Déploiement

```bash
az login
az account set --subscription <ID>
ssh-keygen -t ed25519          # si pas déjà fait
./devbox/deploy.sh              # utilise ~/.ssh/id_ed25519.pub
```

## Après le déploiement

L'outillage finit de s'installer en arrière-plan (~3-5 min). Connecte-toi puis :

```bash
ssh azureuser@<ip>
cloud-init status --wait        # attend la fin du provisioning
docker --version && node -v && npm -v && ng version && dotnet --version
```

Le NSG n'ouvre que **SSH (22)**. Pour atteindre tes serveurs de dev (Angular 4200, API .NET 5000…),
forwarde les ports via SSH :

```bash
ssh -L 4200:localhost:4200 -L 5000:localhost:5000 azureuser@<ip>
```

## Scalabilité

- **Vertical (par défaut)** : change `vmSize` dans `main.bicepparam` (ex. `Standard_D8s_v5`) et relance `deploy.sh`. Redimensionnement à chaud, sans recréer la VM.
- **Horizontal** : pour plusieurs box de dev identiques, dupliquer le déploiement avec des `vmName` différents, ou migrer `vm.bicep` vers un **VM Scale Set** (`Microsoft.Compute/virtualMachineScaleSets`) avec autoscale. À faire seulement si tu veux une flotte ; pour une box de dev unique, le vertical suffit.

## Sécurité

- Auth **par clé SSH** uniquement (mot de passe désactivé).
- Restreins `allowedSshSource` à ton IP (`x.x.x.x/32`) plutôt que `*`.
- Aucune clé n'est committée : `deploy.sh` lit `~/.ssh/id_ed25519.pub` et la passe en paramètre.
