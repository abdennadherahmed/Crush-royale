using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>Settings and profile: audio, haptics, notifications, accessibility, language, account, VIP, legal.</summary>
    public sealed class SettingsScreen : UIScreen
    {
        public override System.Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("settings.title");
            RectTransform list = UIFactory.ScrollList(body, 16, 40);
            PlayerSettings s = Game.Save.Settings;

            Widgets.SectionTitle(list, Loc.T("settings.audio"));
            Widgets.Stepper(list, Loc.T("settings.master"), s.MasterVolume, v => Game.Audio.SetVolume(v, s.SfxVolume, s.MusicVolume));
            Widgets.Stepper(list, Loc.T("settings.music"), s.MusicVolume, v => Game.Audio.SetVolume(s.MasterVolume, s.SfxVolume, v));
            Widgets.Stepper(list, Loc.T("settings.sfx"), s.SfxVolume, v => Game.Audio.SetVolume(s.MasterVolume, v, s.MusicVolume));
            Widgets.Toggle(list, Loc.T("settings.mute"), s.Muted, v => Game.Audio.SetMuted(v));

            Widgets.SectionTitle(list, Loc.T("settings.gameplay"));
            Widgets.Toggle(list, Loc.T("settings.haptics"), s.HapticsEnabled, v =>
            {
                s.HapticsEnabled = v;
                Game.Save.SaveSettings();
                Game.Haptics.Medium();
            });
            Widgets.Toggle(list, Loc.T("settings.colorBlind"), s.ColorBlindMode, v =>
            {
                s.ColorBlindMode = v;
                Game.Save.SaveSettings();
            });
            Widgets.Toggle(list, Loc.T("settings.notifications"), s.NotificationsEnabled, v =>
            {
                s.NotificationsEnabled = v;
                Game.Save.SaveSettings();
                if (v)
                {
                    Game.Notifications.RequestPermission();
                }
                else
                {
                    Game.Notifications.CancelAll();
                }
            });

            Widgets.SectionTitle(list, Loc.T("settings.language"));
            RectTransform gridRect = UIFactory.Rect("Languages", list);
            UIFactory.Height(gridRect, 4 * 110 + 3 * 12);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(300, 110);
            grid.spacing = new Vector2(12, 12);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            GridFit.On(grid);
            foreach (string code in Localization.Supported)
            {
                string language = code;
                UIFactory.Button(gridRect, Loc.T("language." + code), () =>
                {
                    s.Language = language;
                    Game.Save.SaveSettings();
                    Loc.SetLanguage(language);
                }, code == Loc.Language ? Theme.GoldDark : Theme.PanelLight, Theme.SmallSize + 2, Theme.Text);
            }

            Widgets.SectionTitle(list, Loc.T("settings.account"));
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null)
            {
                RectTransform card = Widgets.Card(list, 300);
                UIFactory.Label(card, profile.DisplayName, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("settings.playerId", profile.Id), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                UIFactory.Label(card, Loc.T("settings.vip", profile.Vip.Tier, Mathf.RoundToInt(profile.Vip.Progress * 100)), Theme.BodySize, Theme.Gold, TextAnchor.MiddleLeft);

                UIFactory.Height(UIFactory.Button(list, Loc.T("settings.copyId"), () =>
                {
                    GUIUtility.systemCopyBuffer = profile.Id;
                    UI.Toast(Loc.T("settings.copied"));
                }, Theme.PanelLight, Theme.BodySize, Theme.Text), 110);

                if (Game.Backend.IsGuest)
                {
                    UIFactory.Height(UIFactory.Label(list, Loc.T("settings.guestWarning"), Theme.SmallSize, Theme.Warning), 110);
                    UIFactory.Height(UIFactory.Button(list, Loc.T("settings.linkGoogle"), () => _ = LinkGoogleAsync()), 120);
                }
                else
                {
                    UIFactory.Height(UIFactory.Button(list, Loc.T("settings.signOut"), () => _ = SignOutAsync(), Theme.Danger), 120);
                }
                UIFactory.Height(UIFactory.Button(list, Loc.T("settings.editHero"), () => UI.Show<HeroSelectScreen>(), Theme.PanelLight, Theme.BodySize, Theme.Text), 110);
            }
            else
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("menu.practiceBody"), Theme.BodySize, Theme.Warning), 140);
            }

            Widgets.SectionTitle(list, Loc.T("settings.legal"));
            UIFactory.Height(UIFactory.Button(list, Loc.T("settings.privacy"), () => Application.OpenURL(Game.Config.PrivacyPolicyUrl), Theme.PanelLight, Theme.BodySize, Theme.Text), 110);
            UIFactory.Height(UIFactory.Button(list, Loc.T("settings.terms"), () => Application.OpenURL(Game.Config.TermsUrl), Theme.PanelLight, Theme.BodySize, Theme.Text), 110);
            UIFactory.Height(UIFactory.Button(list, Loc.T("settings.deleteAccount"), () =>
            {
                if (profile != null && Game.Backend.IsOnline)
                {
                    _ = DeleteAccountAsync();
                    return;
                }
                // Offline or no server account: fall back to an e-mail request.
                string id = profile?.Id ?? "unknown";
                Application.OpenURL("mailto:" + Game.Config.SupportEmail + "?subject=" + System.Uri.EscapeDataString("Account deletion " + id));
            }, Theme.PanelLight, Theme.BodySize, Theme.Danger), 110);
            UIFactory.Height(UIFactory.Label(list, Loc.T("settings.version", Application.version), Theme.SmallSize, Theme.TextMuted), 70);
        }

        private async Task LinkGoogleAsync()
        {
            bool ok;
            using (UI.Loading())
            {
                ok = await Game.Backend.SignInWithGoogleAsync(Game.Google);
            }
            if (ok)
            {
                UI.Toast(Loc.T("settings.linked"));
                Rebuild();
            }
            else if (Game.Backend.LastError != null)
            {
                UI.ShowError(Game.Backend.LastError);
            }
        }

        private async Task DeleteAccountAsync()
        {
            if (!await UI.Confirm(Loc.T("settings.deleteAccount"), Loc.T("settings.deleteAccountBody")))
            {
                return;
            }
            bool deleted;
            using (UI.Loading())
            {
                deleted = await Game.Backend.DeleteAccountAsync();
            }
            if (!deleted)
            {
                if (Game.Backend.LastError != null)
                {
                    UI.ShowError(Game.Backend.LastError);
                }
                return;
            }
            UI.Toast(Loc.T("settings.deleteAccountDone"), 3f);
            UI.ShowRoot<SplashScreen>();
        }

        private async Task SignOutAsync()
        {
            if (!await UI.Confirm(Loc.T("settings.signOut"), Loc.T("settings.signOutBody")))
            {
                return;
            }
            await Game.Backend.SignOutAsync();
            UI.ShowRoot<SplashScreen>();
        }
    }
}
