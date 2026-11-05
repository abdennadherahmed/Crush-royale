using System.Threading.Tasks;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Proposé une fois, juste après le duel d'accueil : relier le compte à Google, ou continuer sans.
    ///
    /// Jusqu'ici le jeu créait un compte anonyme en silence et ne proposait Google que dans les réglages,
    /// où personne ne va. Un compte anonyme vit dans l'installation : désinstaller le jeu, changer de
    /// téléphone ou vider les données, et toute la progression disparaît sans avertissement. C'est arrivé
    /// pour de vrai, à un compte qui avait cent soixante-huit étapes et une guilde.
    ///
    /// Google est donc proposé en grand, et « continuer sans compte » reste disponible en bas, en petit,
    /// parce qu'obliger quelqu'un à se connecter avant d'avoir joué fait fuir plus de monde que ça n'en
    /// sauve. Le duel a déjà eu lieu quand cet écran s'affiche : le joueur sait ce qu'il protège.
    /// </summary>
    public sealed class SignInScreen : UIScreen
    {
        public override System.Type BackTarget => null;

        private Button _google;
        private Text _status;

        protected override void Build()
        {
            Widgets.Backdrop(Root, CrushRoyale.Core.Story.Kingdom.Central, 0.62f);
            Widgets.Fade(Root, top: true, 0.3f, 0.85f);
            Widgets.Fade(Root, top: false, 0.3f, 0.9f);
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeAreaPadding>();

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", safe), 0.06f, 0.84f, 0.94f, 0.94f);
            bool onRibbon = UiKit.Ribbon(ribbon) != null;
            Text title = UIFactory.Label(ribbon, Loc.T("signIn.title"), Theme.HeaderSize + 2, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            title.resizeTextForBestFit = true;
            title.resizeTextMinSize = Theme.SmallSize;
            title.resizeTextMaxSize = Theme.HeaderSize + 2;
            UIFactory.Anchor(title.rectTransform, 0.16f, onRibbon ? 0.32f : 0.1f, 0.84f, onRibbon ? 0.92f : 0.9f);
            Widgets.TitleOutline(title);

            Image glow = UIFactory.Icon(safe, ProceduralSprites.Glow(128), new Color(0.55f, 0.75f, 1f, 0.35f), 0);
            UIFactory.Anchor(glow.rectTransform, 0.2f, 0.56f, 0.8f, 0.8f);
            glow.gameObject.AddComponent<Pulse>();

            Image art = UIFactory.Icon(safe, UiKit.Art("item_lock") ?? UiKit.Art("item_crown"), Color.white, 0);
            art.preserveAspect = true;
            UIFactory.Anchor(art.rectTransform, 0.3f, 0.58f, 0.7f, 0.78f);
            art.gameObject.AddComponent<Breathe>();

            Text body = UIFactory.Label(safe, Loc.T("signIn.body"), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = Theme.SmallSize - 4;
            body.resizeTextMaxSize = Theme.BodySize;
            UIFactory.Anchor(body.rectTransform, 0.07f, 0.4f, 0.93f, 0.55f);
            Widgets.TitleOutline(body);

            _google = UIFactory.Button(safe, Loc.T("signIn.google"), () => _ = SignInAsync(), Theme.Gold, Theme.HeaderSize);
            UIFactory.Anchor(_google.GetComponent<RectTransform>(), 0.1f, 0.25f, 0.9f, 0.36f);
            _google.gameObject.AddComponent<Breathe>().Amount = 0.03f;

            _status = UIFactory.Label(safe, string.Empty, Theme.SmallSize - 4, Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            _status.resizeTextForBestFit = true;
            _status.resizeTextMinSize = Theme.SmallSize - 10;
            _status.resizeTextMaxSize = Theme.SmallSize - 4;
            UIFactory.Anchor(_status.rectTransform, 0.06f, 0.17f, 0.94f, 0.24f);

            // Discret, mais jamais caché : un joueur pressé doit pouvoir passer sans chercher.
            //
            // Un simple texte cliquable, et non un bouton du kit : le kit peint un cadre doré, qui faisait
            // de « continuer sans compte » l'element le plus voyant de l'ecran, exactement l'inverse de ce
            // qu'il doit etre.
            Text skipLabel = UIFactory.Label(safe, Loc.T("signIn.skip"), Theme.SmallSize - 2, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(skipLabel.rectTransform, 0.15f, 0.07f, 0.85f, 0.15f);
            Button skip = skipLabel.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            skip.targetGraphic = skipLabel;
            skip.onClick.AddListener(Continue);

            // Sans identifiant client Google, ou hors ligne, le bouton ne mènerait qu'à une erreur.
            bool possible = Game.Backend.IsOnline && !string.IsNullOrWhiteSpace(Game.Config.GoogleWebClientId);
            if (!possible)
            {
                _google.interactable = false;
                _status.color = Theme.TextMuted;
                _status.text = Loc.T(Game.Backend.IsOnline ? "signIn.unavailable" : "error.offline");
            }
        }

        public override Task OnShownAsync()
        {
            Game.Telemetry.Track("sign_in_shown");
            return Task.CompletedTask;
        }

        private async Task SignInAsync()
        {
            _status.text = string.Empty;
            bool ok;
            using (UI.Loading())
            {
                ok = await Game.Backend.SignInWithGoogleAsync(Game.Google);
            }
            if (this == null)
            {
                return;
            }
            if (ok)
            {
                Game.Telemetry.Track("sign_in_google");
                UI.Toast(Loc.T("settings.linked"), 3f);
                Continue();
                return;
            }
            // L'échec reste sur l'écran plutôt que de renvoyer ailleurs : le joueur peut réessayer, ou
            // passer. Lui dire ce qui a échoué évite le « ça ne marche pas » sans rien derrière.
            Game.Telemetry.Track("sign_in_failed", ("code", Game.Backend.LastError?.Code ?? "unknown"));
            _status.color = Theme.Danger;
            _status.text = Game.Backend.LastError == null
                ? Loc.T("signIn.failed")
                : Loc.T("signIn.failed") + " · " + Game.Backend.LastError.Code;
        }

        private void Continue()
        {
            Game.Audio.PlaySFX(SoundIds.Click);
            Game.Save.Settings.SignInOffered = true;
            Game.Save.SaveSettings();
            UI.ShowRoot<HeroSelectScreen>();
        }
    }
}
