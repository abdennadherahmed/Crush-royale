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
                if (Game.Save.Settings.HeroCreated)
                {
                    UI.ShowRoot<MainMenuScreen>();
                }
                else
                {
                    UI.ShowRoot<HeroSelectScreen>();
                }
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

            PlayerSettings settings = game.Save.Settings;
            if (!profile.HeroChosen && settings.HeroCreated && HeroSelectScreen.IsValidPseudo(settings.HeroPseudo))
            {
                // Hero created offline: send it now (a refused pseudo reopens the creation screen, pre-filled).
                try
                {
                    await game.Backend.Client.Api.SetHeroAsync(HeroSelectScreen.HeroRequest(settings, loc.Language));
                    await game.Backend.RefreshProfileAsync();
                    profile = game.Backend.Profile;
                }
                catch (CrushApiException ex)
                {
                    Debug.LogWarning("Offline hero sync failed: " + ex.Code);
                }
            }
            if (profile.HeroChosen && !settings.HeroCreated)
            {
                settings.HeroCreated = true;
                game.Save.SaveSettings();
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

    /// <summary>
    /// First launch: pick the hero (big portraits) and a pseudo, optional age (purchase protection). Works offline:
    /// the choice is kept on the device and sent to the server as soon as the game connects.
    /// </summary>
    public sealed class HeroSelectScreen : UIScreen
    {
        private string _gender = "female";
        private RectTransform _femaleCard;
        private RectTransform _maleCard;
        private InputField _pseudo;
        private InputField _age;
        private bool _submitting;

        protected override void Build()
        {
            PlayerSettings settings = Game.Save.Settings;
            if (!string.IsNullOrEmpty(settings.HeroGender))
            {
                _gender = settings.HeroGender;
            }

            RectTransform body = Frame("hero.title", backButton: false);

            Text choose = UIFactory.Label(body, Loc.T("hero.choose"), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(choose.rectTransform, 0.04f, 0.9f, 0.96f, 0.98f);

            _femaleCard = HeroCard(body, "female", 0.04f, 0.49f);
            _maleCard = HeroCard(body, "male", 0.51f, 0.96f);
            Highlight();

            Text story = UIFactory.Label(body, Loc.T("hero.story"), Theme.SmallSize + 2, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(story.rectTransform, 0.06f, 0.4f, 0.94f, 0.48f);

            _pseudo = Widgets.Input(body, Loc.T("hero.pseudoPlaceholder"), 20);
            UIFactory.Anchor(_pseudo.GetComponent<RectTransform>(), 0.1f, 0.3f, 0.9f, 0.38f);
            _pseudo.text = settings.HeroPseudo ?? string.Empty;

            _age = Widgets.Input(body, Loc.T("hero.agePlaceholder"), 3, InputField.ContentType.IntegerNumber);
            UIFactory.Anchor(_age.GetComponent<RectTransform>(), 0.3f, 0.215f, 0.7f, 0.28f);
            if (settings.HeroAge > 0)
            {
                _age.text = settings.HeroAge.ToString();
            }
            Text why = UIFactory.Label(body, Loc.T("hero.ageWhy"), Theme.SmallSize - 2, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(why.rectTransform, 0.08f, 0.15f, 0.92f, 0.21f);

            Button confirm = UIFactory.Button(body, Loc.T("hero.confirm"), () => _ = SubmitAsync(), Theme.GoldDark, Theme.HeaderSize, Theme.Text);
            UIFactory.Anchor(confirm.GetComponent<RectTransform>(), 0.12f, 0.03f, 0.88f, 0.13f);
        }

        private RectTransform HeroCard(RectTransform parent, string gender, float xMin, float xMax)
        {
            Button button = UIFactory.Button(parent, string.Empty, () =>
            {
                _gender = gender;
                Game.Audio.PlaySFX(SoundIds.Click);
                Highlight();
            }, Theme.Panel, Theme.BodySize, Theme.Text);
            RectTransform card = UIFactory.Anchor(button.GetComponent<RectTransform>(), xMin, 0.5f, xMax, 0.89f);

            Sprite portrait = ArtLibrary.Character(gender == "male" ? "hero" : "heroine");
            if (portrait != null)
            {
                Image art = UIFactory.Icon(card, portrait, Color.white, 0);
                art.name = "Portrait";
                UIFactory.Anchor(art.rectTransform, 0.04f, 0.14f, 0.96f, 0.98f);
            }
            else
            {
                Image silhouette = UIFactory.Icon(card, ProceduralSprites.Gem(gender == "male" ? ProceduralSprites.GemShape.Diamond : ProceduralSprites.GemShape.Hexagon), gender == "male" ? Theme.Crystal : Theme.Orbe, 220);
                silhouette.name = "Portrait";
                silhouette.rectTransform.anchorMin = silhouette.rectTransform.anchorMax = new Vector2(0.5f, 0.58f);
            }

            Text label = UIFactory.Label(card, Loc.T("hero." + gender), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0, 0.01f, 1, 0.14f);
            return card;
        }

        private void Highlight()
        {
            Style(_femaleCard, _gender == "female");
            Style(_maleCard, _gender == "male");
        }

        private static void Style(RectTransform card, bool selected)
        {
            card.GetComponent<Image>().color = selected ? Theme.GoldDark : Theme.Panel;
            card.localScale = Vector3.one * (selected ? 1f : 0.92f);
            Transform portrait = card.Find("Portrait");
            if (portrait != null)
            {
                portrait.GetComponent<Image>().color = selected ? Color.white : new Color(0.55f, 0.55f, 0.6f, 1f);
            }
        }

        private async Task SubmitAsync()
        {
            if (_submitting)
            {
                return;
            }
            string pseudo = _pseudo.text.Trim();
            if (!IsValidPseudo(pseudo))
            {
                UI.Toast(Loc.T("hero.pseudoInvalid"));
                return;
            }

            PlayerSettings settings = Game.Save.Settings;
            settings.HeroGender = _gender;
            settings.HeroPseudo = pseudo;
            settings.HeroAge = int.TryParse(_age.text, out int age) && age >= 4 && age <= 120 ? age : 0;
            settings.HeroCreated = true;
            Game.Save.SaveSettings();

            _submitting = true;
            try
            {
                if (Game.Backend.IsOnline)
                {
                    ProfileDto profile = await Api(api => api.SetHeroAsync(HeroRequest(settings, Loc.Language)));
                    if (profile == null)
                    {
                        return;
                    }
                    await Game.Backend.RefreshProfileAsync();
                }
                await DialogueOverlay.PlayAsync(UI, Loc, "dlg.prologue");
                UI.ShowRoot<MainMenuScreen>();
            }
            finally
            {
                _submitting = false;
            }
        }

        /// <summary>Same rules as the server: 3-20 letters, digits, spaces, '_' or '-'.</summary>
        public static bool IsValidPseudo(string pseudo) =>
            pseudo != null && pseudo.Length >= 3 && pseudo.Length <= 20 && pseudo.All(c => char.IsLetterOrDigit(c) || c == ' ' || c == '_' || c == '-');

        /// <summary>The pseudo doubles as the hero's name (letters only on the server, 16 max).</summary>
        public static SetHeroRequest HeroRequest(PlayerSettings settings, string language)
        {
            string heroName = new string((settings.HeroPseudo ?? string.Empty).Where(c => char.IsLetter(c) || c == ' ' || c == '-').ToArray()).Trim();
            if (heroName.Length > 16)
            {
                heroName = heroName.Substring(0, 16).Trim();
            }
            if (heroName.Length == 0)
            {
                heroName = "Crusher";
            }
            return new SetHeroRequest
            {
                Gender = settings.HeroGender == "male" ? "male" : "female",
                HeroName = heroName,
                DisplayName = settings.HeroPseudo,
                Age = settings.HeroAge > 0 ? settings.HeroAge : (int?)null,
                Language = language
            };
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
            string name = profile?.DisplayName ?? Game.Save.Settings.HeroPseudo ?? Loc.T("menu.practiceTitle");
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
