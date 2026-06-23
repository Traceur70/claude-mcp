# OneDrive MCP — serveur MCP pour lire vos Excel/CSV depuis Claude

Serveur **MCP distant** (ASP.NET Core / .NET 8) déployable sur une **Azure Web App**.
Il permet à Claude de **lister, chercher et lire** les fichiers `.xlsx` / `.csv` de votre **OneDrive**.

L'objectif : après déploiement, vous n'avez qu'à **saisir 3 valeurs** (Client ID, Client Secret, Tenant ID),
faire **une seule connexion Microsoft**, et c'est prêt.

---

## ⚠️ À lire en premier (OneDrive perso)

Un **OneDrive personnel** (compte Microsoft grand public) **ne peut pas** être lu avec uniquement
Client ID + Secret + Tenant (flux *app-only*). Microsoft réserve ce mode aux comptes professionnels.

Pour un OneDrive perso, il faut une **connexion utilisateur déléguée** : vous vous authentifiez
**une seule fois** via la page `/login` de l'application. Le serveur conserve ensuite un *refresh token*
(chiffré, sur le disque persistant `/home` d'Azure) et fonctionne tout seul ensuite.

> C'est l'unique étape en plus des 3 valeurs. Tout le reste est automatique.

---

## 📁 Structure

```
src/OneDriveMcp/
├─ Program.cs                 # Démarrage, endpoints /, /login, /signin-oidc, /mcp
├─ Options/                   # AzureAdOptions (les 3 valeurs), McpOptions (clé API)
├─ Auth/                      # MSAL (token store), clé API, stockage persistant
├─ Graph/OneDriveClient.cs    # Accès OneDrive via Microsoft Graph REST
├─ Files/SpreadsheetReader.cs # Parsing Excel (ClosedXML) + CSV (CsvHelper)
└─ Mcp/OneDriveTools.cs       # Outils MCP : list_files, search_files, read_spreadsheet
.github/workflows/deploy.yml  # Déploiement optionnel via GitHub Actions
infra/                        # Infrastructure as Code (Bicep)
├─ main.bicep                 # Déploiement subscription-scope : crée le RG + les ressources
├─ resources.bicep            # App Service Plan B1 (Linux) + Web App .NET 8
├─ main.bicepparam            # Paramètres (région, noms…)
└─ deploy.sh                  # validate + what-if + deploy en une commande
```

---

## 🚀 Mise en route — pas à pas

### Étape 1 — Inscrire l'application dans Microsoft Entra (Azure)

1. Portail Azure → **Microsoft Entra ID** → **App registrations** → **New registration**.
2. **Supported account types** : choisissez
   **« Personal Microsoft accounts only »** (ou *Accounts in any org directory and personal* si vous hésitez).
3. **Redirect URI** : plateforme **Web**, valeur :
   `https://<NOM-DE-VOTRE-WEBAPP>.azurewebsites.net/signin-oidc`
   *(remplacez `<NOM-DE-VOTRE-WEBAPP>` ; pour tester en local ajoutez aussi `https://localhost:5001/signin-oidc`).*
4. Notez l'**Application (client) ID** → ce sera votre **Client ID**.
5. **Certificates & secrets** → **New client secret** → copiez la **valeur** → ce sera votre **Client Secret**.
6. **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated permissions** →
   ajoutez **`Files.Read`** et **`User.Read`** (`offline_access` est ajouté automatiquement par MSAL).

> **Tenant ID** : pour un OneDrive **perso**, laissez la valeur par défaut **`consumers`** (rien à copier).
> Pour un OneDrive **Entreprise**, mettez votre Tenant ID (GUID).

### Étape 2 — Créer l'environnement Azure (Bicep)

L'infrastructure est décrite dans `infra/` (Infrastructure as Code). Elle crée, en une commande :
le **groupe de ressources `claude-mcp`**, un **App Service Plan B1 Linux** et la **Web App .NET 8**
(HTTPS only, TLS 1.2, FTPS désactivé), avec les **App Settings déjà créés (vides)** prêts à remplir.

```bash
az login
az account set --subscription "<VOTRE-SOUSCRIPTION>"
./infra/deploy.sh            # validate + what-if + déploiement
```

Valeurs par défaut (modifiables dans `infra/main.bicepparam`) :
`location=westeurope`, `resourceGroupName=claude-mcp`, `webAppName=claude-mcp-nadda`, `appServicePlanName=claude-mcp-plan`.

> ⚠️ `webAppName` doit être **unique sur tout Azure**. À la fin, le déploiement affiche les sorties
> `webAppUrl` et **`redirectUri`** : c'est exactement l'URL à enregistrer à l'étape 1.3
> (`https://claude-mcp-nadda.azurewebsites.net/signin-oidc`).

### Étape 3 — Déployer le code

Au choix :

- **GitHub Actions** : renseignez `AZURE_WEBAPP_NAME` dans `.github/workflows/deploy.yml`,
  ajoutez le secret `AZURE_WEBAPP_PUBLISH_PROFILE` (profil de publication Azure), puis poussez sur `main`.
- **Azure CLI** (manuel) :
  ```bash
  az webapp up --name <NOM-WEBAPP> --runtime "DOTNET:8.0" --sku B1
  # ou : dotnet publish src/OneDriveMcp -c Release -o publish puis zip deploy
  ```
- **Visual Studio / VS Code** : clic droit → *Publish* vers votre Web App.

### Étape 4 — Saisir VOS 3 valeurs (la seule chose à écrire 🎯)

Le Bicep a déjà créé ces App Settings (vides). Portail Azure → votre Web App →
**Settings → Environment variables / Application settings** → **éditez les valeurs** :

| Nom de l'App Setting | Valeur                          |
|----------------------|---------------------------------|
| `AzureAd__ClientId`     | *votre Client ID*            |
| `AzureAd__ClientSecret` | *votre Client Secret*        |
| `AzureAd__TenantId`     | `consumers` (OneDrive perso) |
| `Mcp__ApiKey`           | *une chaîne secrète au choix* (protège l'accès au MCP) |

> Notez le **double underscore** `__` (convention Azure pour les sections de configuration).
> Enregistrez → la Web App redémarre.

### Étape 5 — Connexion Microsoft (une seule fois)

Ouvrez dans un navigateur :
`https://<NOM-WEBAPP>.azurewebsites.net/`
→ cliquez **« Se connecter à Microsoft / OneDrive »**, authentifiez-vous, acceptez les permissions.
La page d'accueil affiche alors **« Connecté »**. C'est fini.

### Étape 6 — Brancher Claude sur le MCP

URL du connecteur MCP :
```
https://<NOM-WEBAPP>.azurewebsites.net/mcp?api_key=VOTRE_CLE_API
```
(ou bien sans `?api_key=` dans l'URL, mais en envoyant le header `X-API-Key: VOTRE_CLE_API`).

Ajoutez ce serveur MCP distant dans Claude (connecteur personnalisé / `mcpServers`).
Exemple de config Claude Code (`.mcp.json`) avec header :
```json
{
  "mcpServers": {
    "onedrive": {
      "type": "http",
      "url": "https://<NOM-WEBAPP>.azurewebsites.net/mcp",
      "headers": { "X-API-Key": "VOTRE_CLE_API" }
    }
  }
}
```

---

## 🛠️ Outils MCP exposés

| Outil              | Description                                                              |
|--------------------|-------------------------------------------------------------------------|
| `list_files`       | Liste les Excel/CSV (et dossiers) d'un dossier OneDrive.                |
| `search_files`     | Recherche des Excel/CSV par nom dans tout le OneDrive.                  |
| `read_spreadsheet` | Lit un fichier (par id ou chemin) et renvoie les lignes/colonnes.       |

---

## 💻 Test en local

```bash
cd src/OneDriveMcp
# Renseignez les valeurs sans les committer (User Secrets) :
dotnet user-secrets init
dotnet user-secrets set "AzureAd:ClientId" "..."
dotnet user-secrets set "AzureAd:ClientSecret" "..."
dotnet user-secrets set "AzureAd:TenantId" "consumers"
dotnet user-secrets set "Mcp:ApiKey" "dev-key"
dotnet run
```
Puis ouvrez `https://localhost:5001/` et connectez-vous.
(Pensez à ajouter `https://localhost:5001/signin-oidc` comme URI de redirection dans l'app registration.)

---

## 🔒 Sécurité

- Ne committez **jamais** vos secrets : ils vivent dans les **App Settings Azure** ou les **User Secrets** locaux.
- L'accès au MCP est protégé par la **clé API** (`Mcp__ApiKey`). Utilisez une valeur longue et aléatoire.
- Le refresh token est stocké **chiffré** (ASP.NET Data Protection) sur le disque persistant `/home` d'Azure.
- Permissions Graph en **lecture seule** (`Files.Read`). Pour de l'écriture, il faudrait `Files.ReadWrite`.

---

## ❓ Dépannage

- **`NotConnectedException` côté outils** → vous n'avez pas encore fait la connexion : ouvrez `/login`.
- **Restauration NuGet qui échoue sur `ModelContextProtocol.AspNetCore`** (paquet en preview) :
  ```bash
  dotnet add src/OneDriveMcp package ModelContextProtocol.AspNetCore --prerelease
  ```
- **Erreur de redirection OAuth** → l'URI `/signin-oidc` doit être **exactement** enregistrée dans l'app registration.
- **401 sur `/mcp`** → clé API manquante/incorrecte (header `X-API-Key` ou `?api_key=`).
