# Décisions de conception

Ce document liste les endroits où l'implémentation s'écarte du GDD (`CRUSH_ROYALE_GAME_DESIGN_DOC.md`) ou le
complète. Chaque valeur est un réglage par défaut : tout l'équilibrage vit dans `GameBalance`
(`src/CrushRoyale.Core/Config`) et peut être modifié à chaud par l'admin (`PUT /v1/admin/balance`).

## Architecture

### Supabase + serveur .NET au lieu de Firebase
- **Pourquoi** : l'économie (pièces, orbes, achats), les trophées et les replays demandent des transactions et
  une source de vérité unique. Postgres donne transactions, contraintes et RLS ; Firestore n'offre rien
  d'équivalent pour un grand livre de comptes.
- **Correspondance** : Firebase Auth devient Supabase Auth (anonyme puis Google) ; Cloud Functions deviennent
  l'API .NET ; Realtime Database devient Supabase Realtime (chat de guilde) ; FCM devient des notifications
  locales (rappels de vies, bonus quotidien, boss de guilde).
- Le client n'écrit jamais en base : seule l'API (rôle `crush_api`) écrit. Les joueurs n'ont qu'un droit de
  lecture sur les messages de leur guilde, pour le temps réel.

### Moteur déterministe partagé
- Même code C# sur le téléphone et le serveur.
- Hasard PCG32 et calculs entiers, pour des résultats identiques sur toutes les machines.
- Une source de hasard par colonne pour les recharges : deux joueurs en ghost play voient exactement les mêmes
  gemmes tomber.
- Une partie = graine + actions horodatées. Le serveur rejoue le replay : un score qu'il ne retrouve pas est
  refusé (`ReplayMismatch`), sans sanction automatique si la cause est une version différente (`VersionMismatch`).
- Délai minimal entre deux actions, calé sur la durée des animations : les bots trop rapides sont détectés.

### Interface Unity générée en code
Pas de scènes ni de prefabs : tous les écrans sont construits en C#. Les modifications restent lisibles dans
git, rien ne se casse en binaire, et le projet tient en une seule scène vide.

## Gameplay

| Sujet | GDD | Implémentation | Raison |
|---|---|---|---|
| Multiplicateurs de cascade | Imprécis | Niveaux 0-1 x1, 2 x1,5, 3 x2, 4 et plus x3, +10 % par cascade enchaînée | Progression lisible, pas d'explosion des scores |
| Bonus « Cascade Fill » | 500 points | 150 points | 500 rendait les cascades plus rentables que les objectifs |
| Gel Givrant | Gèle le plateau adverse | Les points marqués par l'adversaire pendant 10 s ne comptent pas | En ghost play l'adversaire est un enregistrement : on ne peut pas geler son plateau, on neutralise ses points |
| Difficulté fin d'histoire | — | Facteur d'objectif relevé à 1000 ‰ | Un bot glouton gagnait 64 % des derniers niveaux : trop facile |
| Aide à la difficulté | — | Petit coup de pouce après plusieurs échecs sur un niveau | Évite les murs de frustration |

## Économie et monétisation

- **VIP sans pay-to-win.**
  - L'accélération VIP porte sur la recharge des vies, pas sur les parties.
  - Le vol de bonus n'existe qu'en défi amical.
  - La couronne donne seulement +10 % de pièces et un visuel.
- **Prix transparents** : chaque pack affiche son prix réel et le nombre d'orbes par euro. Bonus du pack 3
  ramené à 70 orbes pour garder une progression cohérente.
- **Remise VIP** : 3 % par niveau, plafonnée à 30 %.
- **Protection des mineurs** : pas d'achat avant 13 ans ; plafond de 50 € par mois pour les mineurs. L'âge est
  demandé à la création du héros, uniquement pour ces limites.
- **Achats Google Play** : le serveur valide le reçu et l'acquitte, le client consomme l'objet. Un reçu n'est
  crédité qu'une fois (clés d'idempotence dans un grand livre en ajout seul).
- **Publicités** : vidéos récompensées uniquement à la demande, plafonnées par jour ; « Supprimer les pubs »
  retire les interstitiels mais laisse les vidéos facultatives.

## PvP et saisons

- **Réinitialisation hebdomadaire douce** au lieu d'une remise à zéro : plancher de 300 trophées, l'excédent au-dessus est divisé par deux. Le classement reste frais sans effacer le niveau réel des joueurs.
- **Matchmaking** : fenêtre de trophées qui s'élargit avec l'attente ; adversaire en direct si disponible,
  sinon un fantôme de niveau proche.
- **Défis amicaux** : aucun trophée en jeu, 5 minutes d'attente entre deux défis contre le même ami.

## Histoire

- **Mira meurt à l'acte 4 (niveau 760)** en protégeant le héros de Mark. Le GDD se contredisait (acte 3 dans la
  fiche personnage, acte 4 dans les temps forts) ; l'acte 4 garde la trahison au bon moment.
- **Mark est obligatoire** : sa trahison porte le rebondissement principal. Il revient au niveau 900 s'il a été
  épargné. **Soren reste optionnel**.
- **Trois fins** : Sacrifice, Corruption, Rédemption. La Rédemption exige d'avoir pardonné à Lyra et épargné
  Mark : les choix du joueur comptent vraiment.
- **Déblocages** :

  | Fonction | Niveau |
  |---|---|
  | Quêtes | 5 |
  | Publicités | 10 |
  | Boutique | 15 |
  | Amis | 20 |
  | PvP | 25 |
  | Passe de combat | 30 |
  | Guildes | 40 (le GDD ne précisait pas) |

- Après le niveau 1000, des niveaux sans fin reprennent le rythme du dernier acte avec de nouvelles graines.

## Contenu ajouté (absent ou flou dans le GDD)

- **Succès** : 60 succès sur 12 pages ; chaque page complétée donne +5 % de pièces à vie et un cadre.
- **Rétention** : passe de combat de 50 paliers, quêtes quotidiennes, calendrier de connexion sur 7 jours.
- **Guildes** : arbre technologique de 21 rangs pour 19 points (les guildes doivent choisir), dons, rôles.
- **Chat** : modération (filtre, limite de débit, masquage).
- **Anti-triche** : score de suspicion, signalements, bannissements progressifs, registre des appareils.
- **Accessibilité** : mode daltonien avec une forme différente par couleur de gemme.
- **Hors ligne** : mode entraînement quand le serveur est injoignable ; file d'attente hors ligne qui renvoie
  les résultats au retour du réseau.
- **RGPD et vie privée** : consentement au premier lancement, suppression de compte, anonymisation autorisée
  dans le grand livre.

## Localisation

- 11 langues : anglais, français, arabe (avec mise en forme de droite à gauche), espagnol, allemand, italien,
  portugais, russe, japonais, coréen, chinois.
- L'interface est complète dans toutes les langues. Les dialogues sont écrits en anglais et en français ; les
  autres langues affichent l'anglais en attendant une traduction professionnelle.
- Les fichiers sont générés et vérifiés par `tools/LocalizationGen` (clés manquantes, paramètres `{0}`
  incohérents, personnages inconnus).

## Limites connues et prochaines étapes

1. **Unity** : ouvrir le projet dans Unity 6 et corriger les éventuelles erreurs de compilation, jamais testées
   faute d'éditeur sur la machine de développement.
2. **Assets** : remplacer les formes et sons procéduraux par de vrais graphismes et une vraie musique.
3. **Publicités** : brancher un SDK (LevelPlay ou AdMob) derrière l'interface `IAdsProvider`.
4. **Traductions** : faire relire les traductions par des locuteurs natifs et traduire les dialogues dans les 9
   autres langues.
5. **Notifications push serveur** : à ajouter si besoin (aujourd'hui seulement des notifications locales).
6. **Légal** : publier les pages de politique de confidentialité et de conditions d'utilisation, puis renseigner
   leurs adresses dans `client.json`.
