# CRUSH ROYALE - GAME DESIGN DOCUMENT (COMPLET)

**Version:** 1.0  
**Date:** September 2026  
**Engine:** Unity (C#)  
**Platform:** Android  
**Target:** Full Game (1000+ stages, all systems)

---

## TABLE OF CONTENTS
1. [High-Level Overview](#high-level-overview)
2. [Core Gameplay Systems](#core-gameplay-systems)
3. [Game Modes](#game-modes)
4. [Story & Narrative](#story--narrative)
5. [Power-Ups System](#power-ups-system)
6. [Trophy & Ranking System](#trophy--ranking-system)
7. [Cascade System](#cascade-system)
8. [Monetization](#monetization)
9. [Social & Guilds](#social--guilds)
10. [Achievements & Collections](#achievements--collections)
11. [Anti-Cheat System](#anti-cheat-system)
12. [Technical Architecture](#technical-architecture)
13. [UI/UX Flow](#uiux-flow)

---

## HIGH-LEVEL OVERVIEW

**Game Title:** CRUSH ROYALE  
**Genre:** Match-3 PVP Multiplayer  
**Core Loop:** Match pieces → Gain score → Compete in PVP → Climb ranks → Unlock features

### Key Pillars
- ✅ **Casual but Competitive:** Easy to learn, hard to master
- ✅ **Social:** Friends, Guilds, World Map
- ✅ **Long-term Progression:** 1000+ stages, endless content
- ✅ **Fair Monetization:** VIP benefits, not pay-to-win

---

## CORE GAMEPLAY SYSTEMS

### 1. Board & Mechanics

**Board Size:** 8x8 grid  
**Piece Types:** 6 colors (Red, Blue, Green, Yellow, Purple, Orange)

**Matching Rules:**
- **Match 3:** 3 pieces same color in line (horizontal/vertical) = 3 points + 1 bonus piece
- **Match 4:** 4 pieces same color = 4 pieces removed + bonus object generated
- **Match 5:** 5 pieces same color = all pieces of that color removed + super bonus

**Mechanics:**
```
- Gravity: Pieces fall down after matches
- Cascades: When pieces fall and create new matches automatically
- Time/Moves Limit: Each stage has TIME + MOVE LIMIT (e.g., 60 sec + 25 moves)
- Difficulty Scaling: % difficulty increases progressively (0-100%)
```

### 2. Board Generation (INTELLIGENT)

**Algorithm:**
- Generate board that guarantees 3-4 possible opening moves
- Vary difficulty by adjusting match probability
- At 100% difficulty: minimal matches available, maximum strategy required

**Pseudo-code:**
```csharp
GenerateBoard(difficultyPercent) {
    board = new Piece[8,8]
    do {
        board = FillRandomly()
    } while (CountPossibleMatches(board) < (4 - difficultyPercent/100 * 3))
    return board
}
```

### 3. Lives/Stamina System

**Initial State:**
- Player starts with 2 free lives

**Recharge Rules:**
- Lives 1-2: Can buy with Coins (standard price)
- Lives 3+: Must buy with Orbes Magiques
- Each consecutive purchase costs 25% more (fixed malus)
- VIP 10 gets 1 free daily continue

**Example:**
```
Orbe cost progression:
Life 3: 100 orbes
Life 4: 125 orbes (100 * 1.25)
Life 5: 156 orbes (125 * 1.25)
...
```

---

## GAME MODES

### Mode 1: Story Campaign (Local)

**Total Stages:** ~1000+  
**Structure:**
- 5 Acts (200 stages each)
- 10 chapters per act (20 stages per chapter)
- 2 bosses per chapter (mini-boss + final boss)

**Stage Unlocks:**
- Stage 10: First ads appear (every 2 wins)
- Stage 15: Shop unlocked
- Stage 25: PVP mode unlocked
- Level X: Guilds unlocked (TBD which stage)

**Boss Mechanics:**
- Boss stages have special board patterns
- Bosses have HP bars that decrease with player score
- Multi-phase bosses (defeat multiple waves)
- Unique cinematics for major bosses

### Mode 2: PVP Ranked (Ghost Play)

**Unlock:** Stage 25  
**Gameplay:** Asynchronous real-time
- Both players play simultaneously
- Each player sees opponent's ghost (replay of their moves)
- Winner = highest score after same time limit

**Leagues:** Bronze → Silver → Gold → Platinum → Diamond → Master  
**Reset:** Every Sunday at midnight UTC  
**Trophies:** Dynamic points based on MMR matching

**Matchmaking Logic:**
```
opponent_trophies = player_trophies ± (random * 10% ± win_streak_bonus)
If trophy_diff > 200: Give extra points
If trophy_diff < 50: Reduce points (anti-boosting)
```

### Mode 3: Guilds

**Unlock:** After certain story progression  
**Max Members:** 20 players  
**Guild Levels:** 20 levels via donations
- Level 1: 1st donation with Coins (standard price)
- Levels 2-20: Donations with Orbes (increasing cost)

**Guild Ranking:**
- Based on sum of all members' trophies
- Weekly leaderboard
- Rewards distributed Sundays based on final ranking

**Guild Boss Fight (Weekly):**
- Occurs 1x per week
- All 20 members must contribute attacks
- Cumulative score required to defeat boss
- Progressive difficulty (Boss 1 easy → Boss N very hard)
- Rewards scale with boss difficulty

---

## STORY & NARRATIVE

### Universe: Crystalheim

**5 Kingdoms:**
1. **North Kingdom** (Ice realm) - Acts 1
2. **East Kingdom** (Fire realm) - Act 2
3. **West Kingdom** (Nature realm) - Act 3
4. **South Kingdom** (Underground realm) - Act 4
5. **Central Kingdom** (Golden capital) - Act 5

### Main Antagonist: VALDORAX THE CORRUPTED

- Former god protector, corrupted by ancient curse
- Seeks revenge on humans for being forgotten
- Controls elemental magic, summons creatures
- Final boss at end of Act 5

### Hero Character

**At Launch:** Player chooses between male/female hero
- Customizable avatar (appearance, name)
- Motivation: Save kingdom from Valdorax's curse
- Character arc: Gets progressively corrupted by malédiction

### Allied Characters (6-8)

1. **LYRA** (Joins ~Stage 50)
   - Role: Elemental Mage
   - Backstory: Last priestess of destroyed Valdorax temple, seeks redemption
   - Personality: Mysterious, educated, harbors guilt
   - Character Arc: Major plot twist in Act 2

2. **KAEL** (Joins ~Stage 80)
   - Role: Knight Warrior
   - Backstory: Royal knight whose city was destroyed
   - Personality: Honorable, impulsive, fights for justice

3. **MIRA** (Joins ~Stage 250)
   - Role: Rogue/Scout
   - Backstory: War orphan turned thief
   - Personality: Sarcastic, hidden empathy
   - Character Arc: Dies heroically in Act 3

4. **THORIN** (Joins ~Stage 280)
   - Role: Paladin/Healer
   - Backstory: Pacifist priest forced into war
   - Personality: Wise, kind, naive
   - Character Arc: Sacrifices himself (dies at ~Stage 520)

5. **ZARA** (Joins ~Stage 500)
   - Role: Ranger/Hunter
   - Backstory: Sole survivor of nomadic tribe
   - Personality: Fierce, independent, instinctive

6. **ELIAN** (Joins ~Stage 550)
   - Role: Shaman/Druid
   - Backstory: Guardian of nature, refuses destruction
   - Personality: Calm, observant, enigmatic

7. **MARK** (Joins ~Stage 750, OPTIONAL)
   - Role: Assassin/Rogue
   - Backstory: Former agent of Valdorax, turncoat
   - Personality: Cynical, humorous, seeking redemption
   - Character Arc: Plot twist - revealed as spy

8. **SOREN** (Joins ~Stage 800, OPTIONAL)
   - Role: Enchanter/Alchemist
   - Backstory: Scientist who discovered Valdorax's secret
   - Personality: Obsessive, brilliant, morally conflicted

### Story Beats

**Act 1: The Call (0-200)**
- Hero discovers the curse spreading
- Encounters Lyra + Kael
- First boss: Corrupted Ice Creature

**Act 2: The Rebellion (200-400)**
- Gains Mira + Thorin
- First major defeat reveals enemy's power
- Boss: Black Knight of Valdorax

**Act 3: The Secrets (400-600)**
- Uncovers Valdorax's backstory
- Gains Zara + Elian
- Team morale low (character death - THORIN sacrifices)
- Boss: Elemental Avatar of Valdorax

**Act 4: The Preparation (600-800)**
- Quest for ancient relic to defeat god
- Gains Mark + Soren
- Major plot twist: Mark revealed as spy (MIRA dies protecting)
- Training montage
- Boss: Valdorax's General (seemingly unbeatable)

**Act 5: The Reckoning (800-1000+)**
- March toward Valdorax
- Epic confrontations (mini-bosses)
- Final revelation: Hero gets corrupted by curse
- **Final Boss: VALDORAX THE CORRUPTED**

### Ending Choices

**Ending A: Sacrifice (Dark)**
- Hero sacrifices self to seal Valdorax
- Kingdom saved but hero is gone
- Bittersweet ending

**Ending B: Corruption (Grey)**
- Hero absorbs Valdorax's power
- Becomes new antagonist (NG+ story)
- Kingdom questioning new "ruler"

**Ending C: Redemption (Happy-ish)**
- Allies purify hero before corruption spreads
- Hero survives but forever changed
- Happy-ish ending (ambiguous future)

---

## POWER-UPS SYSTEM

### 9 Power-Ups (3 Rarity Tiers)

#### TIER 1 - COMMON (Unlocked Stage 1, available always)
1. **Chrono Bomb** → +20 sec to timer
   - Price: 50 Coins
   - Cooldown: None

2. **Coin Booster** → +50% score for 20 sec
   - Price: 60 Coins
   - Cooldown: None

3. **Bright Spark** → Match 4 auto-generates 1 bonus piece
   - Price: 70 Coins
   - Cooldown: None

#### TIER 2 - RARE (Unlocked Silver League)
4. **Multiplier x2** → All scores x2 for 15 sec
   - Price: 200 Coins
   - Cooldown: 1x per match

5. **Golden Chain** → Destroys column + row on first match
   - Price: 180 Coins
   - Cooldown: 1x per match

6. **Freezing Gel** → Freezes opponent board for 10 sec (PVP only)
   - Price: 220 Coins
   - Cooldown: 1x per match

#### TIER 3 - EPIC (Unlocked Gold League)
7. **Nuclear Bomb** → Explodes 5x5 zone (huge damage)
   - Price: 500 Coins
   - Cooldown: 1x per match

8. **Fire Storm** → Burns all red + orange pieces on board
   - Price: 480 Coins
   - Cooldown: 1x per match

9. **Cascade Infinity** → Double cascade points for 30 sec
   - Price: 550 Coins
   - Cooldown: 1x per match

### Rare Power-Up (Monthly Drop)
- Unlocked after ~1 month of play (2h/day)
- Can be purchased with real money
- Or auto-unlocked when reaching VIP 10
- Grants permanent bonus in matches

---

## TROPHY & RANKING SYSTEM

### Trophy Calculation

**Victory:**
```
If opponent_mmr > player_mmr by X%:
    trophies_gained = 40 + (X * 0.5)
Else if opponent_mmr ≈ player_mmr:
    trophies_gained = 25
Else:
    trophies_gained = 10 (prevents smurfing)

Win_streak_bonus = current_win_streak * 5 (max +20)
total_trophies = trophies_gained + win_streak_bonus
```

**Defeat:**
```
If opponent_mmr > player_mmr by X%:
    trophies_lost = 5 (small penalty)
Else if opponent_mmr ≈ player_mmr:
    trophies_lost = 15
Else:
    trophies_lost = 35 (harsh penalty for losing to weaker - anti-boosting)

total_trophies = player_trophies - trophies_lost
```

### League System

```
Bronze: 0-299 trophies
Silver: 300-699 trophies
Gold: 700-1199 trophies
Platinum: 1200-1799 trophies
Diamond: 1800-2499 trophies
Master: 2500+ trophies
```

### Seasonal Reset
- Every Sunday at midnight UTC
- Top 100 per league get special rewards
- Rewards scale: Bronze < Silver < Gold < Platinum < Diamond < Master

---

## CASCADE SYSTEM

### Cascade Mechanics

**Level 1 Cascade:**
- Multiplier: 1x (base)
- Condition: Natural fall + automatic match

**Level 2 Cascade:**
- Multiplier: 1.5x
- Condition: 2nd consecutive cascade

**Level 3 Cascade:**
- Multiplier: 2x
- Condition: 3rd consecutive cascade

**Level 4+ Cascade (MEGA CASCADE):**
- Multiplier: 3x
- Condition: 4+ consecutive cascades
- Special animation triggered

**Scoring Example:**
```
Match 1: 100 points
Cascade 1: 100 * 1.5 = 150 points
Cascade 2: 150 * 2 = 300 points
Cascade 3: 300 * 3 = 900 points
TOTAL: 1350 points (from cascades!)
```

### Cascade Bonus
- Each cascade adds +10% score stacking
- Example: 4 cascades = +40% to final score

---

## COMBO METER SYSTEM (NEW MECHANIC)

### Red Surge Mode

**Meter Fills On:**
- Match 3: +10%
- Match 4: +20%
- Match 5: +35%
- Each Cascade: +50%

**When Full (100%):**
- RED SURGE MODE activates for 20 seconds
- All moves scored x2 automatically
- Meter resets after mode ends
- Visual/audio feedback for hype moments

---

## MONETIZATION

### Soft Currency: COINS

**Earn:**
- Completing story stages (scales with difficulty)
- PVP rewards (based on trophy rank)
- Daily login bonuses
- Events
- Achievement rewards

**Use:**
- Buy basic power-ups
- Recharge first 2 lives
- Guild donations (first one)
- Battle Pass progression

### Hard Currency: ORBES MAGIQUES (Premium)

**Earn:**
- Weekly/monthly ranking rewards (small amount)
- Every 50 story stages (+10 orbes)
- PVP rank-up bonus
- Special events

**Use:**
- Buy powerful power-ups
- Recharge lives (3+)
- Refresh shop items
- Guild donations (levels 2-20)
- Continue after defeat
- Battle Pass premium track

**Buy with Real Money:**
```
Pack 1: 74 orbes = 0.99€
Pack 2: 385 orbes = 4.99€ (+ 10 bonus = 395)
Pack 3: 965 orbes = 12.99€ (+ 35 bonus = 1000)
Pack 4: 2100 orbes = 24.99€ (+ 100 bonus = 2200)
Pack 5: 5250 orbes = 49.99€ (+ 250 bonus = 5500)
Pack 6: 11000 orbes = 99.99€ (+ 1000 bonus = 12000)
Mega: 27500 orbes = 199.99€ (+ 2500 bonus = 30000)

Buy same pack 3x = +20% bonus orbes
```

### VIP System (Cumulative Spending)

```
VIP 1  (1.50€):  +15% coins/stage
VIP 2  (5€):     +15% coins + 1 free life/day
VIP 3  (20€):    +20% coins + basic cosmetics
VIP 4  (50€):    +25% coins + speedup x1.2
VIP 5  (100€):   +30% coins + rare cosmetics
VIP 6  (500€):   +35% coins + cosmetics + 2x event rewards
VIP 7  (1000€):  +40% coins + 5% orbes gains + skip 1 ad/day
VIP 8  (2000€):  +45% coins + 10% orbes gains + continue 1x/day
VIP 9  (5000€):  +50% coins + 15% orbes gains + steal power-up 60%
VIP 10 (10000€): +60% coins + 20% orbes gains + rare power-up unlock + steal 75%
```

### Ads & Premium Pass

**Remove Ads Pack:** 5€ (lifetime, all ads removed)

**Ad Placement:**
- Story: Every 2 wins (starting stage 10)
- PVP: Every 3 battles

**Ads are Optional:** Never forced (only after X wins/losses)

### Battle Pass (Seasonal)

**Free Track:** Rewards available to everyone
**Premium Track:** Extra rewards (paid with Orbes/Real Money)
**Duration:** 1 season = 4 weeks
**Rewards:** Coins, Orbes, Cosmetics, Power-ups

---

## SOCIAL & GUILDS

### Friends System

**Features:**
- Add friends by ID or search
- Challenge friends in PVP (no trophy risk)
- Friend challenges use 100% of friend's power-ups
- Cooldown: 1 challenge per 5 minutes (anti-spam)
- View friend profiles with avatars/stats

### World Map

**Visual:**
- Progression map showing current stage
- Friends visible at their respective positions
- Interactive (tap to view profile)
- Real-time updates

### Guild System

**Creation:**
- Available after certain story progression
- Max 20 members
- Guild leader (guild master)
- Officer roles (invite/kick permissions)

**Guild Levels:**
```
Level 1: Base (0 donations)
Level 2-20: Upgraded by donations
Donation 1: Coins (standard price)
Donations 2-20: Orbes (increasing cost)
```

**Guild Tech Tree:**
- Unlock passive bonuses as guild levels up
- Example: +5% coin bonus for all members
- Building slots (guild perks)

**Guild Ranking:**
- Sum of all members' PVP trophies
- Weekly leaderboard
- Rewards: Coins + Orbes distributed to all members
- Rewards vary by league (Bronze < Master)

**Guild Boss (Weekly Event):**
- Spawns once per week
- All members participate
- Score accumulation (each member's score counts)
- Difficulty scaling:
  - Boss 1: Easy (1-2 players can solo if high score)
  - Boss 2-5: Progressive difficulty
  - Boss 10+: Requires guild effort
- Rewards scale with boss difficulty

---

## ACHIEVEMENTS & COLLECTIONS

### Achievement Types

**Story-based:**
- Reach stage 100, 500, 1000
- Defeat specific boss
- Complete act X

**Gameplay-based:**
- 100 wins in PVP
- 50 cascades triggered
- Reach Diamond league

**Collection-based:**
- Collect 10 unique cosmetics
- Complete power-up set

### Collection Book

**Structure:**
- Pages of 5-10 achievements each
- Completion rewards: +5% per page (stacking)
- Visual progress bar per page
- Unlock cosmetics as bonus

**Example:**
```
Page 1 (5 achievements): +5% coins bonus
Page 2 (5 achievements): +10% coins bonus (total)
Page 3 (5 achievements): +15% coins bonus (total)
```

---

## ANTI-CHEAT SYSTEM

### Detection Mechanisms

1. **Pattern Recognition:**
   - Flag impossible scores (e.g., max score every game)
   - Unusual win rates (+95% = suspicious)
   - Sudden skill spikes

2. **Server-Side Validation:**
   - All PVP scores validated on server
   - Move-by-move validation
   - Timestamp verification (no time-travel exploits)

3. **Cooldown Anti-Spam:**
   - Action rate-limiting
   - Minimum time between moves (e.g., 200ms)

4. **Device Fingerprinting:**
   - Flag multiple accounts per device
   - Monitor account behavior patterns

### Punishment System

```
First Offense: Warning + temp suspension (1 day)
Second Offense: Temp ban (7 days) + trophy reset
Third Offense: Permanent ban
Severe (Hacking): Immediate permaban
```

---

## TECHNICAL ARCHITECTURE

### Engine: Unity (C#)

**Project Structure:**
```
Assets/
├── Scripts/
│   ├── Core/
│   │   ├── GameManager.cs
│   │   ├── BoardManager.cs
│   │   ├── TrophySystem.cs
│   ├── Gameplay/
│   │   ├── Match3Logic.cs
│   │   ├── CascadeCalculator.cs
│   │   ├── PowerUpManager.cs
│   ├── Networking/
│   │   ├── FirebaseManager.cs
│   │   ├── GooglePlayGames.cs
│   │   ├── ServerAPI.cs
│   ├── UI/
│   │   ├── MainMenuUI.cs
│   │   ├── GameplayUI.cs
│   │   ├── ShopUI.cs
│   ├── Social/
│   │   ├── FriendsManager.cs
│   │   ├── GuildManager.cs
│   ├── Story/
│   │   ├── StoryManager.cs
│   │   ├── DialogueSystem.cs
├── Prefabs/
├── Scenes/
├── Assets/
│   ├── Sprites/
│   ├── Audio/
│   ├── Fonts/
```

### Backend Architecture

**Cloud Services:**
- **Authentication:** Google Play Games + Firebase Auth
- **Real-time Database:** Firebase Realtime DB (player data, chat)
- **Game State Server:** Custom Node.js/Python backend
  - Stores: Trophies, match results, guild data
  - Validates: PVP scores, anti-cheat
  - Distributes: Rewards, leaderboards

**API Endpoints (REST):**
```
POST /auth/login
GET /player/{id}/stats
POST /pvp/match/result
GET /leaderboard/weekly
POST /guild/create
POST /shop/purchase
GET /story/{stageId}
POST /achievement/unlock
```

### Local Save Data Structure

```csharp
public class PlayerData {
    public string playerId;
    public int currentStage;
    public int coins;
    public int orbes;
    public int vipLevel;
    public Dictionary<int, bool> achievementProgress;
    public int[] guildBossData;
    public DateTime lastLoginTime;
}
```

---

## UI/UX FLOW

### Main Menu
- **Hero Selection:** Male/Female (first time only)
- **Login:** Google Play Games or Firebase
- **Quick Play Buttons:** Story, PVP, Shop, Guild

### Story Mode
- **World Map:** Progression visualization
- **Stage Select:** View difficulty, boss info
- **Pre-Game:** Power-up selection, lives check
- **Gameplay:** Board, score, timer/moves, power-up usage
- **Post-Game:** Win/lose screen, rewards, next stage unlock

### PVP Mode
- **Matchmaking:** Search for opponent
- **vs Screen:** Show opponent profile, power-ups
- **Gameplay:** Ghost play, real-time opponent moves
- **Results:** Winner screen, trophy gains/losses

### Shop
- **Power-ups:** Display by category
- **Items:** Coins, Orbes, cosmetics
- **Refresh:** Daily items, paid refresh option

### Guild Screen
- **Guild Info:** Name, members, level, ranking
- **Members List:** Leaderboard within guild
- **Chat:** Guild-wide chat with moderation
- **Boss Fight:** Weekly event, damage contribution

### Achievements
- **Book:** Pages of achievements
- **Progress:** Visual bars, rewards
- **Cosmetics:** Previews of unlocked items

---

## MONETIZATION SUMMARY

**Revenue Streams:**
1. Orbes Packs (Primary) - ~60% revenue
2. VIP System (Secondary) - ~25% revenue
3. Battle Pass Premium (Tertiary) - ~10% revenue
4. Remove Ads Pack - ~5% revenue

**F2P Friendly:** Yes
- All content accessible without paying
- Progression slower for F2P but achievable
- No gameplay-critical purchases

---

## FINAL NOTES

**Polish Requirements:**
- Smooth animations (cascades especially)
- Sound design (matching, victory, defeat)
- Haptic feedback (mobile vibration)
- Localization: FR, AR, EN, ES, DE, IT, PT, RU, JA, KO, ZH

**Launch Checklist:**
- [ ] All systems tested + balanced
- [ ] Story content finalized (50+ chapters)
- [ ] Backend server live
- [ ] Anti-cheat verified
- [ ] Google Play Store submission
- [ ] Facebook SDK (optional social login)
- [ ] Push notification system live

---

**End of Game Design Document**
