using System.Threading.Tasks;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Le tout premier ecran d'un nouveau joueur : un duel, avant le choix du heros et avant le menu.
    ///
    /// Un match-3 PvP se vend sur le duel, et jusqu'ici un nouveau venu arrivait sur un menu de quatorze
    /// boutons et une etape solo : il n'avait aucune raison de deviner que le jeu se joue a deux. L'ecran
    /// d'affiche montre l'adversaire, le bouton lance le duel, et le reste du jeu n'apparait qu'apres.
    ///
    /// Rien n'est mise ni gagne : le duel est hors ligne, contre un bot, et ne soumet aucun resultat.
    /// </summary>
    public sealed class FirstDuelScreen : UIScreen
    {
        /// <summary>Le dos du telephone ne doit pas pouvoir sauter le duel vers un menu qui n'existe pas encore.</summary>
        public override System.Type BackTarget => null;

        private Button _fight;
        private Text _loading;
        private MatchLaunch _launch;
        private bool _started;

        protected override void Build()
        {
            Widgets.Backdrop(Root, CrushRoyale.Core.Story.Kingdom.Central, 0.6f);
            Widgets.Fade(Root, top: true, 0.3f, 0.85f);
            Widgets.Fade(Root, top: false, 0.3f, 0.9f);
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeAreaPadding>();

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", safe), 0.06f, 0.86f, 0.94f, 0.96f);
            bool onRibbon = UiKit.Ribbon(ribbon) != null;
            Text title = UIFactory.Label(ribbon, Loc.T("firstDuel.title"), Theme.HeaderSize + 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.18f, onRibbon ? 0.32f : 0.1f, 0.82f, onRibbon ? 0.92f : 0.9f);
            Widgets.TitleOutline(title);

            // Les cartes suivent la hauteur des portraits : plus hautes, elles laissaient un grand vide violet
            // sous chaque combattant, parce qu'un portrait a ratio preserve ne remplit pas une carte etiree.
            Portrait(safe, ArtLibrary.Character("heroine"), Loc.T("firstDuel.you"), Theme.Crystal, 0.04f, 0.5f, 0.48f, 0.84f);
            Portrait(safe, ArtLibrary.Character("mark"), Loc.T("firstDuel.opponent"), Theme.Danger, 0.52f, 0.5f, 0.96f, 0.84f);

            // Le « VS » par-dessus la couture des deux portraits, pose en dernier pour rester au-dessus.
            Image clash = UIFactory.Icon(safe, ProceduralSprites.Glow(128), new Color(1f, 0.8f, 0.35f, 0.5f), 0);
            UIFactory.Anchor(clash.rectTransform, 0.33f, 0.59f, 0.67f, 0.75f);
            clash.gameObject.AddComponent<Pulse>();
            Text vs = UIFactory.Label(safe, Loc.T("firstDuel.vs"), Theme.TitleSize + 14, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(vs.rectTransform, 0.3f, 0.6f, 0.7f, 0.74f);
            Widgets.TitleOutline(vs);
            vs.gameObject.AddComponent<PopIn>();

            Text intro = UIFactory.Label(safe, Loc.T("firstDuel.intro"), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            intro.resizeTextForBestFit = true;
            intro.resizeTextMinSize = Theme.SmallSize - 4;
            intro.resizeTextMaxSize = Theme.BodySize;
            UIFactory.Anchor(intro.rectTransform, 0.08f, 0.33f, 0.92f, 0.46f);
            Widgets.TitleOutline(intro);

            _fight = UIFactory.Button(safe, Loc.T("firstDuel.fight"), Fight, Theme.Hex("B3263E"), Theme.HeaderSize + 2);
            UIFactory.Anchor(_fight.GetComponent<RectTransform>(), 0.16f, 0.16f, 0.84f, 0.28f);
            _fight.gameObject.AddComponent<Breathe>().Amount = 0.035f;
            // Le plateau se choisit en tache de fond pendant que le joueur lit l'affiche : le bouton ne
            // s'allume qu'une fois le duel pret, plutot que d'ouvrir un ecran de chargement.
            _fight.interactable = false;

            _loading = UIFactory.Label(safe, Loc.T("firstDuel.preparing"), Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_loading.rectTransform, 0.1f, 0.1f, 0.9f, 0.15f);
        }

        private void Portrait(RectTransform safe, Sprite art, string name, Color tint, float x0, float y0, float x1, float y1)
        {
            Image card = UIFactory.Panel("Fighter", safe, Theme.Panel);
            UIFactory.Anchor(card.rectTransform, x0, y0, x1, y1);
            UiKit.CardFrame(card);
            if (art != null)
            {
                Image portrait = UIFactory.Icon(card.transform, art, Color.white, 0);
                portrait.preserveAspect = true;
                UIFactory.Anchor(portrait.rectTransform, 0.04f, 0.16f, 0.96f, 0.98f);
                portrait.gameObject.AddComponent<Breathe>();
            }
            Text label = UIFactory.Label(card.transform, name, Theme.BodySize, tint, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Theme.SmallSize - 6;
            label.resizeTextMaxSize = Theme.BodySize;
            UIFactory.Anchor(label.rectTransform, 0.04f, 0.03f, 0.96f, 0.16f);
            Widgets.TitleOutline(label);
        }

        public override async Task OnShownAsync()
        {
            Game.Audio.PlayMusic(SoundIds.PvpMusic);
            Game.Telemetry.Track("first_duel_shown");

            // Rejouer une vingtaine de parties du bot prend un instant : hors du fil principal, l'affiche
            // reste vivante pendant ce temps.
            CrushRoyale.Core.Config.GameBalance balance = Game.Backend.Balance;
            MatchLaunch launch = await Task.Run(() => MatchLaunch.OnboardingDuel(balance));
            if (this == null)
            {
                return;
            }
            _launch = launch;
            if (_loading != null)
            {
                _loading.text = string.Empty;
            }
            if (_fight != null)
            {
                _fight.interactable = true;
            }
        }

        private void Fight()
        {
            if (_started || _launch == null)
            {
                return;
            }
            _started = true;
            Game.Audio.PlaySFX(SoundIds.Click);
            Game.Telemetry.Track("first_duel_start");
            UI.Show<GameplayScreen>(_launch, addToHistory: false);
        }
    }

    /// <summary>
    /// Juste apres le duel d'accueil : ce qu'il a donne, puis la creation du heros.
    ///
    /// Un ecran a part parce que le resultat PvP habituel renvoie au menu principal, qui n'existe pas encore
    /// pour un joueur sans heros, et parce que ni les trophees ni les recompenses n'ont de sens ici.
    /// </summary>
    public sealed class FirstDuelResultScreen : UIScreen
    {
        /// <summary>Scores du duel d'accueil.</summary>
        public sealed class Args
        {
            public long You;
            public long Opponent;
        }

        public override System.Type BackTarget => null;

        protected override void Build()
        {
            var args = (Args)base.Args ?? new Args();
            bool won = args.You >= args.Opponent;

            Widgets.Backdrop(Root, CrushRoyale.Core.Story.Kingdom.Central, 0.6f);
            Widgets.Fade(Root, top: true, 0.3f, 0.85f);
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeAreaPadding>();

            Image rays = UIFactory.Icon(safe, UiKit.Art("burst") ?? ProceduralSprites.Glow(128), new Color(1f, 0.9f, 0.55f, won ? 0.45f : 0.12f), 0);
            UIFactory.Anchor(rays.rectTransform, -0.2f, 0.35f, 1.2f, 0.95f);
            rays.gameObject.AddComponent<Spin>();

            Text title = UIFactory.Label(safe, Loc.T(won ? "firstDuel.wonTitle" : "firstDuel.lostTitle"),
                Theme.TitleSize + 6, won ? Theme.Gold : Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.06f, 0.74f, 0.94f, 0.86f);
            Widgets.TitleOutline(title);
            title.gameObject.AddComponent<PopIn>();

            // Les deux scores etiquetes, sinon « 35 840 - 35 340 » ne dit pas qui est qui.
            ScoreRow(safe, Loc.T("firstDuel.you"), args.You, Theme.Crystal, 0.52f);
            ScoreRow(safe, Loc.T("firstDuel.opponent"), args.Opponent, Theme.Danger, 0.4f);

            Text body = UIFactory.Label(safe, Loc.T(won ? "firstDuel.wonBody" : "firstDuel.lostBody"),
                Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = Theme.SmallSize - 4;
            body.resizeTextMaxSize = Theme.BodySize;
            UIFactory.Anchor(body.rectTransform, 0.08f, 0.24f, 0.92f, 0.37f);
            Widgets.TitleOutline(body);

            Button next = UIFactory.Button(safe, Loc.T("firstDuel.continue"), () =>
            {
                Game.Audio.PlaySFX(SoundIds.Click);
                UI.ShowRoot<HeroSelectScreen>();
            }, Theme.Gold, Theme.HeaderSize + 2);
            UIFactory.Anchor(next.GetComponent<RectTransform>(), 0.16f, 0.08f, 0.84f, 0.2f);
            next.gameObject.AddComponent<Breathe>().Amount = 0.035f;
        }

        private void ScoreRow(RectTransform safe, string who, long score, Color tint, float y)
        {
            Image row = UIFactory.Panel("Score", safe, Theme.Panel);
            UIFactory.Anchor(row.rectTransform, 0.1f, y, 0.9f, y + 0.1f);
            UiKit.CardFrame(row);
            Text name = UIFactory.Label(row.transform, who, Theme.BodySize, tint, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.06f, 0.15f, 0.56f, 0.85f);
            Widgets.TitleOutline(name);
            Text value = UIFactory.Label(row.transform, Loc.Number(score), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Anchor(value.rectTransform, 0.56f, 0.15f, 0.94f, 0.85f);
            Widgets.TitleOutline(value);
        }

        public override Task OnShownAsync()
        {
            var args = (Args)base.Args ?? new Args();
            Game.Audio.PlaySFX(args.You >= args.Opponent ? SoundIds.WinFanfare : SoundIds.LoseJingle);
            return Task.CompletedTask;
        }
    }
}
