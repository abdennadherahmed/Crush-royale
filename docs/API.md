# API Crush Royale (v1)

Les DTO sont définis dans `src/CrushRoyale.Contracts` et partagés avec le client : `CrushApi` dans
`src/CrushRoyale.Client/ApiClient.cs` expose une méthode typée pour chaque route.

## Conventions

- **Authentification** : `Authorization: Bearer <access token Supabase>`. En développement uniquement,
  l'en-tête `X-Dev-User: <id>` suffit.
- **Format** : JSON en camelCase ; les énumérations passent en texte (`"Gold"`, `"FreezingGel"`).
- **Erreurs** : toute réponse non-2xx a pour corps `{ "code": "NotEnoughCoins", "message": "..." }`. Les codes
  reprennent l'énumération `ErrorCode` du moteur, plus `Unauthorized`, `RateLimited`, `Conflict` et `Internal`.
- **Limite de débit** : environ 120 écritures par minute par joueur, au-delà réponse `429`.
- **Idempotence** : les crédits (récompenses, achats, reçus) sont protégés par des clés uniques côté serveur.
  Rejouer une requête ne crédite jamais deux fois.
- **Replays** : une partie commence par `start`, qui renvoie la graine et la configuration. Le client renvoie
  ensuite les actions horodatées ; le serveur les re-simule avant d'accorder quoi que ce soit.

## Système et compte

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/health` | Sonde de santé (sans authentification) |
| GET | `/v1/config` | Équilibrage courant (`GameBalance`) et son empreinte |
| POST | `/v1/auth/login` | Crée ou charge le joueur (appareil, région, langue) ; renvoie le profil |
| GET | `/v1/player/me` | Profil complet : portefeuille, vies, VIP, PvP, histoire, inventaire |
| PUT | `/v1/player/me/hero` | Genre, nom du héros, pseudo, âge déclaré, langue |
| GET | `/v1/player/{id}/stats` | Profil public et statistiques |
| GET | `/v1/players/search?q=` | Recherche par pseudo ou identifiant |

## Histoire

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/story/{stageId}/data` | Données d'un niveau |
| POST | `/v1/story/{stageId}/start` | Consomme une vie, applique le loadout, renvoie graine et configuration |
| POST | `/v1/story/matches/{matchId}/continue` | Continuer avec des coups en plus (gratuit ou payant) |
| POST | `/v1/story/matches/{matchId}/complete` | Soumet le replay : étoiles, récompenses, déblocages, événements d'histoire |
| POST | `/v1/story/choices` | Choix narratif (vérifie les conditions ; peut déclencher une fin) |
| POST | `/v1/story/events/{eventId}/seen` | Marque une cinématique comme vue |

## Économie

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/v1/stamina/buy` | Acheter des vies |
| POST | `/v1/stamina/vip-life` | Vie gratuite quotidienne des VIP |
| GET | `/v1/shop` | Offres du jour, packs, cosmétiques, prochain renouvellement |
| POST | `/v1/shop/refresh` | Renouveler les offres contre des orbes |
| POST | `/v1/shop/purchase` | Achat en pièces ou en orbes (`method` : `Coins` ou `Orbes`) |
| POST | `/v1/iap/precheck` | Vérifie qu'un achat réel est autorisé (âge, plafond mineurs) |
| POST | `/v1/iap/validate` | Valide un reçu Google Play et crédite une seule fois |
| POST | `/v1/inventory/equip` | Équiper un cosmétique |
| POST | `/v1/ads/rewarded` | Récompense de vidéo (`life` ou `coins`), plafonnée par jour |

## PvP

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/v1/pvp/matchmaking` | Entrer en file avec un loadout |
| GET | `/v1/pvp/matchmaking` | État de la recherche : `Waiting`, `Matched` (avec la partie) ou `TimedOut` |
| DELETE | `/v1/pvp/matchmaking` | Quitter la file |
| GET | `/v1/pvp/match/{matchId}` | Informations et résultat d'une partie |
| POST | `/v1/pvp/match/record` | Soumet le replay PvP : trophées, ligue, gains (`Pending` si l'adversaire en direct joue encore) |
| GET | `/v1/leaderboard/weekly?league=&limit=` | Classement hebdomadaire (toutes ligues ou une ligue) |
| GET | `/v1/leaderboard/guilds` | Classement des guildes |

## Amis

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/friends` | Amis, demandes reçues et envoyées, délais de défi |
| POST | `/v1/friends/request` | Envoyer une demande (`playerId`) |
| POST | `/v1/friends/accept` | Accepter une demande |
| POST | `/v1/friends/decline` | Refuser une demande |
| POST | `/v1/friends/remove` | Retirer un ami |
| POST | `/v1/friends/block` | Bloquer un joueur |
| POST | `/v1/friends/{friendId}/challenge` | Défi amical (sans trophées) |
| GET | `/v1/friends/map` | Position des amis sur la carte du monde |

## Guildes

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/v1/guild/create` | Créer une guilde (coût en pièces) |
| GET | `/v1/guild/me` | Ma guilde : membres, technologies, boss, rang |
| GET | `/v1/guild/search?q=` | Rechercher des guildes |
| GET | `/v1/guild/{guildId}` | Détail d'une guilde |
| POST | `/v1/guild/{guildId}/join` | Rejoindre |
| POST | `/v1/guild/leave` | Quitter |
| POST | `/v1/guild/kick` | Exclure un membre (officier ou chef) |
| POST | `/v1/guild/role` | Promouvoir, rétrograder ou nommer chef |
| POST | `/v1/guild/invite` | Inviter un joueur |
| PUT | `/v1/guild/settings` | Description, ouverture, trophées minimum |
| POST | `/v1/guild/donate` | Don pour faire monter la guilde de niveau |
| POST | `/v1/guild/tech` | Dépenser un point de technologie |
| POST | `/v1/guild/boss/start` | Démarrer une attaque du boss hebdomadaire |
| POST | `/v1/guild/boss/damage` | Soumettre le replay d'attaque et appliquer les dégâts |
| POST | `/v1/guild/chat` | Envoyer un message (modéré) |
| GET | `/v1/guild/chat?before=` | Historique du chat |

## Progression

| Méthode | Route | Rôle |
|---|---|---|
| GET | `/v1/achievements` | Succès, pages complétées, bonus de pièces |
| POST | `/v1/achievement/unlock` | Récupérer la récompense d'un succès |
| GET | `/v1/quests/daily` | Quêtes du jour |
| POST | `/v1/quests/{questId}/claim` | Récupérer une quête terminée |
| POST | `/v1/login-bonus/claim` | Bonus de connexion quotidien |
| GET | `/v1/battlepass` | Saison, XP, paliers |
| POST | `/v1/battlepass/claim` | Récupérer un palier (gratuit ou premium) |

## Modération et administration

| Méthode | Route | Rôle |
|---|---|---|
| POST | `/v1/admin/report/cheat` | Signaler un joueur (tout joueur authentifié) |
| GET | `/v1/admin/flags` | Signalements anti-triche (admin) |
| POST | `/v1/admin/flags/{flagId}/review` | Trancher un signalement : relaxe, avertissement, suspension, bannissement (admin) |
| POST | `/v1/admin/grant` | Créditer un joueur, tracé dans le grand livre (admin) |
| PUT | `/v1/admin/balance` | Publier un nouvel équilibrage validé (admin) |
| POST | `/v1/admin/jobs/weekly-reset` | Forcer la clôture de la saison hebdomadaire (admin) |

Est administrateur un utilisateur dont `app_metadata.role` vaut `admin` ou dont l'identifiant figure dans
`Game:AdminUserIds`.
