#!/usr/bin/env bash
# Provisionne une VM de dev Azure (RG claude-devbox + reseau + VM Ubuntu 24.04).
# La VM s'auto-configure au 1er boot via cloud-init : Docker + Node/npm + Angular CLI (>21) + .NET 10.
#
# Pre-requis :
#   - Azure CLI installe et connecte : az login ; az account set --subscription <id>
#   - Une cle SSH : ssh-keygen -t ed25519   (genere ~/.ssh/id_ed25519[.pub])
#
# Usage :
#   ./deploy.sh                          # utilise ~/.ssh/id_ed25519.pub
#   SSH_KEY=~/.ssh/ma_cle.pub ./deploy.sh
set -euo pipefail

LOCATION="westeurope"
DEPLOYMENT_NAME="claude-devbox-infra"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SSH_KEY="${SSH_KEY:-$HOME/.ssh/id_ed25519.pub}"

if [[ ! -f "$SSH_KEY" ]]; then
  echo "ERREUR : cle SSH publique introuvable : $SSH_KEY" >&2
  echo "Genere-en une avec : ssh-keygen -t ed25519" >&2
  exit 1
fi
PUBKEY="$(cat "$SSH_KEY")"

COMMON_ARGS=(
  --name "$DEPLOYMENT_NAME"
  --location "$LOCATION"
  --template-file "$SCRIPT_DIR/main.bicep"
  --parameters "$SCRIPT_DIR/main.bicepparam"
  --parameters "adminPublicKey=$PUBKEY"
)

echo ">> Validation du modele Bicep..."
az deployment sub validate "${COMMON_ARGS[@]}" --output none

echo ">> Apercu des changements (what-if)..."
az deployment sub what-if "${COMMON_ARGS[@]}"

echo ">> Deploiement..."
az deployment sub create "${COMMON_ARGS[@]}"

echo ">> Termine. Sorties :"
az deployment sub show --name "$DEPLOYMENT_NAME" --query properties.outputs -o jsonc

cat <<'EOF'

------------------------------------------------------------------
La VM termine son install en arriere-plan (cloud-init, ~3-5 min).
Verifier que tout est pret, une fois connecte en SSH :
  cloud-init status --wait          # attend la fin du provisioning
  cat /var/lib/devbox-ready         # marqueur de fin
  docker --version && node -v && npm -v && ng version && dotnet --version

Astuce remote dev : forwarde tes ports via SSH (NSG = SSH only) :
  ssh -L 4200:localhost:4200 -L 5000:localhost:5000 azureuser@<ip>
------------------------------------------------------------------
EOF
