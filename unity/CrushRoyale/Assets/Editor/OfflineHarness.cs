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

        public static void SetProfile(GameRoot game, bool withProfile)
        {
            game.Backend.OfflineProfile = withProfile ? DemoProfile() : null;
            game.Backend.OfflineShop = withProfile ? DemoShop() : null;
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
