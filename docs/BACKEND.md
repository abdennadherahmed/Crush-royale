# Backend : Supabase et serveur .NET

## Vue d'ensemble

- **Supabase Auth** : comptes invités (connexion anonyme) puis liaison à Google. Le client reçoit un jeton JWT.
- **API `CrushRoyale.Server`** (ASP.NET 8) : vérifie ce jeton grâce aux clés publiques Supabase (JWKS). C'est le
  seul composant qui écrit en base.
- **Postgres Supabase** : toutes les tables ont la RLS activée. Le serveur s'y connecte avec le rôle dédié
  `crush_api`, jamais avec la clé secrète.
- **Supabase Realtime** : diffuse les nouveaux messages de guilde. Un joueur ne reçoit que ceux de sa propre
  guilde.

## 1. Créer le projet Supabase

1. Créer un projet sur supabase.com et noter la référence du projet (`<ref>`) et la région.
2. **Appliquer le schéma**, au choix :
   - coller `supabase/migrations/20260914120000_init.sql` dans l'éditeur SQL et l'exécuter ;
   - ou, avec la CLI Supabase, lancer `supabase link --project-ref <ref>` puis `supabase db push`.
3. **Activer la connexion du rôle API** (éditeur SQL) :
   ```sql
   alter role crush_api with login password 'un-mot-de-passe-long-et-aleatoire';
   ```
4. **Auth > Sign In / Providers** :
   - activer **Anonymous sign-ins** (comptes invités) ;
   - activer **Google** : saisir l'identifiant client *Web* OAuth et son secret, et ajouter l'identifiant client
     *Android* dans « Authorized Client IDs ».
5. **Project Settings > JWT Keys** : passer aux clés de signature asymétriques. Le serveur valide les jetons
   via `/auth/v1/.well-known/jwks.json` et n'a donc besoin d'aucun secret.
6. **Project Settings > API Keys** : copier la clé *publishable* (`sb_publishable_…`) dans
   `unity/CrushRoyale/Assets/Resources/Config/client.json`. Ne jamais mettre la clé secrète dans le jeu.
7. **Data API** : les tables ne sont pas exposées aux rôles `anon` et `authenticated`. Le jeu passe
   uniquement par l'API .NET.

## 2. Chaîne de connexion

Utiliser **l'une** des deux :

- **Connexion directe** (IPv6) :
  ```
  Host=db.<ref>.supabase.co;Port=5432;Database=postgres;Username=crush_api;Password=...;SSL Mode=Require
  ```
- **Session pooler** (IPv4, conseillé si l'hébergeur ne gère pas l'IPv6) :
  ```
  Host=aws-0-<region>.pooler.supabase.com;Port=5432;Database=postgres;Username=crush_api.<ref>;Password=...;SSL Mode=Require
  ```

## 3. Configuration du serveur

Sections de `appsettings.json`. Chaque clé peut aussi être passée en variable d'environnement, avec deux
tirets bas comme séparateur (par exemple `Database__ConnectionString`).

| Clé | Rôle |
|---|---|
| `Supabase__Url` | `https://<ref>.supabase.co` |
| `Supabase__Audience` | `authenticated` (par défaut) |
| `Supabase__JwksUrl` | Optionnel ; par défaut `{Url}/auth/v1/.well-known/jwks.json` |
| `Supabase__DevAuthEnabled` | Développement seulement : authentification par l'en-tête `X-Dev-User`. Refusé en Production |
| `Database__ConnectionString` | Chaîne de connexion du rôle `crush_api` |
| `Database__UseInMemory` | Développement seulement : stockage en mémoire. Refusé en Production |
| `Iap__PackageName` | `com.crushroyale.game` |
| `Iap__ServiceAccountJsonPath` | Chemin du JSON du compte de service Google Play |
| `Iap__AllowFakeReceipts` | Tests seulement : accepte les reçus `fake:<orderId>`. Refusé en Production |
| `Game__MatchExpiryMinutes` | Délai avant expiration d'une partie non soumise (20) |
| `Game__MatchmakingTickMs` | Fréquence du matchmaking (1000) |
| `Game__RateLimitPerMinute` | Requêtes d'écriture par joueur par minute (120) |
| `Game__EnableBackgroundJobs` | Matchmaking, fantômes, expirations, clôture de saison (true) |
| `Game__AdminUserIds__0` | Identifiants Supabase des administrateurs (ou `app_metadata.role = "admin"`) |

Les options réservées au développement font planter le démarrage si elles sont activées en Production. Une
mauvaise configuration ne peut donc pas passer inaperçue en ligne.

## 4. Google Play (achats intégrés)

1. **Play Console > Monétiser > Produits** : créer les produits intégrés avec les identifiants listés dans
   [UNITY.md](UNITY.md#achats-intégrés).
2. **Google Cloud** : créer un compte de service, activer l'API *Google Play Android Developer* et télécharger
   la clé JSON.
3. **Play Console > Utilisateurs et autorisations** : inviter le compte de service avec les droits « Afficher
   les données financières » et « Gérer les commandes et abonnements ».
4. Déposer le JSON sur le serveur (secret monté, jamais dans git) et renseigner `Iap__ServiceAccountJsonPath`.

**Déroulé d'un achat** :
1. Le jeu demande l'autorisation au serveur (`/v1/iap/precheck` : âge, plafond mensuel des mineurs).
2. Google Play encaisse.
3. Le serveur vérifie le reçu auprès de Google, crédite une seule fois, puis acquitte.
4. Le jeu consomme l'achat.

## 5. Lancer en local

```bash
dotnet run --project src/CrushRoyale.Server --environment Development
```

Stockage en mémoire, aucun Supabase nécessaire. Appeler l'API avec l'en-tête `X-Dev-User: joueur-test-1`.

Pour tester contre un vrai projet Supabase en local, garder `Development` mais surcharger :

```bash
dotnet run --project src/CrushRoyale.Server --environment Development -- --Database:UseInMemory=false --Database:ConnectionString="Host=...;SSL Mode=Require" --Supabase:Url=https://<ref>.supabase.co --Supabase:DevAuthEnabled=false
```

## 6. Déployer

Image Docker (à construire depuis la racine du dépôt) :

```bash
docker build -f src/CrushRoyale.Server/Dockerfile -t crushroyale-api .
```

```bash
docker run -p 8080:8080 -e Supabase__Url=https://<ref>.supabase.co -e Database__ConnectionString="..." -e Iap__ServiceAccountJsonPath=/secrets/play.json -v /chemin/play.json:/secrets/play.json:ro crushroyale-api
```

**Render (gratuit)** : `render.yaml` à la racine décrit le service. *Render > New > Blueprint*, choisir le dépôt,
puis saisir `Database__ConnectionString` (chaîne du *session pooler*, IPv4). L'URL publique
`https://crushroyale-api.onrender.com` est déjà dans `client.json`. Limite du plan gratuit : le service s'endort
après 15 minutes sans requête (premier appel lent, matchmaking en pause pendant le sommeil).

**Hébergement** : n'importe quel hébergeur de conteneurs convient (Fly.io, Render, Azure Container Apps, Cloud
Run…). Contraintes :
- **Une seule instance** tant que le matchmaking reste en mémoire. Pour plusieurs instances, désactiver
  `Game__EnableBackgroundJobs` sur toutes sauf une.
- **HTTPS** assuré par l'hébergeur, puis mettre l'URL publique dans `apiBaseUrl` de `client.json`.
- **Surveillance** : `GET /health`.

## 7. Exploitation

- **Équilibrage à chaud** : `PUT /v1/admin/balance`. La nouvelle version est validée puis stockée dans
  `remote_config` ; les clients la récupèrent au démarrage et contrôlent son empreinte.
- **Réinitialisation hebdomadaire** : automatique chaque lundi 00:00 UTC (tâche de fond), ou manuelle via
  `POST /v1/admin/jobs/weekly-reset`.
- **Anti-triche** : `GET /v1/admin/flags` pour lister les signalements, `POST /v1/admin/flags/{flagId}/review`
  pour trancher.
- **Support** : `POST /v1/admin/grant` pour créditer un joueur. Chaque opération est tracée dans le grand
  livre, qui n'accepte que des ajouts.
