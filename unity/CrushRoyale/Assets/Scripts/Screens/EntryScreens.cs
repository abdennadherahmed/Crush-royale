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
        private RectTransform _updateBar;
        private Image _updateFill;

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

            _updateFill = UiKit.Bar(Root, 0f, Theme.Crystal, out _updateBar);
            UIFactory.Anchor(_updateBar, 0.12f, 0.215f, 0.88f, 0.25f);
            _updateBar.gameObject.SetActive(false);
        }

        /// <summary>
        /// Self-update (directly-installed APKs): download the newer build with a progress bar and open the installer.
        /// Returns true when the game must stop here (installer opened, or a required update that failed).
        /// </summary>
        private async Task<bool> RunUpdaterAsync(bool mustUpdate)
        {
            var updater = new AppUpdater(Game.Config);
            if (!updater.Supported)
            {
                return false;
            }
            _status.text = Loc.T("update.checking");
            if (!await updater.CheckAsync() || this == null)
            {
                return mustUpdate;
            }
            bool required = mustUpdate || updater.Required;
            Game.Telemetry.Track("update_found", ("to", (int)updater.Available.VersionCode), ("required", required));
            _updateBar.gameObject.SetActive(true);
            while (this != null)
            {
                AppUpdater.Outcome outcome = await updater.DownloadAndInstallAsync(
                    progress =>
                    {
                        if (this == null)
                        {
                            return;
                        }
                        _updateFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
                        _status.text = Loc.T("update.downloading") + "  " + Mathf.RoundToInt(progress * 100) + "%";
                    },
                    () => UI.Dialog(Loc.T("update.permissionTitle"), Loc.T("update.permissionBody"), Loc.T("update.allow"), required ? null : Loc.T("update.later")));
                if (this == null)
                {
                    return true;
                }
                if (outcome == AppUpdater.Outcome.InstallerOpened)
                {
                    // Installing restarts the game. Back here means the player closed the installer.
                    _status.text = Loc.T("update.installing");
                    await Task.Delay(1500);
                    if (this == null)
                    {
                        return true;
                    }
                    bool retry = await UI.Dialog(Loc.T("update.title"), Loc.T(required ? "update.requiredBody" : "update.readyBody"), Loc.T("update.install"), required ? null : Loc.T("update.later"));
                    if (retry || required)
                    {
                        continue;
                    }
                    break;
                }
                Game.Telemetry.Track("update_failed", ("required", required));
                if (!required)
                {
                    UI.Toast(Loc.T("update.failed"), 3f);
                    break;
                }
                await UI.Alert(Loc.T("update.title"), Loc.T("update.failedRequired"));
            }
            if (this != null)
            {
                _updateBar.gameObject.SetActive(false);
            }
            return false;
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

            if (await RunUpdaterAsync(false))
            {
                return;
            }

            _status.text = Loc.T("splash.connecting");
            var playOffline = new TaskCompletionSource<bool>();
            _connecting = true;
            _ = RevealOfflineButtonAsync(playOffline);
            float loginStarted = Time.realtimeSinceStartup;
            bool online = await Game.Backend.StartAsync(Loc.Language, 8000, playOffline.Task);
            _connecting = false;
            Game.Telemetry.Track(online ? "login_online" : "login_offline", ("ms", (int)((Time.realtimeSinceStartup - loginStarted) * 1000)),
                ("skipped", playOffline.Task.IsCompleted), ("error", Game.Backend.LastError?.Code ?? string.Empty));
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
                    // The server runs newer rules: update in place (APK) or through the store (Google Play install).
                    if (new AppUpdater(Game.Config).Supported)
                    {
                        await RunUpdaterAsync(true);
                        if (this != null)
                        {
                            // No newer build published yet: the player can only wait for it.
                            _status.text = Loc.T("error.VersionMismatch");
                        }
                        return;
                    }
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
            UiKit.Recolor(card.GetComponent<Image>(), selected ? Theme.GoldDark : Theme.Panel);
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
            Game.Telemetry.Track("hero_created", ("gender", _gender), ("online", Game.Backend.IsOnline), ("age_given", settings.HeroAge > 0));

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

    /// <summary>
    /// Hub: the player's hero in the middle; profile and currencies on top; events, achievements, pass and ranking
    /// on the top left; friends and map on the right; shop bottom left, guild bottom right, the two play buttons between.
    /// </summary>
    public sealed class MainMenuScreen : UIScreen
    {
        protected override void Build()
        {
            Widgets.Backdrop(Root, CrushRoyale.Core.Story.Kingdom.Central, 0.78f);
            Widgets.Fade(Root, top: true, 0.26f, 0.8f);
            Widgets.Fade(Root, top: false, 0.34f, 0.9f);
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeArea>();

            ProfileDto profile = Game.Backend.Profile;
            PlayerSettings settings = Game.Save.Settings;
            string gender = profile?.Hero?.Gender ?? settings.HeroGender ?? "female";

            BuildHero(safe, profile, settings, gender);
            BuildTopBar(safe, profile, settings, gender);

            // Top left: events and progression.
            // Five buttons per column must stay above the chest bar (which starts at 0.34).
            float y = 0.815f;
            SideButton(safe, "quests", "menu.quests", typeof(QuestsScreen), "DailyQuests", 0.015f, y, profile?.ClaimableQuests ?? 0);
            SideButton(safe, "achievements", "menu.achievements", typeof(AchievementsScreen), null, 0.015f, y -= SideStep, profile?.UnclaimedAchievements ?? 0);
            SideButton(safe, "battlepass", "menu.battlepass", typeof(BattlePassScreen), "BattlePass", 0.015f, y -= SideStep, 0);
            SideButton(safe, "leaderboard", "menu.leaderboard", typeof(LeaderboardScreen), "Pvp", 0.015f, y -= SideStep, 0);
            SideButton(safe, "castle", "menu.kingdom", typeof(KingdomScreen), null, 0.015f, y -= SideStep, profile?.Story?.Restoration?.CanBuild == true ? 1 : 0);

            // Right: social and the world map.
            SideButton(safe, "friends", "menu.friends", typeof(FriendsScreen), "Friends", 0.815f, 0.815f, 0);
            SideButton(safe, "map", "menu.map", typeof(WorldMapScreen), "Story", 0.815f, 0.815f - SideStep, 0);
            SideButton(safe, "pets", "menu.pets", typeof(PetsScreen), null, 0.815f, 0.815f - 2 * SideStep, 0);
            WheelButton(safe, profile);

            // Bottom: shop (left), play buttons (center), guild (right).
            CornerButton(safe, "shop", "menu.shop", typeof(ShopScreen), "Shop", 0.01f);
            CornerButton(safe, "guild", "menu.guild", typeof(GuildScreen), "Guilds", 0.77f);

            int stage = profile?.Story?.HighestUnlockedStage ?? 0;
            string storyLabel = Loc.T("menu.story") + (stage > 0 ? "\n" + Loc.T("menu.stageShort", stage) : string.Empty);
            PlayButton(safe, storyLabel, "map", typeof(WorldMapScreen), "Story", Theme.GoldDark, 0.14f, 0.255f);
            PlayButton(safe, Loc.T("menu.pvp"), "pvp", typeof(PvpScreen), "Pvp", Theme.Hex("B3263E"), 0.025f, 0.13f);

            if (!Game.Backend.IsOnline)
            {
                OfflineBanner(safe);
            }
        }

        /// <summary>
        /// Offline is practice only (story up to stage 50 and PvP against a bot, nothing saved): everything that touches the
        /// shared online world (shop, guild, pets, friends...) waits for the connection so nothing can conflict with it.
        /// </summary>
        private void OfflineBanner(RectTransform safe)
        {
            Image pill = UIFactory.Panel("Offline", safe, new Color(0.35f, 0.06f, 0.12f, 0.92f));
            UIFactory.Anchor(pill.rectTransform, 0.2f, 0.835f, 0.8f, 0.885f);
            Outline border = pill.gameObject.AddComponent<Outline>();
            border.effectColor = new Color(1f, 0.4f, 0.45f, 0.9f);
            border.effectDistance = new Vector2(3, -3);
            Image dot = UIFactory.Icon(pill.transform, ProceduralSprites.Circle(), Theme.Danger, 0);
            dot.preserveAspect = true;
            UIFactory.Anchor(dot.rectTransform, 0.03f, 0.25f, 0.09f, 0.75f);
            dot.gameObject.AddComponent<Pulse>().Scale = true;
            Text text = UIFactory.Label(pill.transform, Loc.T("menu.offlineBanner"), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(text.rectTransform, 0.1f, 0.05f, 0.98f, 0.95f);
            Widgets.TitleOutline(text);
            Button retry = pill.gameObject.AddComponent<Button>();
            retry.targetGraphic = pill;
            retry.onClick.AddListener(() =>
            {
                Game.Audio.PlaySFX(SoundIds.Click);
                Game.Backend.TryReconnect();
                UI.Toast(Loc.T("menu.offlineRetry"));
            });
        }

        private void BuildHero(RectTransform safe, ProfileDto profile, PlayerSettings settings, string gender)
        {
            string id = gender == "male" ? "hero" : "heroine";
            Sprite full = CosmeticLook.HeroFull(gender, profile?.Inventory?.EquippedOutfit);
            Sprite art = full ?? ArtLibrary.Character(id);

            Image halo = UIFactory.Icon(safe, ProceduralSprites.Glow(128), new Color(Theme.Crystal.r, Theme.Crystal.g, Theme.Crystal.b, 0.38f), 0);
            UIFactory.Anchor(halo.rectTransform, 0.08f, 0.39f, 0.92f, 0.88f);
            halo.gameObject.AddComponent<Pulse>();

            Image pedestal = UIFactory.Icon(safe, ProceduralSprites.Glow(128), new Color(0.02f, 0.01f, 0.06f, 0.75f), 0);
            UIFactory.Anchor(pedestal.rectTransform, 0.22f, 0.325f, 0.78f, 0.385f);

            if (art != null)
            {
                Image hero = UIFactory.Icon(safe, art, Color.white, 0);
                hero.preserveAspect = true;
                hero.raycastTarget = false;
                hero.rectTransform.pivot = new Vector2(0.5f, 0f);
                UIFactory.Anchor(hero.rectTransform, 0.17f, full != null ? 0.345f : 0.39f, 0.83f, 0.885f);
                hero.gameObject.AddComponent<Breathe>();
            }

            string equippedPet = profile?.Pets?.Equipped;
            Sprite petArt = string.IsNullOrEmpty(equippedPet) ? null : PetsScreen.PetArt(equippedPet);
            if (petArt != null)
            {
                Image pet = UIFactory.Icon(safe, petArt, Color.white, 0);
                pet.raycastTarget = false;
                pet.rectTransform.pivot = new Vector2(0.5f, 0f);
                UIFactory.Anchor(pet.rectTransform, 0.6f, 0.335f, 0.86f, 0.48f);
                Breathe hop = pet.gameObject.AddComponent<Breathe>();
                hop.Amount = 0.05f;
                hop.Speed = 3f;
            }

            if (profile != null)
            {
                ChestBar chests = ChestBar.Create(safe, UI);
                UIFactory.Anchor((RectTransform)chests.transform, 0.03f, 0.262f, 0.97f, 0.34f);
            }
        }

        private void BuildTopBar(RectTransform safe, ProfileDto profile, PlayerSettings settings, string gender)
        {
            // Profile chip (the gear on the right opens the settings).
            Button chip = UIFactory.Button(safe, string.Empty, () =>
            {
                if (Game.Backend.IsOnline)
                {
                    UI.Show<ProfileScreen>();
                }
                else
                {
                    UI.Toast(Loc.T("error.offline"));
                }
            }, new Color(0.1f, 0.07f, 0.22f, 0.9f));
            RectTransform chipRect = UIFactory.Anchor(chip.GetComponent<RectTransform>(), 0.015f, 0.915f, 0.43f, 0.99f);
            Image ring = UIFactory.Icon(chipRect, ProceduralSprites.Circle(), CosmeticLook.Frame(profile?.Inventory?.EquippedFrame).Main, 0);
            UIFactory.Anchor(ring.rectTransform, 0.02f, 0.06f, 0.29f, 0.94f).GetComponent<Image>().preserveAspect = true;
            Sprite portrait = ArtLibrary.Character(gender == "male" ? "hero" : "heroine");
            if (portrait != null)
            {
                Image face = UIFactory.Icon(ring.transform, portrait, Color.white, 0);
                face.preserveAspect = true;
                UIFactory.Stretch(face.rectTransform, 6, 6, 6, 6);
            }
            string name = profile?.DisplayName ?? settings.HeroPseudo ?? Loc.T("menu.practiceTitle");
            Text title = UIFactory.Label(chipRect, name, Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.32f, 0.5f, 0.98f, 0.95f);
            string equippedTitle = profile?.Inventory?.EquippedTitle;
            if (!string.IsNullOrEmpty(equippedTitle))
            {
                RectTransform plate = CosmeticLook.TitlePlate(chipRect, Loc, equippedTitle, Theme.SmallSize - 6);
                UIFactory.Anchor(plate, 0.28f, 0.02f, 1.0f, 0.52f);
            }
            else
            {
                string detail = profile == null
                    ? Loc.T("common.offline")
                    : Loc.T("league." + profile.Pvp.League) + "  " + Loc.Number(profile.Pvp.Trophies);
                Text sub = UIFactory.Label(chipRect, detail, Theme.SmallSize - 4, profile == null ? Theme.Warning : Theme.League(profile.Pvp.League), TextAnchor.MiddleLeft);
                UIFactory.Anchor(sub.rectTransform, 0.32f, 0.06f, 0.98f, 0.5f);
            }
            Sprite frameArt = CosmeticLook.FrameArt(profile?.Inventory?.EquippedFrame);
            if (frameArt != null)
            {
                Image frameImage = UIFactory.Icon(ring.transform, frameArt, Color.white, 0);
                frameImage.preserveAspect = false;
                CosmeticLook.PlaceFrame(frameImage.rectTransform, profile.Inventory.EquippedFrame);
            }

            if (profile != null)
            {
                VipBadge(safe, profile.Vip?.Tier ?? 0);
                StreakBadge(safe, profile.Story?.WinStreak ?? 0);
            }

            CurrencyBar bar = CurrencyBar.Create(safe);
            UIFactory.Anchor((RectTransform)bar.transform, 0.44f, 0.925f, 0.865f, 0.985f);

            Button gear = IconButton(safe, "settings", () => UI.Show<SettingsScreen>());
            UIFactory.Anchor(gear.GetComponent<RectTransform>(), 0.875f, 0.915f, 0.985f, 0.99f);
        }

        /// <summary>Daily wheel under the pets: glows with a "!" while today's free spin is waiting.</summary>
        private void WheelButton(RectTransform safe, ProfileDto profile)
        {
            bool available = Game.Backend.IsOnline && (profile?.WheelAvailable ?? false);
            Button button = IconButton(safe, "wheel", () =>
            {
                Game.Audio.PlaySFX(SoundIds.Click);
                if (!Game.Backend.IsOnline)
                {
                    UI.Toast(Loc.T("menu.offlineLocked"), 3f);
                    return;
                }
                DailyWheelPopup.Show();
            });
            float top = 0.815f - 3 * SideStep;
            RectTransform rect = UIFactory.Anchor(button.GetComponent<RectTransform>(), 0.815f, top - SideHeight, 0.985f, top);
            Transform wheelIcon = rect.childCount > 0 ? rect.GetChild(0) : null;
            Caption(rect, Loc.T("menu.wheel"), Game.Backend.IsOnline);
            if (available)
            {
                Image glow = UIFactory.Icon(rect, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.35f, 0.8f), 0);
                UIFactory.Stretch(glow.rectTransform, -20, -20, -20, -20);
                glow.raycastTarget = false;
                glow.transform.SetAsFirstSibling();
                glow.gameObject.AddComponent<Pulse>();
                if (wheelIcon != null)
                {
                    wheelIcon.gameObject.AddComponent<Spin>().DegreesPerSecond = 25f;
                }
                CountBadge(rect, 1);
            }
        }

        /// <summary>Flame next to the VIP plate while a win streak runs (3, 5, 7 wins give starting bonuses).</summary>
        private void StreakBadge(RectTransform safe, int streak)
        {
            if (streak <= 0)
            {
                return;
            }
            Button plate = UIFactory.Button(safe, string.Empty, () => UI.Toast(Loc.T("streak.explain"), 4f), Theme.Hex("B3263E"));
            RectTransform rect = UIFactory.Anchor(plate.GetComponent<RectTransform>(), 0.26f, 0.872f, 0.42f, 0.914f);
            Sprite bolt = UiKit.Art("item_bolt");
            if (bolt != null)
            {
                Image icon = UIFactory.Icon(rect, bolt, Color.white, 0);
                icon.preserveAspect = true;
                UIFactory.Anchor(icon.rectTransform, -0.04f, 0.02f, 0.36f, 1.2f);
            }
            Text label = UIFactory.Label(rect, "x" + streak, Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.34f, 0.05f, 0.98f, 0.95f);
            Widgets.TitleOutline(label);
            if (streak >= 3)
            {
                plate.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }
        }

        /// <summary>Gold "VIP n" plate with a crown under the profile chip; opens the VIP benefits page.</summary>
        private void VipBadge(RectTransform safe, int tier)
        {
            Button plate = UIFactory.Button(safe, string.Empty, () => UI.Show<VipScreen>(), tier > 0 ? Theme.GoldDark : Theme.PanelLight);
            RectTransform rect = UIFactory.Anchor(plate.GetComponent<RectTransform>(), 0.03f, 0.872f, 0.25f, 0.914f);
            Sprite crown = UiKit.Art("item_crown") ?? ArtLibrary.Icon("crown");
            if (crown != null)
            {
                Image icon = UIFactory.Icon(rect, crown, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, -0.06f, 0.05f, 0.3f, 1.25f);
            }
            Text label = UIFactory.Label(rect, tier > 0 ? Loc.T("vip.badge", tier) : Loc.T("vip.become"), Theme.SmallSize - 2, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.28f, 0.05f, 0.97f, 0.95f);
            Widgets.TitleOutline(label);
            if (tier > 0)
            {
                plate.gameObject.AddComponent<Breathe>().Amount = 0.015f;
            }
        }

        private const float SideStep = 0.094f;

        private const float SideHeight = 0.086f;

        private void SideButton(RectTransform safe, string icon, string key, Type screen, string feature, float x, float yTop, int badge)
        {
            bool unlocked = IsUnlocked(screen, feature);
            Button button = IconButton(safe, icon, () => Open(screen, feature, unlocked));
            RectTransform rect = UIFactory.Anchor(button.GetComponent<RectTransform>(), x, yTop - SideHeight, x + 0.17f, yTop);
            Caption(rect, Loc.T(key), unlocked);
            if (!unlocked)
            {
                LockBadge(rect);
            }
            else if (badge > 0)
            {
                CountBadge(rect, badge);
            }
        }

        private void CornerButton(RectTransform safe, string icon, string key, Type screen, string feature, float x)
        {
            bool unlocked = IsUnlocked(screen, feature);
            Button button = IconButton(safe, icon, () => Open(screen, feature, unlocked));
            RectTransform rect = UIFactory.Anchor(button.GetComponent<RectTransform>(), x, 0.05f, x + 0.22f, 0.21f);
            Caption(rect, Loc.T(key), unlocked);
            if (!unlocked)
            {
                LockBadge(rect);
            }
        }

        private void PlayButton(RectTransform safe, string label, string icon, Type screen, string feature, Color color, float yMin, float yMax)
        {
            bool unlocked = IsUnlocked(screen, feature);
            Button button = UIFactory.Button(safe, label, () => Open(screen, feature, unlocked), unlocked ? color : Theme.BackgroundLight, Theme.HeaderSize, Theme.Text);
            RectTransform rect = UIFactory.Anchor(button.GetComponent<RectTransform>(), 0.25f, yMin, 0.75f, yMax);
            Text text = button.GetComponentInChildren<Text>();
            text.fontStyle = FontStyle.Bold;
            UIFactory.Stretch(text.rectTransform, 130, 16, 0, 0);
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.55f);
            outline.effectDistance = new Vector2(3, -3);
            Sprite art = ArtLibrary.Icon(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(rect, art, Color.white, 0);
                image.preserveAspect = true;
                image.raycastTarget = false;
                UIFactory.Anchor(image.rectTransform, 0.02f, 0.06f, 0.26f, 0.94f);
            }
            if (unlocked && feature == "Story")
            {
                rect.gameObject.AddComponent<Breathe>().Amount = 0.025f;
            }
            if (!unlocked)
            {
                text.text = label + "\n" + Loc.T("menu.locked", LockHint(feature));
                text.fontSize = Theme.SmallSize;
            }
        }

        private Button IconButton(RectTransform parent, string icon, Action onClick)
        {
            Button button = UIFactory.Button(parent, string.Empty, onClick, new Color(0, 0, 0, 0));
            Sprite art = ArtLibrary.Icon(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(button.transform, art, Color.white, 0);
                image.preserveAspect = true;
                image.raycastTarget = false;
                UIFactory.Anchor(image.rectTransform, 0.04f, 0.2f, 0.96f, 1f);
            }
            return button;
        }

        private static void Caption(RectTransform rect, string text, bool unlocked)
        {
            Text caption = UIFactory.Label(rect, text, Theme.SmallSize - 6, unlocked ? Theme.Text : Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(caption.rectTransform, -0.15f, -0.02f, 1.15f, 0.24f);
            Outline outline = caption.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.03f, 0.01f, 0.08f, 0.95f);
            outline.effectDistance = new Vector2(2, -2);
            if (!unlocked)
            {
                foreach (Image image in rect.GetComponentsInChildren<Image>())
                {
                    if (image.sprite != null && image.transform != rect)
                    {
                        image.color = new Color(0.45f, 0.45f, 0.5f, 1f);
                    }
                }
            }
        }

        private static void LockBadge(RectTransform rect)
        {
            Sprite lockArt = ArtLibrary.Icon("lock");
            if (lockArt == null)
            {
                return;
            }
            Image image = UIFactory.Icon(rect, lockArt, Color.white, 0);
            image.preserveAspect = true;
            image.raycastTarget = false;
            UIFactory.Anchor(image.rectTransform, 0.58f, 0.55f, 0.98f, 0.98f);
        }

        private static void CountBadge(RectTransform rect, int count)
        {
            Image dot = UIFactory.Icon(rect, ProceduralSprites.Circle(), Theme.Danger, 0);
            dot.raycastTarget = false;
            UIFactory.Anchor(dot.rectTransform, 0.66f, 0.7f, 0.98f, 1.02f);
            dot.preserveAspect = true;
            Text number = UIFactory.Label(dot.transform, count > 9 ? "9+" : count.ToString(), Theme.SmallSize - 4, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(number.rectTransform);
            dot.gameObject.AddComponent<Pulse>().Scale = true;
        }

        private static string ProgressKey(ProfileDto p) => string.Join("|",
            p.Story?.HighestUnlockedStage, p.Pvp?.Trophies, p.Pvp?.League, p.ClaimableQuests, p.UnclaimedAchievements, p.DisplayName,
            p.Pets?.Equipped, p.Story?.UnlockedFeatures == null ? 0 : p.Story.UnlockedFeatures.Count,
            p.Chests == null ? string.Empty : string.Join(",", p.Chests.Slots.Select(c => c == null ? "-" : c.Type + c.Status)));

        private bool IsUnlocked(Type screen, string feature)
        {
            if (!Game.Backend.IsOnline)
            {
                // Offline practice: story and PvP against a bot only.
                return screen == typeof(WorldMapScreen) || screen == typeof(PvpScreen) || screen == typeof(SettingsScreen);
            }
            return feature == null || Widgets.FeatureUnlocked(feature);
        }

        private void Open(Type screen, string feature, bool unlocked)
        {
            Game.Audio.PlaySFX(SoundIds.Click);
            if (unlocked)
            {
                UI.Show(screen);
            }
            else if (!Game.Backend.IsOnline)
            {
                // Online-only feature: explain why (no conflicts with the online world) and try to reconnect.
                UI.Toast(Loc.T("menu.offlineLocked"), 3f);
                Game.Backend.TryReconnect();
            }
            else
            {
                UI.Toast(Loc.T("menu.lockedToast", LockHint(feature)));
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

            // Always show fresh progress (stage, trophies, badges, chests, pet) when coming back to the hub.
            if (profile != null)
            {
                string before = ProgressKey(profile);
                await Game.Backend.RefreshProfileAsync();
                if (this == null)
                {
                    return;
                }
                profile = Game.Backend.Profile;
                if (profile != null && ProgressKey(profile) != before)
                {
                    Rebuild();
                }
            }
            await ChallengeFlow.CheckClipboardAsync(UI, Game);
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
