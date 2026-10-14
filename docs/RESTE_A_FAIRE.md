# Reste à faire

État au 14 octobre 2026. Les points barrés sont faits et poussés ; ils attendent la version 59.

## Ce que la 59 apporte

1. ~~**Le gel de la boutique.**~~ Corrigé. Un achat qui n'obtient pas de réponse du magasin échoue au bout de
   quatre-vingt-dix secondes au lieu d'attendre indéfiniment, et deux achats ne peuvent plus se chevaucher.
2. ~~**La connexion Google.**~~ Les deux identifiants OAuth existent et celui du client web est dans la
   configuration. Tes testeurs sortent du compte invité.
3. ~~**Les cadres coupés à droite.**~~ La cause était bien dans ce que les deux grilles partagent : le canevas
   répartissait son échelle moitié largeur moitié hauteur, donc sur un téléphone plus allongé que 16:9 la
   largeur logique tombait sous 1080 alors que toutes les tailles du code sont écrites pour 1080. Elle est
   maintenant fixée sur la largeur.
4. ~~**`GridFit` n'ajustait que la largeur des cases.**~~ La hauteur suit désormais, donc une case rétrécie
   garde sa forme au lieu de s'aplatir.
5. ~~**Les gemmes de ligne presque invisibles.**~~ Les deux spéciales portent des bandes sur toute la case, à la
   façon des bonbons rayés. La surface dorée passe de 8,5 % de l'image à 41 %.
6. ~~**Les gestes jetés pendant une cascade.**~~ Un glissement fait pendant que le plateau bouge est reconnu,
   mis en file et joué dès qu'il se pose. Viser une cible de pouvoir attend toujours, parce que la case sous le
   doigt est encore en train de bouger.
7. ~~**Les durées.**~~ L'échange passe de 250 à 200 ms et l'étape de cascade de 400 à 280.
8. ~~**Les chutes.**~~ La durée de chaque chute suit la racine carrée de sa distance : une colonne se verse au
   lieu de clignoter, et le rebond d'arrivée se décale tout seul.
9. ~~**Les cascades qui ne montaient pas en intensité.**~~ La secousse grandit avec ce que l'étape a pris et
   avec la profondeur de la chaîne. Le son montait déjà d'un demi-ton par étape.
10. ~~**Les publicités.**~~ Tout le circuit est en place, voir la section suivante.

## Les publicités : ce qui est fait, et les deux choses qui restent de ton côté

Le paquet LevelPlay 9.3.2 est installé, le SDK natif vient de Maven Central par `mainTemplate.gradle` (pas du
gestionnaire de réseaux, que personne ne peut cliquer dans un build sans écran), l'adaptateur compile, la vidéo
est proposée quand tu n'as plus de vies, la permission `AD_ID` est déclarée depuis le dépôt, et le consentement
est demandé une fois après l'avis de confidentialité.

Le principe de la récompense vaut la peine d'être connu : le jeu qui dit avoir montré une vidéo ne prouve rien,
puisqu'il tourne sur le téléphone du joueur. C'est LevelPlay qui appelle le serveur directement, signé avec une
clé que seuls eux et toi connaissez, et c'est cet appel qui rend une récompense payable. Un test vérifie la
signature, le fait qu'une vidéo non confirmée ne donne rien, et le fait qu'un rappel répété ne paie qu'une fois.

11. **Sur Render**, trois variables : `Ads__LevelPlayPrivateKey` (une chaîne que tu inventes),
    `Ads__RewardedUnitId` (l'identifiant de l'unité Rewarded Video du tableau de bord LevelPlay) et
    `Game__RewardedAdsEnabled` à `true`. `Ads__InterstitialUnitId` quand tu voudras des interstitiels. Les
    identifiants d'unité sont servis avec la configuration, donc les changer ne demande aucun nouveau build.
12. **Sur LevelPlay**, Apps → ton application → Set S2S callback. L'URL est
    `https://crushroyale-api.onrender.com/api/v1/ads/levelplay?applicationUserId=[USER_ID]&eventId=[EVENT_ID]&rewards=[REWARDS]`
    et la clé privée est exactement celle que tu as mise sur Render. Teste depuis leur page avant d'enregistrer :
    tant que le serveur ne répond pas, le rappel reste inactif.

Tant que ces deux points ne sont pas faits, la vidéo se lance et se regarde mais aucune vie n'est créditée.

## Dans la console Play, quand tu t'y connecteras

13. **Inscrire les 114 testeurs comme testeurs de licence** (Paramètres → Monétisation → Test de licence). Sans
    ça, le premier achat qui fonctionnera sera débité pour de vrai.
14. **Retourner trois déclarations** maintenant que les pubs sont dans le build : contient des annonces,
    identifiant publicitaire, et la collecte à des fins publicitaires dans la sécurité des données. Le manifeste
    déclare déjà `AD_ID` ; ces trois cases sont la moitié que le relecteur regarde.
15. **Vérifier que les onze produits sont visibles depuis le jeu**, la propagation étant faite depuis longtemps.
16. **Deux identifiants OAuth Android supplémentaires**, pour les empreintes de la clé d'importation, afin que
    les APK installés à la main puissent aussi se connecter à Google.
17. **API Play Integrity**, zéro service actif sur sept. Pour un PvP classé, c'est la protection côté appareil
    qui manque, en plus de la resimulation serveur déjà en place.
18. **Les vérifications d'appareil sur la fiche**, à activer après les quatorze jours : activées maintenant,
    elles peuvent barrer la route à un testeur sur un téléphone non certifié.

## Ce qui reste à écrire

19. **Un panneau admin dans les Réglages**, visible seulement pour les comptes administrateurs, pour se créditer
    et débloquer la campagne. Ça demande trois choses qui n'existent pas encore : le profil ne dit pas si le
    compte est administrateur, il n'y a pas de route pour débloquer une étape, et l'écran reste à faire. Ça
    remplacerait les modifications directes en base, qui marchent mais contournent les règles du serveur.

## Ce qui dépend de toi

20. **Ton RIB et tes informations fiscales** sur le profil de paiement. Aucun revenu avant la production, donc
    rien d'urgent, mais les informations fiscales peuvent geler les versements même avec un RIB valide.
21. **La gemme bleue.** J'ai mesuré les six couleurs : la bleue fait 195 px de large sur 250 de haut, ses sœurs
    entre 228 et 252 de large. Elle n'est pas plus petite, elle est plus étroite, et son fichier ne contient
    aucun pixel orange — ce que tu as vu vient de là ou du voisinage dans l'atlas, dont la marge est passée de 4
    à 8 px par précaution. Si ça te gêne encore sur la 59, il faut une nouvelle image au même gabarit que les
    autres, et c'est une décision de direction artistique, pas un bug.
