using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Core.Story;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// One-screen tutorial shown the first time a stage uses a new board mechanic (countdown bombs, blight, dragon eggs):
    /// the piece itself, a title and two short lines on what to do. Each mechanic is explained once per device.
    /// </summary>
    public static class MechanicTutorial
    {
        private sealed class Tip
        {
            public string Id;
            public string Title;
            public string Body;
            public Sprite Art;
        }

        /// <summary>Shows the tips of every mechanic of <paramref name="stage"/> not explained yet.</summary>
        public static async Task ShowNewAsync(GameRoot game, StageData stage)
        {
            if (stage == null)
            {
                return;
            }
            var tips = new List<Tip>();
            Localization loc = game.Loc;
            if (stage.TimeBombCount > 0)
            {
                tips.Add(new Tip { Id = "bomb", Title = loc.T("mechanic.bomb.title"), Body = loc.T("mechanic.bomb.body"), Art = ArtLibrary.BombBadge() });
            }
            if (stage.BlightCount > 0)
            {
                tips.Add(new Tip { Id = "blight", Title = loc.T("mechanic.blight.title"), Body = loc.T("mechanic.blight.body"), Art = ArtLibrary.Blight() });
            }
            if (stage.EggCount > 0)
            {
                tips.Add(new Tip { Id = "egg", Title = loc.T("mechanic.egg.title"), Body = loc.T("mechanic.egg.body"), Art = ArtLibrary.Egg(2) });
            }
            if (stage.ChainCells > 0)
            {
                tips.Add(new Tip { Id = "chain", Title = loc.T("mechanic.chain.title"), Body = loc.T("mechanic.chain.body"), Art = ArtLibrary.Chain() });
            }
            if (stage.CursedCells > 0)
            {
                tips.Add(new Tip { Id = "curse", Title = loc.T("mechanic.curse.title"), Body = loc.T("mechanic.curse.body"), Art = ArtLibrary.Cursed() });
            }
            if (stage.ForgeCount > 0)
            {
                tips.Add(new Tip { Id = "forge", Title = loc.T("mechanic.forge.title"), Body = loc.T("mechanic.forge.body"), Art = ArtLibrary.Forge() ?? ArtLibrary.Blight() });
            }

            PlayerSettings settings = game.Save.Settings;
            settings.SeenMechanics = settings.SeenMechanics ?? new List<string>();
            foreach (Tip tip in tips)
            {
                if (settings.SeenMechanics.Contains(tip.Id))
                {
                    continue;
                }
                await ShowAsync(game, tip);
                settings.SeenMechanics.Add(tip.Id);
                game.Save.SaveSettings();
                game.Telemetry.Track("mechanic_tutorial", ("id", tip.Id), ("stage", stage.Id));
            }
        }

        private static Task ShowAsync(GameRoot game, Tip tip)
        {
            var done = new TaskCompletionSource<bool>();
            Image shade = UIFactory.Panel("MechanicTip", game.UI.DialogLayer, new Color(0.02f, 0.01f, 0.06f, 0.9f), rounded: false);
            UIFactory.Stretch(shade.rectTransform);

            Image box = UIFactory.Panel("Box", shade.transform, Theme.Panel);
            UIFactory.Anchor(box.rectTransform, 0.07f, 0.24f, 0.93f, 0.76f);
            bool framed = UiKit.FramePanel(box);
            box.gameObject.AddComponent<PopIn>();

            Text badge = UIFactory.Label(box.transform, game.Loc.T("mechanic.new"), Theme.SmallSize, Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(badge.rectTransform, 0.2f, framed ? 0.84f : 0.88f, 0.8f, framed ? 0.9f : 0.96f);
            Widgets.TitleOutline(badge);

            Text title = UIFactory.Label(box.transform, tip.Title, Theme.HeaderSize + 6, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.06f, 0.74f, 0.94f, 0.85f);
            Widgets.TitleOutline(title);

            // The piece, big, glowing and breathing, so the player recognises it on the board.
            Image glow = UIFactory.Icon(box.transform, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.45f, 0.55f), 0);
            UIFactory.Anchor(glow.rectTransform, 0.28f, 0.4f, 0.72f, 0.76f);
            glow.gameObject.AddComponent<Pulse>();
            if (tip.Art != null)
            {
                Image art = UIFactory.Icon(box.transform, tip.Art, Color.white, 0);
                art.preserveAspect = true;
                UIFactory.Anchor(art.rectTransform, 0.33f, 0.44f, 0.67f, 0.73f);
                art.gameObject.AddComponent<Breathe>().Amount = 0.05f;
            }

            Text body = UIFactory.Label(box.transform, tip.Body, Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = Theme.SmallSize - 6;
            body.resizeTextMaxSize = Theme.BodySize;
            UIFactory.Anchor(body.rectTransform, 0.07f, 0.2f, 0.93f, 0.42f);
            Widgets.TitleOutline(body);

            Button ok = UIFactory.Button(box.transform, game.Loc.T("mechanic.ok"), () =>
            {
                Object.Destroy(shade.gameObject);
                done.TrySetResult(true);
            }, Theme.Gold, Theme.BodySize);
            UIFactory.Anchor(ok.GetComponent<RectTransform>(), 0.22f, 0.05f, 0.78f, 0.17f);
            ok.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            return done.Task;
        }
    }
}
