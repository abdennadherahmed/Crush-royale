# Crush Royale on Google Play

Everything the listing asks for, written out so the console is twenty minutes of copying rather than a day of
deciding. Limits are Google's: 30 characters for the title, 80 for the short description, 4000 for the full one.

---

## App details

| Field | Value |
|---|---|
| App name | Crush Royale |
| Package | see `unity/CrushRoyale/ProjectSettings` (applicationIdentifier) |
| Default language | English (United States) |
| App or game | Game |
| Category | Puzzle |
| Tags | Match 3, Casual, Multiplayer |
| Free or paid | Free, with in-app purchases |
| Contains ads | No |
| Support email | abdennadherahmed975@gmail.com |
| Privacy policy | https://crushroyale-legal.onrender.com/privacy.html |
| Terms | https://crushroyale-legal.onrender.com/terms.html |

---

## Store listing, English

**Title (12/30)**

    Crush Royale

**Short description (79/80)**

    Match-3 duels on the same board as your rival. Same gems, same seed, 90 seconds.

**Full description**

    Crystalheim has shattered. Five kingdoms are buried under the ice, the sand and the ash, and the crystal
    dragon Valdorax is still above them. Match gems, break what holds the land down, and rebuild it.

    A THOUSAND STAGES, AND THEY FIGHT BACK
    Ice that takes two moves to open. Stones that will not break from a distance. Corruption that spreads on
    every move you spend elsewhere. Chains, cursed gems, mirrors that strike the opposite cell, forges that rot
    a gem every single turn. The hardest stage of each chapter is marked before you spend a life on it, because
    it is meant to need the boosts you bring, not the luck you hope for.

    DUELS DECIDED BY SKILL, NOT BY A TIMER
    Your opponent plays the same board as you, from the same seed, for the same ninety seconds. There is no
    luckier board and no better draw. Climb from Bronze to Crystal, and send a friend the exact board you just
    scored on so they can try to beat it.

    A GUILD, AND SOMETHING TO FIGHT
    Twenty players, a shared chat, weekly donations, and a boss that comes back week after week. 7kou spreads
    corruption, Escobaros freezes the board, Majors Blue plants bombs with a short fuse. The same three
    characters return, so your guild builds a history with them.

    PETS THAT PLAY WITH YOU
    Frost Fox, Sun Fennec, Forest Owl, Ember Salamander, Crystal Drake. Raise them, awaken them, and they hand
    you a power-up mid-match or take a move for you.

    REBUILD THE KINGDOM
    Every star you earn is a stone put back. Fifty sites across five kingdoms, from the Crystal Bridge to the
    citadel itself.

    Eleven languages. Plays offline against bots when you have no signal, and syncs when you are back.

---

## Store listing, French

**Title**

    Crush Royale

**Short description (78/80)**

    Duels match-3 sur le même plateau que l'adversaire. Mêmes gemmes, 90 secondes.

**Full description**

    Crystalheim s'est brisée. Cinq royaumes sont ensevelis sous la glace, le sable et la cendre, et le dragon
    de cristal Valdorax est toujours au-dessus d'eux. Aligne les gemmes, brise ce qui retient la terre, et
    reconstruis-la.

    MILLE NIVEAUX, ET ILS SE DÉFENDENT
    De la glace qui demande deux coups. Des pierres qui ne cèdent pas à distance. Une corruption qui s'étend à
    chaque coup passé ailleurs. Des chaînes, des gemmes maudites, des miroirs qui frappent la case opposée, des
    forges qui pourrissent une gemme à chaque tour. Le niveau le plus dur de chaque chapitre est signalé avant
    que tu n'y laisses une vie, parce qu'il est fait pour exiger les boosts que tu apportes.

    DES DUELS DÉCIDÉS PAR LE TALENT
    Ton adversaire joue le même plateau que toi, depuis la même graine, pendant les mêmes quatre-vingt-dix
    secondes. Il n'y a pas de plateau plus chanceux. Monte de Bronze à Cristal, et envoie à un ami le plateau
    exact sur lequel tu viens de marquer pour qu'il essaie de te battre.

    UNE GUILDE, ET QUELQUE CHOSE À COMBATTRE
    Vingt joueurs, un chat commun, des dons hebdomadaires et un boss qui revient semaine après semaine. 7kou
    répand la corruption, Escobaros gèle le plateau, Majors Blue pose des bombes à mèche courte.

    DES FAMILIERS QUI JOUENT AVEC TOI
    Frost Fox, Sun Fennec, Forest Owl, Ember Salamander, Crystal Drake. Élève-les, éveille-les, et ils te
    tendent un boost en pleine partie ou jouent un coup à ta place.

    RECONSTRUIS LE ROYAUME
    Chaque étoile gagnée est une pierre reposée. Cinquante chantiers dans cinq royaumes.

    Onze langues. Jouable hors ligne contre des bots, synchronisé dès que le réseau revient.

---

## Graphics (in this folder)

| Asset | File | Google's requirement |
|---|---|---|
| App icon | `icon_512.png` | 512 x 512 PNG, no transparency |
| Feature graphic | `feature_1024x500.png` | 1024 x 500 PNG |
| Phone screenshots | `screenshot_1..8.png` | 1080 x 1920, at least 2, at most 8 |

The screenshots are the game as it renders, with no device frame and no added text, because Play rejects both.

---

## Content rating questionnaire

Answer honestly; these are the answers for this game.

- Category: **Game**
- Violence: none. No blood, no weapons used against people. The bosses are destroyed as a score bar, not as a
  depicted act.
- Sexuality, nudity: none.
- Language: none.
- Controlled substances: none.
- **Gambling**: there is a loot mechanic. The daily wheel and the pet summon give random rewards, and the summon
  can be paid for with orbes bought with real money. Declare **yes** to "randomised items (loot boxes)". The
  odds are shown in the game (4% whole pet per summon, guaranteed within 40), which is what Play requires.
- Users interact: **yes**. Guild chat and friend challenges.
- Shares location: no.
- Shares personal information: the player name is visible to other players in leaderboards, guilds and duels.
- Digital purchases: **yes**.

Expected rating: PEGI 3 / ESRB Everyone, with the in-game-purchases and users-interact notices.

---

## Data safety form

Declare exactly this. A wrong declaration here is what gets an app pulled.

**Collected, linked to the user, not shared with third parties:**

| Type | Why | Required? |
|---|---|---|
| Email address | Google Sign-In account | Optional (the game plays offline) |
| User ID | the account itself | Required |
| Name (chosen player name) | leaderboards, guilds, duels | Required |
| Purchase history | granting what was bought | Required |
| In-app actions, app interactions | balancing and anti-cheat (the server re-simulates every match) | Required |
| Crash logs and diagnostics | the client sends its own errors to the game's server | Required |

**Not collected:** location, contacts, photos, files, messages outside guild chat, health, financial information
(payments are handled by Google Play Billing and never reach this server), advertising ID.

**Security:** data is in transit over HTTPS. Account deletion: say that the player can ask at
abdennadherahmed975@gmail.com. Play requires a deletion route; an in-app button is better and is not built yet.

---

## What is left, and who has to do it

**Only you can do these.** They need your identity, your card and your signature, and I am not able to act on
them for you:

1. Create the developer account at https://play.google.com/console/signup, personal type.
2. Accept the Developer Distribution Agreement.
3. Pay the one-time 25 USD registration fee.
4. Pass identity verification: an identity document, your address, and a phone number. Google takes between a
   few hours and a few days.

**Then, with the account open**, the listing above is copy and paste, and the release itself needs:

5. An **Android App Bundle** (.aab), not an APK. Play has not accepted APKs for new apps since August 2021.
6. **Play App Signing**: let Google hold the signing key. The upload key is the keystore already in the CI
   secrets.
7. A **closed test with at least 12 testers for 14 days** before a personal developer account may go to
   production. This is the long pole: it starts the day the account exists, so open the account first and sort
   the listing while the fourteen days run.
