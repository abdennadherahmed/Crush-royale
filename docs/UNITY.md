# Projet Unity

## Prérequis

- **Unity 6 LTS**, installé avec le module **Android Build Support** (OpenJDK, Android SDK et NDK).
- **SDK .NET 8** pour la synchronisation des DLL.

## Première ouverture

1. **Synchroniser le projet**, depuis la racine du dépôt :
   ```bash
   powershell -ExecutionPolicy Bypass -File tools/sync-unity.ps1
   ```
   Le script compile `CrushRoyale.Core`, `CrushRoyale.Contracts` et `CrushRoyale.Client` vers
   `Assets/Plugins/CrushRoyale`. Il exporte aussi les données de jeu vers `Assets/Resources/Data` et génère les
   11 langues dans `Assets/Resources/Localization`.
2. **Ouvrir le projet** : dans Unity Hub, *Add project from disk* et choisir `unity/CrushRoyale`. Les paquets de
   `Packages/manifest.json` se téléchargent : uGUI, Newtonsoft JSON, In-App Purchasing, Mobile Notifications.
3. **Préparer le projet** avec le menu **Crush Royale** :
   - **1. Configure Android Player Settings** : identifiant `com.crushroyale.game`, IL2CPP, ARM64, API 24
     minimum, portrait, format AAB ;
   - **2. Create Boot Scene** : crée la scène de démarrage, tout le reste est construit en code ;
   - **3. Sync Core DLLs + Game Data** : relance le script de l'étape 1 depuis Unity ;
   - **4. Audit Localization** : vérifie qu'aucun texte n'est manquant.
4. **Entrée clavier et tactile** : *Project Settings > Player > Active Input Handling* sur **Input Manager (Old)**
   ou **Both**.
5. **Lancer** avec **Play**. Sans backend configuré, le jeu démarre en **mode entraînement hors ligne** (niveaux
   1 à 50 et PvP contre un bot) : pratique pour tester le gameplay tout de suite.

> Le code Unity a été écrit sans éditeur disponible. Quelques erreurs de compilation sont possibles à la
> première ouverture ; elles devraient être mineures (API ou using manquant). Le moteur de jeu, lui, est
> entièrement testé.

## Brancher le backend

Remplir `Assets/Resources/Config/client.json` :

| Clé | Valeur |
|---|---|
| `apiBaseUrl` | URL publique de l'API .NET (HTTPS) |
| `supabaseUrl` | `https://<ref>.supabase.co` |
| `supabasePublishableKey` | Clé *publishable* Supabase (jamais la clé secrète) |
| `googleWebClientId` | Identifiant client OAuth **Web** (celui configuré dans Supabase Auth) |
| `deviceSalt` | Sel de l'empreinte d'appareil anonyme (ne plus le changer après la sortie) |
| `iapEnabled` | `true` pour activer les achats intégrés |
| `adsEnabled` | `false` tant qu'aucun SDK publicitaire n'est branché |
| `privacyPolicyUrl`, `termsUrl`, `supportEmail` | Pages légales et contact |

Voir [BACKEND.md](BACKEND.md) pour créer le projet Supabase et déployer l'API.

## Connexion Google

- **Plugin Android** : `Assets/Plugins/Android/GoogleSignInBridge.java` s'appuie sur Google Play Services.
- **Dépendance Gradle** : cocher *Player Settings > Publishing Settings > Custom Main Gradle Template*, puis
  ajouter dans `mainTemplate.gradle` :
  ```
  implementation 'com.google.android.gms:play-services-auth:21.2.0'
  ```
- **Google Cloud Console** : créer un identifiant client **Android** avec le nom de package et l'empreinte SHA-1
  de la clé de signature (clé d'upload et clé Play App Signing), puis l'ajouter dans Supabase.

## Achats intégrés

Produits consommables à créer dans la Play Console :

| Identifiant | Contenu | Prix |
|---|---|---|
| `crushroyale.orbes.pack1` | 74 orbes | 0,99 € |
| `crushroyale.orbes.pack2` | 385 + 10 orbes | 4,99 € |
| `crushroyale.orbes.pack3` | 965 + 70 orbes | 12,99 € |
| `crushroyale.orbes.pack4` | 2 100 + 100 orbes | 24,99 € |
| `crushroyale.orbes.pack5` | 5 250 + 250 orbes | 49,99 € |
| `crushroyale.orbes.pack6` | 11 000 + 1 000 orbes | 99,99 € |
| `crushroyale.orbes.mega` | 27 500 + 2 500 orbes | 199,99 € |

Produits non consommables :

| Identifiant | Contenu | Prix |
|---|---|---|
| `crushroyale.removeads` | Suppression des publicités interstitielles | 4,99 € |
| `crushroyale.battlepass` | Passe de combat premium de la saison | défini dans `GameBalance` |
| `crushroyale.crown` | Couronne royale (+10 % de pièces, cosmétique) | 19,99 € |

Les prix affichés en jeu sont ceux de Google Play (devise locale) quand ils sont disponibles.

**Tester** :
- activer une piste de **test interne** et ajouter des comptes testeurs de licence ;
- côté serveur de développement, `Iap:AllowFakeReceipts=true` accepte les reçus `fake:<orderId>`.

## Build Android

1. *File > Build Profiles > Android*, puis **Switch Platform**.
2. *Player Settings > Publishing Settings* : créer ou choisir le keystore de signature.
3. **Build** pour produire l'AAB à envoyer dans la Play Console.

## Organisation du code (`Assets/Scripts`)

| Dossier | Contenu |
|---|---|
| `Core` | Démarrage (`GameRoot`), configuration, sauvegarde locale, localisation (dont l'arabe de droite à gauche), vibrations |
| `UI` | Thème, sprites procéduraux, fabrique de widgets, pile d'écrans, dialogues, notifications à l'écran |
| `Gameplay` | Vue du plateau, entrée tactile, animations, boucle de partie sur le thread principal |
| `Screens` | Tous les écrans : accueil, héros, menu, carte, niveau, partie, résultats, PvP, boutique, guilde, amis, classements, succès, passe, quêtes, réglages |
| `Networking` | Backend Supabase et API, connexion Google, achats intégrés, publicités, notifications locales |
| `Audio` | Sons et musiques générés par synthèse |

## Remplacer les placeholders

Tout le visuel et le son sont générés en code pour que le jeu soit jouable immédiatement. Pour y mettre de vrais
assets :

- **Gemmes** : remplacer `ProceduralSprites.Gem` par des sprites importés (garder une forme distincte par
  couleur pour le mode daltonien).
- **Sons** : remplacer les clips de `SynthClips` par des `AudioClip` chargés depuis `Resources`, en gardant les
  identifiants `SoundIds`.
- **Police** : la police système est utilisée pour couvrir l'arabe, le cyrillique et les caractères CJK ; une
  police de marque nécessite des variantes couvrant ces écritures.
