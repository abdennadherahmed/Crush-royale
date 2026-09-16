using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>Boot: privacy consent (first launch), sign-in and server login, then hero creation or main menu.</summary>
    public sealed class SplashScreen : UIScreen
    {
        private Text _status;
        private Button _offlineButton;
        private bool _connecting;

        protected override void Build()
        {
            Widgets.Backdrop(Root, CrushRoyale.Core.Story.Kingdom.Central, 0.55f);
            Sprite logo = ArtLibrary.Logo();
            if (logo != null)
            {
                Image image = UIFactory.Icon(Root, logo, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.05f, 0.59f, 0.95f, 0.84f);
            }
            else
            {
                Text title = UIFactory.Label(Root, "CRUSH ROYALE", 120, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(title.rectTransform, 0.05f, 0.58f, 0.95f, 0.72f);
            }
            Text subtitle = UIFactory.Label(Root, Loc.T("splash.subtitle"), Theme.BodySize, Theme.Crystal);
            UIFactory.Anchor(subtitle.rectTransform, 0.1f, 0.52f, 0.9f, 0.58f);

            Image spinner = UIFactory.Icon(Root, ProceduralSprites.Ring(), Theme.Gold, 140);
            spinner.rectTransform.anchorMin = spinner.rectTransform.anchorMax = new Vector2(0.5f, 0.38f);
            spinner.gameObject.AddComponent<Spinner>();

            _status = UIFactory.Label(Root, string.Empty, Theme.SmallSize, Theme.TextMuted);
            UIFactory.Anchor(_status.rectTransform, 0.1f, 0.26f, 0.9f, 0.32f);
        }

        public override async Task OnShownAsync()
        {
            Game.Audio.PlayMusic(SoundIds.MenuMusic);

            while (!Game.Save.Settings.PrivacyAccepted)
            {
                bool accepted = await UI.Dialog(Loc.T("privacy.title"), Loc.T("privacy.body"), Loc.T("privacy.accept"), Loc.T("privacy.read"));
                if (accepted)
                {
                    Game.Save.Settings.PrivacyAccepted = true;
                    Game.Save.SaveSettings();
                }
                else
                {
                    Application.OpenURL(Game.Config.PrivacyPolicyUrl);
                }
            }

            _status.text = Loc.T("splash.connecting");
            var playOffline = new TaskCompletionSource<bool>();
            _connecting = true;
            _ = RevealOfflineButtonAsync(playOffline);
            bool online = await Game.Backend.StartAsync(Loc.Language, 8000, playOffline.Task);
            _connecting = false;
            if (this == null)
            {
                return;
            }
            if (_offlineButton != null)
            {
                Destroy(_offlineButton.gameObject);
            }

            if (!online)
            {
                CrushApiException error = Game.Backend.LastError;
                if (error != null && error.Code == "VersionMismatch")
                {
                    await UI.Alert(Loc.T("error.title"), Loc.T("error.VersionMismatch"));
                    Application.OpenURL("market://details?id=" + Application.identifier);
                    return;
                }
                UI.Toast(Game.Backend.IsConfigured ? Loc.T("splash.offline") : Loc.T("splash.notConfigured"), 3.5f);
                UI.ShowRoot<MainMenuScreen>();
                return;
            }

            await EnterOnlineAsync(Game, UI, Loc);
        }

        /// <summary>After a successful login (at boot, or later in the background): store, notifications, hero creation or menu.</summary>
        public static async Task EnterOnlineAsync(GameRoot game, UIRoot ui, Localization loc)
        {
            EconomyBalance economy = game.Backend.Balance.Economy;
            var skus = economy.OrbePacks.Select(p => p.Sku).ToList();
            skus.Add(economy.RemoveAdsSku);
            skus.Add(economy.BattlePassSku);
            skus.Add(economy.RarePerkSku);
            game.Iap.Initialize(skus, "crushroyale.orbes");
            game.Notifications.RequestPermission();

            ProfileDto profile = game.Backend.Profile;
            if (profile.SuspendedUntilUnixMs > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            {
                await ui.Alert(loc.T("error.title"), loc.T("error.Banned"));
            }

            if (!profile.HeroChosen)
            {
                ui.ShowRoot<HeroSelectScreen>();
            }
            else
            {
                ui.ShowRoot<MainMenuScreen>();
            }
        }

        /// <summary>A slow server (cold start, weak network) must never trap the player: offer offline play after 3 s.</summary>
        private async Task RevealOfflineButtonAsync(TaskCompletionSource<bool> playOffline)
        {
            await Task.Delay(3000);
            if (this == null || !_connecting)
            {
                return;
            }
            _offlineButton = UIFactory.Button(Root, Loc.T("splash.playOffline"), () => playOffline.TrySetResult(true), Theme.PanelLight, Theme.BodySize, Theme.Text);
            UIFactory.Anchor(_offlineButton.GetComponent<RectTransform>(), 0.2f, 0.12f, 0.8f, 0.2f);
        }
    }

    /// <summary>First launch: choose the hero (GDD male/female + name), a display name and declare an age (purchase protection).</summary>
    public sealed class HeroSelectScreen : UIScreen
    {
        private string _gender = "female";
        private Image _male;
        private Image _female;
        private InputField _heroName;
        private InputField _displayName;
        private InputField _age;

        protected override void Build()
        {
            RectTransform body = Frame("hero.title", backButton: false);
            RectTransform list = UIFactory.ScrollList(body, 28, 56);

            Widgets.SectionTitle(list, Loc.T("hero.choose"));
            HorizontalLayoutGroup genders = UIFactory.Row(list, 320, 32);
            _female = HeroCard(genders.transform, "female");
            _male = HeroCard(genders.transform, "male");
            Highlight();

            UIFactory.Height(UIFactory.Label(list, Loc.T("hero.story"), Theme.SmallSize + 2, Theme.TextMuted), 130);
            UIFactory.Height(_heroName = Widgets.Input(list, Loc.T("hero.namePlaceholder"), 16), 120);
            UIFactory.Height(_displayName = Widgets.Input(list, Loc.T("hero.displayPlaceholder"), 20), 120);
            UIFactory.Height(_age = Widgets.Input(list, Loc.T("hero.agePlaceholder"), 3, InputField.ContentType.IntegerNumber), 120);
            UIFactory.Height(UIFactory.Label(list, Loc.T("hero.ageWhy"), Theme.SmallSize, Theme.TextMuted), 90);

            UIFactory.Height(UIFactory.Button(list, Loc.T("hero.confirm"), () => _ = SubmitAsync()), 140);
        }

        private Image HeroCard(Transform parent, string gender)
        {
            Button button = UIFactory.Button(parent, Loc.T("hero." + gender), () =>
            {
                _gender = gender;
                Highlight();
            }, Theme.PanelLight, Theme.HeaderSize, Theme.Text);
            Sprite portrait = ArtLibrary.Character(gender == "male" ? "hero" : "heroine");
            if (portrait != null)
            {
                Image art = UIFactory.Icon(button.transform, portrait, Color.white, 0);
                UIFactory.Anchor(art.rectTransform, 0.08f, 0.28f, 0.92f, 0.98f);
            }
            else
            {
                Image silhouette = UIFactory.Icon(button.transform, ProceduralSprites.Gem(gender == "male" ? ProceduralSprites.GemShape.Diamond : ProceduralSprites.GemShape.Hexagon), gender == "male" ? Theme.Crystal : Theme.Orbe, 150);
                silhouette.rectTransform.anchorMin = silhouette.rectTransform.anchorMax = new Vector2(0.5f, 0.66f);
            }
            return button.GetComponent<Image>();
        }

        private void Highlight()
        {
            _male.color = _gender == "male" ? Theme.GoldDark : Theme.PanelLight;
            _female.color = _gender == "female" ? Theme.GoldDark : Theme.PanelLight;
        }

        private async Task SubmitAsync()
        {
            int? age = int.TryParse(_age.text, out int parsed) ? parsed : (int?)null;
            if (string.IsNullOrWhiteSpace(_heroName.text))
            {
                UI.Toast(Loc.T("hero.nameRequired"));
                return;
            }

            var request = new SetHeroRequest
            {
                Gender = _gender,
                HeroName = _heroName.text.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(_displayName.text) ? null : _displayName.text.Trim(),
                Age = age,
                Language = Loc.Language
            };

            if (!Game.Backend.IsOnline)
            {
                UI.ShowRoot<MainMenuScreen>();
                return;
            }
            ProfileDto profile = await Api(api => api.SetHeroAsync(request));
            if (profile != null)
            {
                await Game.Backend.RefreshProfileAsync();
                await DialogueOverlay.PlayAsync(UI, Loc, "dlg.prologue");
                UI.ShowRoot<MainMenuScreen>();
            }
        }
    }

    /// <summary>Hub: currencies, profile card, quick play buttons (story, PvP, shop, guild...) and daily login bonus.</summary>
    public sealed class MainMenuScreen : UIScreen
    {
        private static readonly (string Key, Type Screen, string Feature)[] Entries =
        {
            ("menu.story", typeof(WorldMapScreen), "Story"),
            ("menu.pvp", typeof(PvpScreen), "Pvp"),
            ("menu.shop", typeof(ShopScreen), "Shop"),
            ("menu.guild", typeof(GuildScreen), "Guilds"),
            ("menu.friends", typeof(FriendsScreen), "Friends"),
            ("menu.leaderboard", typeof(LeaderboardScreen), "Pvp"),
            ("menu.quests", typeof(QuestsScreen), "DailyQuests"),
            ("menu.battlepass", typeof(BattlePassScreen), "BattlePass"),
            ("menu.achievements", typeof(AchievementsScreen), null),
            ("menu.settings", typeof(SettingsScreen), null)
        };

        protected override void Build()
        {
            Widgets.Backdrop(Root, CrushRoyale.Core.Story.Kingdom.Central, 0.6f);
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeArea>();

            CurrencyBar bar = CurrencyBar.Create(safe);
            UIFactory.Anchor((RectTransform)bar.transform, 0.02f, 0.925f, 0.98f, 0.99f);

            ProfileDto profile = Game.Backend.Profile;
            Image card = UIFactory.Panel("Profile", safe, Theme.Panel);
            UIFactory.Anchor(card.rectTransform, 0.02f, 0.78f, 0.98f, 0.915f);
            string name = profile?.DisplayName ?? Loc.T("menu.practiceTitle");
            Text title = UIFactory.Label(card.transform, name, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.05f, 0.5f, 0.7f, 0.95f);

            string detail = profile == null
                ? Loc.T("menu.practiceBody")
                : Loc.T("menu.profileLine", Loc.T("league." + profile.Pvp.League), profile.Pvp.Trophies, profile.Story.HighestUnlockedStage, profile.Vip.Tier);
            Text sub = UIFactory.Label(card.transform, detail, Theme.SmallSize + 2, profile == null ? Theme.Warning : Theme.League(profile.Pvp.League), TextAnchor.MiddleLeft);
            UIFactory.Anchor(sub.rectTransform, 0.05f, 0.05f, 0.95f, 0.5f);

            Sprite crown = ArtLibrary.Icon("crown");
            if (crown != null)
            {
                UIFactory.Anchor(UIFactory.Icon(card.transform, crown, Color.white, 0).rectTransform, 0.74f, 0.1f, 0.95f, 0.9f);
            }
            else
            {
                Text logo = UIFactory.Label(card.transform, "CR", 80, Theme.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.Anchor(logo.rectTransform, 0.7f, 0.1f, 0.95f, 0.9f);
            }

            RectTransform grid = UIFactory.Anchor(UIFactory.Rect("Grid", safe), 0.03f, 0.04f, 0.97f, 0.76f);
            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(480, 230);
            layout.spacing = new Vector2(28, 28);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 2;

            foreach ((string key, Type screen, string feature) in Entries)
            {
                bool unlocked = feature == null || Widgets.FeatureUnlocked(feature) || (feature == "Pvp" && !Game.Backend.IsOnline && screen == typeof(PvpScreen));
                if (!Game.Backend.IsOnline && feature == null && screen != typeof(SettingsScreen))
                {
                    unlocked = false;
                }
                string label = Loc.T(key) + (unlocked ? string.Empty : "\n" + Loc.T("menu.locked", LockHint(feature)));
                Type target = screen;
                bool open = unlocked;
                UIFactory.Button(grid, label, () =>
                {
                    if (open)
                    {
                        UI.Show(target);
                    }
                    else
                    {
                        UI.Toast(Loc.T("menu.lockedToast", LockHint(feature)));
                    }
                }, unlocked ? (key == "menu.story" || key == "menu.pvp" ? Theme.GoldDark : Theme.PanelLight) : Theme.BackgroundLight, Theme.HeaderSize - 4, Theme.Text);
            }
        }

        public override async Task OnShownAsync()
        {
            Game.Audio.PlayMusic(SoundIds.MenuMusic);
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null && !profile.HeroChosen)
            {
                UI.ShowRoot<HeroSelectScreen>();
                return;
            }
            if (profile == null || !profile.LoginBonusAvailable)
            {
                return;
            }
            LoginBonusResponse bonus = await Api(api => api.ClaimLoginBonusAsync(), loading: false);
            if (bonus != null)
            {
                profile.LoginBonusAvailable = false;
                Game.Backend.ApplyWallet(bonus.Wallet);
                Game.Audio.PlaySFX(SoundIds.Coins);
                await UI.Alert(Loc.T("login.title", bonus.TotalLoginDays), RewardText(Loc, bonus.Reward));
            }
        }

        public override bool HandleBack()
        {
            _ = ConfirmQuitAsync();
            return true;
        }

        public static string RewardText(Localization loc, RewardDto reward)
        {
            if (reward == null)
            {
                return string.Empty;
            }
            var parts = new List<string>();
            if (reward.Coins > 0)
            {
                parts.Add(loc.T("currency.coins", loc.Number(reward.Coins)));
            }
            if (reward.Orbes > 0)
            {
                parts.Add(loc.T("currency.orbes", loc.Number(reward.Orbes)));
            }
            foreach (KeyValuePair<string, int> p in reward.PowerUps)
            {
                parts.Add(loc.T("powerup." + p.Key) + " x" + p.Value);
            }
            foreach (string c in reward.Cosmetics)
            {
                parts.Add(loc.T("cosmetic." + c));
            }
            if (reward.Lives > 0)
            {
                parts.Add(loc.T("currency.lives", reward.Lives));
            }
            if (reward.BattlePassXp > 0)
            {
                parts.Add(loc.T("battlepass.xp", reward.BattlePassXp));
            }
            return string.Join("\n", parts);
        }

        private string LockHint(string feature)
        {
            if (!Game.Backend.IsOnline)
            {
                return Loc.T("common.online");
            }
            StoryBalance s = Game.Backend.Balance.Story;
            switch (feature)
            {
                case "Pvp": return Loc.T("menu.stage", s.UnlockPvpStage);
                case "Shop": return Loc.T("menu.stage", s.UnlockShopStage);
                case "Guilds": return Loc.T("menu.stage", s.UnlockGuildsStage);
                case "Friends": return Loc.T("menu.stage", s.UnlockFriendsStage);
                case "DailyQuests": return Loc.T("menu.stage", s.UnlockDailyQuestsStage);
                case "BattlePass": return Loc.T("menu.stage", s.UnlockBattlePassStage);
                default: return string.Empty;
            }
        }

        private async Task ConfirmQuitAsync()
        {
            if (await UI.Confirm(Loc.T("menu.quitTitle"), Loc.T("menu.quitBody")))
            {
                Application.Quit();
            }
        }
    }
}
