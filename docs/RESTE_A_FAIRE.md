# Reste à faire

État au 13 octobre 2026, après le premier retour des testeurs sur la version 57.

## Ce qui attend un nouveau build

Les quatre premiers points voyagent ensemble : autant tout corriger avant de reconstruire, parce que chaque
version coûte cinquante minutes de CI et une mise à jour de plus à tes testeurs.

1. **Le gel de la boutique.** Corrigé et poussé, il n'attend que d'être fabriqué. C'est le plus urgent : tant
   que la 57 est en main, chaque clic sur Acheter bloque le jeu.
2. **La connexion Google.** Les deux identifiants OAuth existent, l'identifiant du client web est déjà dans la
   configuration. Tes testeurs sortiront enfin du compte invité, qui disparaît à la moindre réinstallation.
3. **Les cadres coupés à droite**, sur le calendrier de connexion et sur l'arbre technologique de la guilde.
   Deux grilles différentes coupées de la même façon : la cause est dans le conteneur qu'elles partagent, pas
   dans chacune d'elles. La boutique « trop petite » vient probablement de la même racine.
4. **`GridFit` n'ajuste que la largeur des cases, jamais leur hauteur.** Quand il rétrécit une case pour la
   faire rentrer, elle garde la hauteur écrite dans le code : les cartes se déforment et un trou reste sous la
   grille. À corriger en même temps que le point 3, sinon on échange un défaut contre un autre.
5. **L'éclat orange sur les gemmes bleues.** Le fichier `blue.png` est propre. Le défaut naît à l'empaquetage
   de l'atlas : la bleue fait 195 pixels de large quand les autres en font 230 à 250, elle laisse donc plus de
   vide dans son rectangle, le voisin orange est serré plus près, et le filtrage va chercher ses pixels. Se
   règle dans les marges de l'atlas, pas en regénérant l'image.

## Ce qui ne demande aucun build

6. **Inscrire les 114 testeurs comme testeurs de licence.** Sans ça, le premier achat qui fonctionnera sera
   débité pour de vrai.
7. **Vérifier que les onze produits sont visibles depuis le jeu** une fois la propagation faite, quelques
   heures après leur création.
8. **Un panneau admin dans les Réglages**, visible seulement pour les comptes administrateurs, pour se créditer
   et débloquer la campagne. Ça remplacerait les modifications directes en base, qui marchent mais contournent
   les règles du serveur.

## Les publicités

9. **Intégrer le SDK LevelPlay.** Le compte, l'application et les deux emplacements existent, la clé est dans
   la configuration, l'adaptateur est écrit. Reste le paquet Unity, son branchement, et le consentement RGPD.
10. **Le rappel serveur de récompense (S2S).** Sans lui, rien ne prouve qu'une vidéo a été regardée et les vies
    gratuites deviennent infinies pour qui sait bidouiller.
11. **Retourner trois déclarations** le jour où les pubs arrivent : contient des annonces, identifiant
    publicitaire avec la permission `AD_ID` dans le manifeste, et la collecte à des fins publicitaires dans la
    sécurité des données.

## La fluidité du plateau

Cinq causes, toutes lues dans le code et comparées à ce que font les match-3 open source. La première compte
plus que les quatre autres réunies.

12. **Les gestes du joueur sont jetés, pas mis en file.** Pendant toute la résolution, `BoardInput.Interactable`
    passe à faux et le glissement est purement ignoré. Une cascade de trois étapes verrouille l'écran 1 450
    millisecondes : le joueur glisse, rien ne bouge, il croit que le jeu a raté son geste. Garder le dernier
    glissement et le rejouer quand le plateau redevient vivant.
13. **Les durées sont au-dessus de l'enveloppe habituelle.** `SwapAnimationMs` vaut 250 et `CascadeStepMs` 400,
    donc un échange plus une cascade coûtent 650 ms, ce que ces projets se donnent comme budget pour la
    résolution entière. Viser 200 et 280. Attention : ces valeurs vivent dans le cœur partagé et servent au
    serveur à vérifier qu'une partie n'a pas été jouée trop vite, donc les changer invalide les parties en
    cours au moment du déploiement.
14. **Toutes les gemmes tombent dans le même temps, quelle que soit la distance.** Une seule interpolation, un
    seul facteur, appliqué à tout ce qui tombe : celle qui descend de cinq cases va cinq fois plus vite que
    celle qui descend d'une, et arrive en même temps. Rien n'a de poids. Décaler colonne par colonne et faire
    dépendre la durée de la distance.
15. **Rien n'atterrit.** La courbe accélère puis s'arrête net. Pas de rebond, pas d'écrasement à l'impact.
16. **Les cascades ne montent pas en intensité.** Même durée, même son, même absence de secousse à la deuxième
    et à la cinquième étape. Monter le son d'un demi-ton par étape, renforcer la secousse, afficher le combo
    qui grimpe.
17. **Les gemmes de ligne sont presque invisibles.** Le défaut n'est pas général, il est précis. La bombe porte
    un anneau doré qui occupe toute la case et se voit très bien. Les deux spéciales de ligne, `special_line_h`
    et `special_line_v`, ne sont que **deux petites pointes dorées posées aux bords opposés**, le reste de
    l'image est vide. Sur un téléphone, ça fait quelques pixels de doré sur une gemme déjà claire. Et ce sont
    justement les plus fréquentes, puisqu'un alignement de quatre en crée une : c'est exactement ce que les
    testeurs décrivent quand ils disent ne pas savoir s'ils viennent d'en créer une. La correction est de
    traiter toute la case, comme les bonbons rayés de Candy Crush, pas d'ajouter un détail au bord.

## Plus tard

18. **API Play Integrity**, zéro service actif sur sept. Pour un PvP classé, c'est la protection anti-triche
    côté appareil qui manque, en plus de la resimulation serveur déjà en place.
19. **Les vérifications d'appareil sur la fiche Play Store.** À activer après les quatorze jours : activée
    maintenant, elle peut barrer la route à un testeur sur un téléphone non certifié.
20. **Deux identifiants OAuth Android supplémentaires**, pour les empreintes de la clé d'importation, afin que
    les APK installés à la main puissent eux aussi se connecter à Google.

## Ce qui dépend de toi

21. **Ton RIB et tes informations fiscales** sur le profil de paiement. Aucun revenu avant la production, donc
    rien d'urgent, mais les informations fiscales peuvent geler les versements même avec un RIB valide.
22. **Décider pour la gemme bleue** : la corriger par l'atlas suffit, ou tu veux en plus une nouvelle image au
    même gabarit que ses sœurs pour l'harmonie.
