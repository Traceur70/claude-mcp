#!/usr/bin/env bash
# Provisionne l'environnement Azure (RG claude-mcp + App Service Plan B1 + Web App .NET 8).
# Pre-requis : Azure CLI installe et connecte (az login), abonnement selectionne (az account set).
set -euo pipefail

LOCATION="westeurope"
DEPLOYMENT_NAME="claude-mcp-infra"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo ">> Validation du modele Bicep..."
az deployment sub validate \
  --name "$DEPLOYMENT_NAME" \
  --location "$LOCATION" \
  --template-file "$SCRIPT_DIR/main.bicep" \
  --parameters "$SCRIPT_DIR/main.bicepparam" \
  --output none

echo ">> Apercu des changements (what-if)..."
az deployment sub what-if \
  --name "$DEPLOYMENT_NAME" \
  --location "$LOCATION" \
  --template-file "$SCRIPT_DIR/main.bicep" \
  --parameters "$SCRIPT_DIR/main.bicepparam"

echo ">> Deploiement..."
az deployment sub create \
  --name "$DEPLOYMENT_NAME" \
  --location "$LOCATION" \
  --template-file "$SCRIPT_DIR/main.bicep" \
  --parameters "$SCRIPT_DIR/main.bicepparam"

echo ">> Termine. Sorties :"
az deployment sub show --name "$DEPLOYMENT_NAME" --query properties.outputs
