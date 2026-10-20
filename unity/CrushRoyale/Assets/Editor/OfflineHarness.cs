using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Game;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.Screens;
using CrushRoyale.Game.UI;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>One screen to build: its file name, its type and the argument <see cref="UIRoot.Show"/> would pass.</summary>
    public sealed class ScreenCase
    {
        public ScreenCase(string name, Type type, Func<GameRoot, object> args = null, bool withProfile = true, Action<GameRoot> overlay = null)
        {
            Name = name;
            Type = type;
            Args = args;
            WithProfile = withProfile;
            Overlay = overlay;
        }

        public string Name { get; }

        public Type Type { get; }

        /// <summary>Built lazily: a fake match or result costs a headless simulation.</summary>
        public Func<GameRoot, object> Args { get; }

        /// <summary>False captures the empty/offline state of the screen (no profile at all).</summary>
        public bool WithProfile { get; }

        /// <summary>Optional extra layer to put on top once the screen is built (dialog, popup).</summary>
        public Action<GameRoot> Overlay { get; }

        /// <summary>Names the test case in the test runner, which otherwise repeats the type name for every screen.</summary>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Runs the real client services in the editor, without play mode and without a server: the screenshot tool and the
    /// UI tests both build screens exactly as <see cref="UIRoot"/> does, on top of a fake profile.
    /// </summary>
    public static class OfflineHarness
    {
        /// <summary>
        /// Creates the service hub. Edit mode never sends Awake to a component added from script, so it is invoked by
        /// hand; Start is deliberately skipped (it shows the splash through a coroutine, which edit mode cannot run).
        /// </summary>
        public static GameRoot BootGame()
        {
            if (GameRoot.Instance != null)
            {
                return GameRoot.Instance;
            }
            var go = new GameObject("CrushRoyale");
            GameRoot game = go.AddComponent<GameRoot>();
            MethodInfo awake = typeof(GameRoot).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
            if (awake == null)
            {
                throw new InvalidOperationException("GameRoot.Awake not found: the harness must be updated.");
            }
            awake.Invoke(game, null);
            // The screenshots and the test expectations are written in English whatever the machine's language is.
            game.Loc.SetLanguage("en");
            return game;
        }

        /// <summary>
        /// Destroys the whole hub so the next test starts clean. Edit mode never sends OnDestroy either, so the
        /// backend is disposed and the static instance cleared by hand.
        /// </summary>
        public static void Shutdown(GameRoot game)
        {
            if (game == null)
            {
                return;
            }
            game.Backend.OfflineProfile = null;
            game.Backend.Dispose();
            UnityEngine.Object.DestroyImmediate(game.gameObject);
            typeof(GameRoot).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null });
        }

        /// <summary>Where tools/ProfileSnapshot drops a real account, when a build was given one.</summary>
        private const string FixturesFolder = "Assets/Editor/Fixtures";

        private const string SnapshotPath = FixturesFolder + "/profile.json";

        public static void SetProfile(GameRoot game, bool withProfile)
        {
            game.Backend.OfflineProfile = withProfile ? (Snapshot() ?? DemoProfile()) : null;
            game.Backend.OfflineShop = withProfile ? DemoShop() : null;
            game.Backend.OfflineFixtures.Clear();
            if (withProfile)
            {
                // Chaque ecran qui demande ses donnees au serveur restait sur « Loading » dans les captures, et
                // ses defauts ne pouvaient donc etre trouves que par un joueur, sur un telephone, apres un build.
                // Le Battle Pass, les quetes, les hauts faits et le classement en font partie.
                game.Backend.OfflineFixtures[typeof(BattlePassResponse)] = DemoBattlePass();
                game.Backend.OfflineFixtures[typeof(QuestsResponse)] = DemoQuests();
                game.Backend.OfflineFixtures[typeof(AchievementsResponse)] = DemoAchievements();
                game.Backend.OfflineFixtures[typeof(LeaderboardResponse)] = DemoLeaderboard();
                game.Backend.OfflineFixtures[typeof(GuildDto)] = DemoGuild();
                game.Backend.OfflineFixtures[typeof(GuildSearchResponse)] = DemoGuildSearch();
                game.Backend.OfflineFixtures[typeof(FriendsResponse)] = DemoFriends();
                game.Backend.OfflineFixtures[typeof(PlayerStatsDto)] = DemoStats();
                // Et si le compte reel a ete releve, il remplace la demonstration, ecran par ecran.
                LoadSnapshotFixtures(game);
            }
        }

        /// <summary>
        /// Remplace les donnees inventees par celles du compte releve, pour les ecrans qui en ont une.
        ///
        /// Le fichier porte le nom du type de reponse, parce que c'est ainsi que le harnais sert une donnee a un
        /// ecran : l'ecran demande un BattlePassResponse, il recoit celui du compte. Un ecran sans fichier garde
        /// sa demonstration, ce qui laisse les captures completes meme quand le releve n'a pas tout ramene.
        /// </summary>
        private static void LoadSnapshotFixtures(GameRoot game)
        {
            Type[] types =
            {
                typeof(BattlePassResponse), typeof(QuestsResponse), typeof(AchievementsResponse),
                typeof(LeaderboardResponse), typeof(GuildDto), typeof(GuildSearchResponse),
                typeof(FriendsResponse), typeof(PlayerStatsDto)
            };
            foreach (Type type in types)
            {
                string path = FixturesFolder + "/" + type.Name + ".json";
                if (!System.IO.File.Exists(path))
                {
                    continue;
                }
                try
                {
                    object value = Newtonsoft.Json.JsonConvert.DeserializeObject(
                        System.IO.File.ReadAllText(path), type, CrushRoyale.Client.JsonSettings.Default);
                    if (value != null)
                    {
                        game.Backend.OfflineFixtures[type] = value;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Fixture " + type.Name + " unreadable: " + ex.Message);
                }
            }
        }

        private static PublicProfileDto Player(string id, string name, int trophies, string league, int stage) =>
            new PublicProfileDto
            {
                Id = id, DisplayName = name, Trophies = trophies, League = league, HighestStage = stage
            };

        /// <summary>Une guilde en milieu de vie : des membres a tous les rangs, un boss entame, une course en cours.</summary>
        private static GuildDto DemoGuild()
        {
            string[] names = { "Demo Hero", "Lyra", "Kael", "Mira", "Thorin", "Zara", "Elian", "Soren" };
            string[] roles = { "Leader", "Officer", "Officer", "Member", "Member", "Member", "Member", "Member" };
            var members = new List<GuildMemberDto>();
            for (int i = 0; i < names.Length; i++)
            {
                members.Add(new GuildMemberDto
                {
                    PlayerId = i == 0 ? "offline-player" : "p" + i,
                    DisplayName = names[i],
                    Role = roles[i],
                    Trophies = 2400 - i * 160,
                    DonatedCoins = 9000 - i * 700,
                    DonatedOrbes = 40 - i * 4,
                    CoinsDonatedToday = i < 3 ? 500 : 0,
                    BossAttacksLeft = i % 3,
                    BossDamage = 120000 - i * 9000,
                    TournamentPoints = 3200 - i * 260,
                    JoinedAtUnixMs = InDays(-30 + i * 3)
                });
            }
            return new GuildDto
            {
                Id = 1, Name = "Crystal Wardens", Description = "On joue tous les soirs, boss le week-end.",
                Level = 7, DonationProgress = 18400, NextLevelCurrency = "coins", NextLevelCost = 25000,
                CoinsPerPoint = 100, DailyCoinCap = 2000, TechPoints = 12,
                Tech = new Dictionary<string, int> { { "coins", 3 }, { "lives", 2 }, { "xp", 1 } },
                Members = members, IsOpen = true, MinTrophies = 800, TotalTrophies = 14200, Rank = 42,
                Boss = new GuildBossDto
                {
                    Week = 3, BossIndex = 2, MaxHp = 1200000, Damage = 740000,
                    Defeated = false, ResetAtUnixMs = InDays(3)
                },
                Tournament = new GuildTournamentDto
                {
                    Points = 18600, MyPoints = 3200, MemberCap = 5000, Ranked = true,
                    MinMembers = 5, MembersMissing = 0, LastPoints = 15400, LastRank = 7,
                    FirstPrizeCoins = 12000, FirstPrizeOrbes = 300, Rank = 4,
                    Board = new List<GuildRaceEntryDto>
                    {
                        new GuildRaceEntryDto { Rank = 1, GuildId = 9, Name = "Emberfall Lords", Points = 31200, Members = 20 },
                        new GuildRaceEntryDto { Rank = 2, GuildId = 8, Name = "Sunspire Blades", Points = 27400, Members = 19 },
                        new GuildRaceEntryDto { Rank = 3, GuildId = 7, Name = "Verdant Pact", Points = 21050, Members = 18 },
                        new GuildRaceEntryDto { Rank = 4, GuildId = 1, Name = "Crystal Wardens", Points = 18600, Members = 8, Mine = true },
                        new GuildRaceEntryDto { Rank = 5, GuildId = 6, Name = "Frostreach Kin", Points = 16900, Members = 14 }
                    }
                }
            };
        }

        /// <summary>La liste des guildes qui recrutent : pleines, ouvertes, exigeantes, pour voir chaque cas de ligne.</summary>
        private static GuildSearchResponse DemoGuildSearch() => new GuildSearchResponse
        {
            Guilds = new List<GuildSummaryDto>
            {
                new GuildSummaryDto { Id = 6, Name = "Frostreach Kin", Level = 9, Members = 14, TotalTrophies = 29800, IsOpen = true, MinTrophies = 0 },
                new GuildSummaryDto { Id = 7, Name = "Verdant Pact", Level = 6, Members = 18, TotalTrophies = 21400, IsOpen = true, MinTrophies = 1200 },
                new GuildSummaryDto { Id = 8, Name = "Sunspire Blades", Level = 11, Members = 20, TotalTrophies = 46300, IsOpen = false, MinTrophies = 2500 },
                new GuildSummaryDto { Id = 9, Name = "Les Gardiens", Level = 2, Members = 3, TotalTrophies = 2100, IsOpen = true, MinTrophies = 0 }
            }
        };

        /// <summary>Des amis, une demande recue, une envoyee, un cadeau de vie et un duel dans chaque etat.</summary>
        private static FriendsResponse DemoFriends() => new FriendsResponse
        {
            Friends = new List<PublicProfileDto>
            {
                Player("p1", "Lyra", 2240, "Gold", 54),
                Player("p2", "Kael", 1890, "Silver", 41),
                Player("p3", "Mira", 980, "Bronze", 22)
            },
            Incoming = new List<PublicProfileDto> { Player("p4", "Thorin", 1450, "Silver", 33) },
            Outgoing = new List<PublicProfileDto> { Player("p5", "Zara", 2610, "Gold", 61) },
            Suggestions = new List<PublicProfileDto>
            {
                Player("p6", "Elian", 760, "Bronze", 15),
                Player("p7", "Soren", 3120, "Diamond", 78)
            },
            LifeGifts = new List<PublicProfileDto> { Player("p1", "Lyra", 2240, "Gold", 54) },
            CanSendLife = true,
            CanAcceptLife = true,
            Duels = new List<FriendDuelDto>
            {
                new FriendDuelDto { Id = "d1", OpponentId = "p2", OpponentName = "Kael", State = "Invited", Outcome = "None" },
                new FriendDuelDto { Id = "d2", OpponentId = "p1", OpponentName = "Lyra", State = "WaitingOpponent", Outcome = "None", MyScore = 24500 },
                new FriendDuelDto { Id = "d3", OpponentId = "p3", OpponentName = "Mira", State = "Finished", Outcome = "Won", MyScore = 31200, OpponentScore = 28700 }
            }
        };

        /// <summary>Le profil du joueur, assez rempli pour que chaque bloc de l'ecran ait quelque chose a montrer.</summary>
        private static PlayerStatsDto DemoStats() => new PlayerStatsDto
        {
            Profile = new PublicProfileDto
            {
                Id = "offline-player", DisplayName = "Demo Hero", Trophies = 2400, League = "Gold",
                HighestStage = 37, GuildId = 1, Frame = "frame_gold", Title = "title_champion"
            },
            PvpWins = 128, PvpLosses = 74, BestWinStreak = 11, HighestLeague = "Diamond",
            TotalStars = 96, AchievementsUnlocked = 23, WeeklyRank = 58,
            HeroGender = "female", VipTier = 3, Outfit = "outfit_frost",
            BoardSkin = "board_crystal", PieceSkin = "piece_royal",
            Cosmetics = new List<string>
            {
                "frame_gold", "frame_silver", "title_champion", "title_explorer",
                "outfit_frost", "outfit_ember", "board_crystal", "piece_royal"
            },
            CosmeticsCounted = 8, CosmeticCoinBonusPermille = 45, CosmeticPassXpBonusPermille = 30,
            Pet = "pet_fox", PetLevel = 4, PetsOwned = 3, CollectionPages = 2
        };

        private static long InDays(int days) =>
            CrushRoyale.Core.Common.TimeUtil.ToUnixMs(DateTime.UtcNow.AddDays(days));

        /// <summary>Un pass a mi-saison : des paliers pris, un palier en cours, et de quoi remplir l'ecran.</summary>
        private static BattlePassResponse DemoBattlePass()
        {
            var pass = new BattlePassResponse
            {
                Season = 3,
                Tier = 14,
                Xp = 2840,
                XpPerTier = 200,
                Premium = false,
                TierPriceOrbes = 120,
                SeasonEndUnixMs = InDays(11)
            };
            for (int tier = 1; tier <= 30; tier++)
            {
                pass.Tiers.Add(new BattlePassTierDto
                {
                    Tier = tier,
                    Free = new RewardDto { Coins = 250 * tier, Lives = tier % 5 == 0 ? 1 : 0 },
                    Premium = new RewardDto { Orbes = 20 + tier * 2, Coins = 400 * tier },
                    FreeClaimed = tier <= 12,
                    PremiumClaimed = false
                });
            }
            return pass;
        }

        /// <summary>Des quetes du jour dans les trois etats : finie et prise, finie a prendre, en cours.</summary>
        private static QuestsResponse DemoQuests()
        {
            var quests = new QuestsResponse { ResetAtUnixMs = InDays(1) };
            (string Id, string Type, int Target, int Progress, bool Claimed)[] rows =
            {
                ("quest.win3", "WinStages", 3, 3, true),
                ("quest.stars", "EarnStars", 12, 12, false),
                ("quest.coins", "SpendCoins", 400, 263, false),
                ("quest.duel", "WinDuels", 2, 0, false)
            };
            foreach ((string id, string type, int target, int progress, bool claimed) in rows)
            {
                quests.Quests.Add(new QuestDto { Id = id, Type = type, Target = target, Progress = progress, Claimed = claimed });
            }
            return quests;
        }

        /// <summary>Assez de hauts faits pour que la grille deborde et que ses lignes se mesurent.</summary>
        private static AchievementsResponse DemoAchievements()
        {
            var response = new AchievementsResponse { CoinBonus = 0.12f };
            response.PagesCompleted.Add(1);
            string[] types = { "StagesCleared", "GemsCleared", "PvpWins", "ChestsOpened", "PetsOwned", "CascadeMax" };
            for (int i = 0; i < 18; i++)
            {
                long target = 50L * (i + 1);
                response.Achievements.Add(new AchievementDto
                {
                    Id = "ach." + i,
                    Type = types[i % types.Length],
                    Page = 1 + i / 6,
                    Target = target,
                    Progress = i < 7 ? target : target / (2 + i % 3),
                    Unlocked = i < 7,
                    Claimed = i < 4
                });
            }
            return response;
        }

        /// <summary>Un classement ou le joueur n'est ni premier ni dernier : c'est la que la ligne se lit mal.</summary>
        private static LeaderboardResponse DemoLeaderboard()
        {
            var board = new LeaderboardResponse { League = "Crystal", Week = 42, ResetAtUnixMs = InDays(3) };
            string[] names = { "Nyx", "Valdoran", "Mira", "Kaelthas", "Sora", "Demo Hero", "Ilya", "Branwen", "Tariq", "Zhao", "Esme", "Ruben" };
            for (int i = 0; i < names.Length; i++)
            {
                board.Entries.Add(new LeaderboardEntryDto
                {
                    Rank = i + 1,
                    PlayerId = "p" + i,
                    DisplayName = names[i],
                    Trophies = 2400 - i * 90,
                    League = "Crystal"
                });
            }
            return board;
        }

        /// <summary>
        /// A real account, when the build was handed one.
        ///
        /// The invented demo player can only ever be in the states somebody thought to invent, and the defects that
        /// matter live in the states nobody thought of: the bag's Convert button broke precisely because its owner
        /// had collected every pet. When tools/ProfileSnapshot has written a profile, the captures are taken against
        /// that account instead; when it has not, nothing changes and the demo player is used.
        /// </summary>
        private static ProfileDto Snapshot()
        {
            try
            {
                if (!System.IO.File.Exists(SnapshotPath))
                {
                    return null;
                }
                ProfileDto profile = CrushRoyale.Client.JsonSettings.Deserialize<ProfileDto>(System.IO.File.ReadAllText(SnapshotPath));
                if (profile != null)
                {
                    Debug.Log("Screens captured against the account of " + profile.DisplayName + ".");
                }
                return profile;
            }
            catch (Exception ex)
            {
                // A bad snapshot must never cost the captures: fall back to the demo player and say why.
                Debug.LogWarning("Could not read " + SnapshotPath + ", using the demo profile: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// A full shop catalogue: the four tabs with the kind of item each one holds.
        ///
        /// The shop asks the server for its items, so it stayed on "Loading" in every capture and its grid was never
        /// once looked at. Two defects reported there were invisible to me for that single reason.
        /// </summary>
        public static ShopResponse DemoShop()
        {
            var items = new List<ShopItemDto>();
            string[] deals = { "NuclearBomb", "FireStorm", "CascadeInfinity" };
            for (int i = 0; i < deals.Length; i++)
            {
                items.Add(new ShopItemDto
                {
                    Id = "deal." + deals[i],
                    Kind = "PowerUp",
                    PowerUp = deals[i],
                    Quantity = 2 + i,
                    PriceCoins = 1800 + i * 900,
                    PriceOrbes = i == 1 ? 45 : 0,
                    IsDeal = i == 0,
                    DiscountPermille = i == 0 ? 400 : 0
                });
            }

            string[] powerUps = { "ChronoBomb", "CoinBooster", "BrightSpark", "Multiplier2x", "GoldenChain", "FreezingGel" };
            for (int i = 0; i < powerUps.Length; i++)
            {
                items.Add(new ShopItemDto
                {
                    Id = "powerup." + powerUps[i],
                    Kind = "PowerUp",
                    PowerUp = powerUps[i],
                    Quantity = 1 + i % 3,
                    PriceCoins = 900 + i * 350,
                    PriceOrbes = i % 2 == 0 ? 0 : 20 + i * 6,
                    IsDeal = i == 1,
                    DiscountPermille = i == 1 ? 300 : 0,
                    SoldOut = i == powerUps.Length - 1
                });
            }

            int[] orbes = { 74, 395, 1035, 2200, 5500, 12000 };
            int[] cents = { 99, 499, 1299, 2499, 4999, 9999 };
            for (int i = 0; i < orbes.Length; i++)
            {
                items.Add(new ShopItemDto
                {
                    Id = "orbes.pack" + (i + 1),
                    Kind = "OrbePack",
                    OrbesGranted = orbes[i],
                    PriceCents = cents[i],
                    Sku = "crushroyale.orbes.pack" + (i + 1),
                    OrbesPerEuro = (decimal)orbes[i] / (cents[i] / 100m)
                });
            }

            int[] coins = { 2500, 9000, 30000, 100000 };
            for (int i = 0; i < coins.Length; i++)
            {
                items.Add(new ShopItemDto
                {
                    Id = "coins.pack" + (i + 1),
                    Kind = "CoinPack",
                    CoinsGranted = coins[i],
                    PriceOrbes = 30 + i * 70
                });
            }

            string[] cosmetics = { "frame_gold", "board_frost", "pieces_runes", "title_crusher", "outfit_royal", "frame_ember" };
            for (int i = 0; i < cosmetics.Length; i++)
            {
                items.Add(new ShopItemDto
                {
                    Id = "cosmetic." + cosmetics[i],
                    Kind = "Cosmetic",
                    CosmeticId = cosmetics[i],
                    PriceOrbes = 180 + i * 90
                });
            }

            return new ShopResponse
            {
                Items = items,
                NextRefreshUnixMs = DateTimeOffset.UtcNow.AddHours(6).ToUnixTimeMilliseconds(),
                RefreshCostOrbes = 40,
                Wallet = new WalletDto { Coins = 12450, Orbes = 320 }
            };
        }

        /// <summary>A mid-game player: everything the hub, the shop and the map read is filled in.</summary>
        public static ProfileDto DemoProfile()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return new ProfileDto
            {
                Id = "demo-player",
                DisplayName = "Demo Hero",
                Hero = new HeroDto { Gender = "female", Name = "Demo Hero", Appearance = 1 },
                HeroChosen = true,
                Language = "en",
                Wallet = new WalletDto { Coins = 12450, Orbes = 320 },
                Lives = new LivesDto
                {
                    Lives = 4,
                    MaxRegen = 5,
                    RechargeSeconds = 1800f,
                    FreeContinues = 1,
                    NextLifeCoins = 900,
                    NextLifeOrbes = 0
                },
                Vip = new VipDto { Tier = 3, LifetimeSpendCents = 2999, NextThresholdCents = 4999, Progress = 0.6f },
                // Full enough to be offered: the state worth photographing is the one with the button in it.
                PiggyBank = new PiggyBankDto { Orbes = 420, Cap = 600, TimesBroken = 1, Offered = true, Sku = "crushroyale.piggybank", PriceCents = 499 },
                Pvp = new PvpDto
                {
                    Trophies = 1420,
                    League = "Gold",
                    HighestLeague = "Gold",
                    WinStreak = 3,
                    BestWinStreak = 7,
                    Wins = 58,
                    Losses = 31,
                    Draws = 2
                },
                Story = new StoryDto
                {
                    HighestUnlockedStage = 37,
                    TotalStars = 84,
                    StarsByStage = Stars(36),
                    Restoration = new RestorationDto { StarsAvailable = 12, StarsSpent = 30, CurrentZone = 2, CanBuild = true },
                    WinStreak = 3
                },
                Inventory = new InventoryDto
                {
                    PowerUps = new Dictionary<string, int>
                    {
                        [PowerUpType.ChronoBomb.ToString()] = 3,
                        [PowerUpType.BrightSpark.ToString()] = 2,
                        [PowerUpType.FireStorm.ToString()] = 1
                    },
                    Cosmetics = new List<string>(),
                    PremiumPass = true
                },
                LoginBonusAvailable = true,
                WheelAvailable = true,
                LoginCalendarSlot = 3,
                CollectionPagesCompleted = 2,
                UnclaimedAchievements = 2,
                ClaimableQuests = 1,
                Pets = new PetsDto
                {
                    // Every pet, in every state a card can be in: owned and equipped, owned and ready to awaken,
                    // owned plainly, half collected, barely started. The demo owned none of them, so the pet screen
                    // was an empty list in every capture and not one pet card was ever looked at -- which is exactly
                    // where a reported defect was hiding.
                    Pets = DemoPets(),
                    Equipped = "FrostFox",
                    PityPulls = 40,
                    UnlockFragments = 50,
                    WholePetOneIn = 25,
                    SummonCostOrbes = 150,
                    Summon10CostOrbes = 1350,
                    MaxLevel = 10,
                    PvpLevelCap = 5
                },
                Chests = new ChestsDto
                {
                    ServerNowUnixMs = now,
                    Slots =
                    {
                        new ChestSlotDto { Slot = 0, Type = "Gold", Status = "Ready", SecondsLeft = 0, UnlockSeconds = 28800 },
                        new ChestSlotDto { Slot = 1, Type = "Silver", Status = "Unlocking", SecondsLeft = 2400, UnlockSeconds = 10800, SkipCostOrbes = 12 },
                        new ChestSlotDto { Slot = 2, Type = "Wood", Status = "Locked", SecondsLeft = 0, UnlockSeconds = 3600 },
                        null
                    }
                }
            };
        }

        /// <summary>The five pets, each in a different state, so every branch of the pet card gets drawn.</summary>
        private static List<PetDto> DemoPets() => new List<PetDto>
        {
            Pet("FrostFox", owned: true, level: 7, fragments: 28, canAwaken: true),
            Pet("SunFennec", owned: true, level: 4, fragments: 9, canAwaken: false),
            Pet("ForestOwl", owned: true, level: 1, fragments: 0, canAwaken: false),
            Pet("EmberSalamander", owned: false, level: 0, fragments: 31, canAwaken: false),
            Pet("CrystalDrake", owned: false, level: 0, fragments: 4, canAwaken: false)
        };

        private static PetDto Pet(string type, bool owned, int level, int fragments, bool canAwaken) => new PetDto
        {
            Type = type,
            Owned = owned,
            Level = level,
            Xp = level * 100,
            XpForCurrent = level * 100,
            XpForNext = (level + 1) * 100,
            Fragments = fragments,
            CanAwaken = canAwaken,
            AwakenFragments = 20,
            AwakenCoins = 5000,
            PowerUp = "ChronoBomb"
        };

        /// <summary>"333...0": three stars on every cleared stage, the format the map reads (index = stage id - 1).</summary>
        private static string Stars(int cleared)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < cleared; i++)
            {
                builder.Append('3');
            }
            return builder.ToString();
        }

        /// <summary>Every screen of the game, with the arguments its Build needs.</summary>
        public static IReadOnlyList<ScreenCase> Screens()
        {
            return new List<ScreenCase>
            {
                new ScreenCase("01-Splash", typeof(SplashScreen), withProfile: false),
                new ScreenCase("02-HeroSelect", typeof(HeroSelectScreen), withProfile: false),
                new ScreenCase("03-MainMenu", typeof(MainMenuScreen)),
                new ScreenCase("04-MainMenu-Offline", typeof(MainMenuScreen), withProfile: false),
                new ScreenCase("05-MainMenu-Dialog", typeof(MainMenuScreen), overlay: ShowSampleDialog),
                new ScreenCase("06-WorldMap", typeof(WorldMapScreen)),
                new ScreenCase("07-StagePreview", typeof(StagePreviewScreen), _ => 7),
                new ScreenCase("08-Gameplay", typeof(GameplayScreen), _ => MatchLaunch.OfflineStory(1)),
                new ScreenCase("09-StoryResult", typeof(StoryResultScreen), StoryResult),
                new ScreenCase("10-StoryResult-Lost", typeof(StoryResultScreen), StoryResultLost),
                new ScreenCase("11-Pvp", typeof(PvpScreen)),
                new ScreenCase("12-PvpResult", typeof(PvpResultScreen), PvpResult),
                new ScreenCase("13-Shop", typeof(ShopScreen), _ => 0),
                new ScreenCase("13b-Shop-Orbes", typeof(ShopScreen), _ => 1),
                new ScreenCase("13c-Shop-Coins", typeof(ShopScreen), _ => 2),
                new ScreenCase("14-Shop-Offline", typeof(ShopScreen), _ => 0, withProfile: false),
                new ScreenCase("15-Guild", typeof(GuildScreen)),
                new ScreenCase("16-Friends", typeof(FriendsScreen)),
                new ScreenCase("17-Leaderboard", typeof(LeaderboardScreen)),
                new ScreenCase("18-Achievements", typeof(AchievementsScreen)),
                new ScreenCase("19-BattlePass", typeof(BattlePassScreen)),
                new ScreenCase("20-Quests", typeof(QuestsScreen)),
                new ScreenCase("21-Pets", typeof(PetsScreen)),
                new ScreenCase("22-Kingdom", typeof(KingdomScreen)),
                new ScreenCase("23-Profile", typeof(ProfileScreen)),
                new ScreenCase("24-Vip", typeof(VipScreen)),
                new ScreenCase("25-Settings", typeof(SettingsScreen)),
                new ScreenCase("26-Bag", typeof(BagScreen)),
                new ScreenCase("27-Bag-Offline", typeof(BagScreen), withProfile: false)
            };
        }

        private static void ShowSampleDialog(GameRoot game)
        {
            // Fire and forget: the dialog builds its widgets synchronously, the task only completes on a button press.
            _ = game.UI.Confirm(game.Loc.T("settings.deleteAccount"), game.Loc.T("settings.deleteAccountBody"));
        }

        /// <summary>Plays stage 1 headlessly with a greedy bot, which is what the result screen would show after a match.</summary>
        private static object StoryResult(GameRoot game)
        {
            GameSession session = StorySession(game);
            StageResult result = HeadlessRunner.Run(session, new GreedyBot());
            return new StoryResultArgs { StageId = 1, Local = result, Offline = true };
        }

        /// <summary>Same stage left untouched until the clock runs out: the lose variant of the screen.</summary>
        private static object StoryResultLost(GameRoot game)
        {
            GameSession session = StorySession(game);
            session.Tick(session.TimeLimitMs);
            return new StoryResultArgs { StageId = 1, Local = session.GetResult(), Offline = true };
        }

        private static GameSession StorySession(GameRoot game)
        {
            SessionConfig config = MatchLaunch.OfflineStory(1).BuildConfig(game.Backend);
            return new GameSession(config, game.Backend.Balance, game.Backend.PlayerId);
        }

        private static object PvpResult(GameRoot game)
        {
            MatchLaunch launch = MatchLaunch.OfflinePvp(game.Backend.Balance, 4242UL);
            var session = new GameSession(launch.BuildConfig(game.Backend), game.Backend.Balance, game.Backend.PlayerId);
            StageResult result = HeadlessRunner.Run(session, new GreedyBot());
            return new PvpResultArgs { Launch = launch, Local = result, OpponentScore = result.FinalScore - 500, Offline = true };
        }
    }
}
