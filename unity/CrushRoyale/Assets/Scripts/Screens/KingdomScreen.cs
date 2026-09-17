using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Story;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Rebuild Crystalheim: the current zone shown ruined, with every repaired part revealed from its restored painting
    /// (dust burst and sparkles when it appears), the stars to spend, and the six repair tasks. Finishing a zone plays a
    /// celebration, pays its reward and opens the next one.
    /// </summary>
    public sealed class KingdomScreen : UIScreen
    {
        /// <summary>
        /// Where each task appears on the zone painting (normalized, origin bottom left), per zone then task.
        /// Matches the Art/Restoration/z{n}_restored paintings.
        /// </summary>
        private static readonly Rect[][] Regions =
        {
            new[] { new Rect(0.4f, 0.2f, 0.28f, 0.47f), new Rect(0f, 0.22f, 0.42f, 0.43f), new Rect(0.08f, 0.68f, 0.34f, 0.32f), new Rect(0.12f, 0f, 0.74f, 0.24f), new Rect(0.68f, 0.4f, 0.19f, 0.6f), new Rect(0.85f, 0.03f, 0.15f, 0.69f) },
            new[] { new Rect(0.05f, 0f, 0.25f, 0.6f), new Rect(0.2f, 0.3f, 0.6f, 0.25f), new Rect(0.2f, 0.5f, 0.6f, 0.12f), new Rect(0.7f, 0.25f, 0.28f, 0.6f), new Rect(0.35f, 0.55f, 0.3f, 0.4f), new Rect(0f, 0.6f, 0.3f, 0.4f) },
            new[] { new Rect(0f, 0f, 1f, 0.22f), new Rect(0f, 0.18f, 0.35f, 0.35f), new Rect(0.38f, 0.3f, 0.24f, 0.55f), new Rect(0.35f, 0f, 0.3f, 0.3f), new Rect(0.65f, 0.25f, 0.35f, 0.7f), new Rect(0f, 0.5f, 0.35f, 0.5f) },
            new[] { new Rect(0.3f, 0f, 0.4f, 0.25f), new Rect(0.05f, 0.1f, 0.3f, 0.5f), new Rect(0.65f, 0.1f, 0.3f, 0.5f), new Rect(0.6f, 0.55f, 0.4f, 0.45f), new Rect(0.4f, 0.3f, 0.2f, 0.3f), new Rect(0.35f, 0.55f, 0.3f, 0.45f) },
            new[] { new Rect(0f, 0.1f, 0.25f, 0.8f), new Rect(0.25f, 0.55f, 0.5f, 0.45f), new Rect(0.3f, 0f, 0.4f, 0.3f), new Rect(0.35f, 0.75f, 0.3f, 0.25f), new Rect(0.38f, 0.2f, 0.24f, 0.45f), new Rect(0.75f, 0.1f, 0.25f, 0.8f) }
        };

        private static readonly string[] TaskIcons = { "item_gift", "item_pouch", "item_hourglass", "item_stars", "item_medal", "item_crown" };

        private string _justBuilt;
        private bool _busy;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("kingdom.title");
            RestorationDto data = Game.Backend.Profile?.Story?.Restoration;
            if (data == null)
            {
                Text offline = UIFactory.Label(body, Loc.T(Game.Backend.IsOnline ? "common.loading" : "error.offline"), Theme.BodySize, Theme.TextMuted);
                UIFactory.Stretch(offline.rectTransform);
                return;
            }
            var built = new HashSet<string>(data.Built);

            if (data.CurrentZone <= 0)
            {
                BuildFinished(body);
                return;
            }
            RestorationZone zone = Restoration.Zones[data.CurrentZone - 1];

            // Header: zone name, zone progress, stars to spend.
            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", body), 0.02f, 0.9f, 0.98f, 1f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, Loc.T("kingdom.zone", zone.Number, Restoration.Zones.Count, ZoneName(zone.Number)), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.16f, 0.34f, 0.84f, 0.92f);
            Widgets.TitleOutline(title);

            int done = 0;
            foreach (RestorationTask task in zone.Tasks)
            {
                done += built.Contains(task.Id) ? 1 : 0;
            }
            UiKit.Bar(body, done / (float)zone.Tasks.Count, Theme.Success, out RectTransform bar);
            UIFactory.Anchor(bar, 0.04f, 0.845f, 0.62f, 0.89f);
            Text barText = UIFactory.Label(bar, Loc.T("kingdom.progress", done, zone.Tasks.Count), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(barText.rectTransform);
            Widgets.TitleOutline(barText);

            Image starChip = UIFactory.Panel("Stars", body, new Color(0.05f, 0.03f, 0.12f, 0.85f));
            UIFactory.Anchor(starChip.rectTransform, 0.65f, 0.84f, 0.96f, 0.895f);
            Sprite starArt = UiKit.Art("item_stars") ?? ArtLibrary.Icon("star");
            if (starArt != null)
            {
                Image icon = UIFactory.Icon(starChip.transform, starArt, Color.white, 0);
                icon.preserveAspect = true;
                UIFactory.Anchor(icon.rectTransform, 0.02f, -0.1f, 0.34f, 1.1f);
                icon.gameObject.AddComponent<Breathe>().Amount = 0.05f;
            }
            Text stars = UIFactory.Label(starChip.transform, Loc.Number(data.StarsAvailable), Theme.HeaderSize - 4, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(stars.rectTransform, 0.34f, 0f, 0.98f, 1f);
            Widgets.TitleOutline(stars);

            BuildScene(body, zone, built);
            BuildTasks(body, zone, built, data.StarsAvailable);
        }

        public override async Task OnShownAsync()
        {
            if (Game.Backend.IsOnline && Game.Backend.Profile?.Story?.Restoration == null)
            {
                await Game.Backend.RefreshProfileAsync();
                if (this != null)
                {
                    Rebuild();
                }
            }
        }

        private string ZoneName(int zone)
        {
            switch (zone)
            {
                case 1: return Loc.T("kingdom.z1");
                case 2: return Loc.T("kingdom.z2");
                case 3: return Loc.T("kingdom.z3");
                case 4: return Loc.T("kingdom.z4");
                default: return Loc.T("kingdom.z5");
            }
        }

        private static Sprite ZoneArt(int zone, bool restored) => ArtLibrary.Load("Art/Restoration/z" + zone + (restored ? "_restored" : "_ruined"));

        private void BuildScene(RectTransform body, RestorationZone zone, HashSet<string> built)
        {
            Image frame = UIFactory.Panel("Scene", body, Theme.Panel);
            UIFactory.Anchor(frame.rectTransform, 0.02f, 0.46f, 0.98f, 0.835f);
            UiKit.FramePanel(frame);
            RectTransform holder = UIFactory.Stretch(UIFactory.Rect("Holder", frame.rectTransform), 22, 22, 22, 22);
            holder.gameObject.AddComponent<RectMask2D>();

            // Paintings come in aligned pairs; a zone missing either one uses its kingdom scenery (dark, then revealed).
            Sprite ruined = ZoneArt(zone.Number, false);
            Sprite restored = ZoneArt(zone.Number, true);
            if (ruined == null || restored == null)
            {
                ruined = null;
                restored = null;
            }
            CrushRoyale.Core.Story.Kingdom fallbackKingdom = (CrushRoyale.Core.Story.Kingdom)((zone.Number + 3) % 5);
            Sprite fallback = ArtLibrary.Background(fallbackKingdom);

            // The painting keeps its proportions and fills the frame.
            RectTransform art = UIFactory.Stretch(UIFactory.Rect("Art", holder));
            AspectRatioFitter fitter = art.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            Sprite baseSprite = ruined ?? fallback;
            fitter.aspectRatio = baseSprite != null ? baseSprite.rect.width / baseSprite.rect.height : 16f / 9f;

            Image baseImage = art.gameObject.AddComponent<Image>();
            baseImage.sprite = baseSprite;
            baseImage.raycastTarget = false;
            // Without the ruined painting, the restored one (or the kingdom) is shown dark and grey.
            baseImage.color = ruined != null ? Color.white : new Color(0.32f, 0.28f, 0.36f, 1f);
            Sprite reveal = restored ?? fallback;

            for (int i = 0; i < zone.Tasks.Count; i++)
            {
                RestorationTask task = zone.Tasks[i];
                if (!built.Contains(task.Id) || reveal == null)
                {
                    continue;
                }
                Rect r = Regions[zone.Number - 1][i];
                RectTransform window = UIFactory.Anchor(UIFactory.Rect("Built" + i, art), r.xMin, r.yMin, r.xMax, r.yMax);
                window.gameObject.AddComponent<RectMask2D>();
                RectTransform piece = UIFactory.Rect("Restored", window);
                piece.anchorMin = new Vector2(-r.xMin / r.width, -r.yMin / r.height);
                piece.anchorMax = new Vector2((1f - r.xMin) / r.width, (1f - r.yMin) / r.height);
                piece.offsetMin = piece.offsetMax = Vector2.zero;
                Image image = piece.gameObject.AddComponent<Image>();
                image.sprite = reveal;
                image.raycastTarget = false;
                if (task.Id == _justBuilt)
                {
                    StartCoroutine(RevealPiece(window, art));
                }
            }

            // Floating dust over the ruins, sparkles over the restored parts.
            for (int i = 0; i < 10; i++)
            {
                Image mote = UIFactory.Icon(art, ProceduralSprites.Spark(), new Color(1f, 0.95f, 0.7f, 0.7f), 26);
                mote.rectTransform.anchorMin = mote.rectTransform.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value);
                mote.gameObject.AddComponent<Pulse>().Speed = 1f + UnityEngine.Random.value * 2f;
                mote.raycastTarget = false;
            }
        }

        private IEnumerator RevealPiece(RectTransform window, RectTransform art)
        {
            _justBuilt = null;
            CanvasGroup group = window.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            Image burst = UIFactory.Icon(art, ProceduralSprites.Glow(128), new Color(1f, 0.9f, 0.5f, 1f), 0);
            burst.raycastTarget = false;
            burst.rectTransform.anchorMin = window.anchorMin;
            burst.rectTransform.anchorMax = window.anchorMax;
            burst.rectTransform.offsetMin = burst.rectTransform.offsetMax = Vector2.zero;
            var sparks = new List<(RectTransform Rect, Vector2 Velocity)>();
            Vector2 center = (window.anchorMin + window.anchorMax) / 2f;
            for (int i = 0; i < 18; i++)
            {
                Image spark = UIFactory.Icon(art, ProceduralSprites.Spark(), i % 2 == 0 ? Theme.Gold : Color.white, 40);
                spark.raycastTarget = false;
                spark.rectTransform.anchorMin = spark.rectTransform.anchorMax = center;
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                sparks.Add((spark.rectTransform, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(180f, 420f)));
            }
            Game.Audio.PlaySFX(SoundIds.Whoosh);
            const float duration = 1.1f;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                group.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f));
                float pop = 1f + 0.12f * Mathf.Sin(Mathf.Clamp01(k * 1.6f) * Mathf.PI);
                window.localScale = new Vector3(pop, pop, 1f);
                burst.color = new Color(1f, 0.9f, 0.5f, 1f - k);
                burst.rectTransform.localScale = Vector3.one * (1f + k * 0.8f);
                foreach ((RectTransform rect, Vector2 velocity) in sparks)
                {
                    if (rect != null)
                    {
                        rect.anchoredPosition = velocity * k;
                        rect.localScale = Vector3.one * (1f - k);
                    }
                }
                yield return null;
            }
            group.alpha = 1f;
            window.localScale = Vector3.one;
            Destroy(burst.gameObject);
            foreach ((RectTransform rect, Vector2 _) in sparks)
            {
                if (rect != null)
                {
                    Destroy(rect.gameObject);
                }
            }
            Game.Audio.PlaySFX(SoundIds.Sparkle);
        }

        private void BuildTasks(RectTransform body, RestorationZone zone, HashSet<string> built, int stars)
        {
            RectTransform gridRect = UIFactory.Anchor(UIFactory.Rect("Tasks", body), 0.02f, 0.01f, 0.98f, 0.45f);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(480, 230);
            grid.spacing = new Vector2(14, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.MiddleCenter;
            GridFit.On(grid);

            for (int i = 0; i < zone.Tasks.Count; i++)
            {
                RestorationTask task = zone.Tasks[i];
                bool done = built.Contains(task.Id);
                bool affordable = !done && stars >= task.Cost;
                Image card = UIFactory.Panel("Task", gridRect, done ? Theme.Hex("1F5C3A") : Theme.Panel);
                UiKit.CardFrame(card);
                card.gameObject.AddComponent<PopIn>().Delay = i * 0.04f;

                Sprite art = UiKit.Art(TaskIcons[i % TaskIcons.Length]);
                if (art != null)
                {
                    Image icon = UIFactory.Icon(card.transform, art, done ? Color.white : new Color(0.85f, 0.85f, 0.95f, 1f), 0);
                    icon.preserveAspect = true;
                    UIFactory.Anchor(icon.rectTransform, 0.03f, 0.4f, 0.27f, 0.95f);
                }
                Text name = UIFactory.Label(card.transform, TaskName(zone.Number, i + 1), Theme.SmallSize - 6, done ? Theme.Success : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, 0.29f, 0.45f, 0.97f, 0.95f);
                Widgets.TitleOutline(name);

                if (done)
                {
                    Text check = UIFactory.Label(card.transform, "✔ " + Loc.T("kingdom.built"), Theme.SmallSize - 4, Theme.Success, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Anchor(check.rectTransform, 0.05f, 0.05f, 0.95f, 0.4f);
                    continue;
                }
                string id = task.Id;
                Button build = UIFactory.Button(card.transform, Loc.T("kingdom.build", task.Cost), () => _ = BuildAsync(id), affordable ? Theme.Success : Theme.PanelLight, Theme.SmallSize - 4);
                UIFactory.Anchor(build.GetComponent<RectTransform>(), 0.08f, 0.06f, 0.92f, 0.42f);
                if (affordable)
                {
                    build.gameObject.AddComponent<Breathe>().Amount = 0.03f;
                }
            }
        }

        /// <summary>Literal keys so the localization validator sees every task name.</summary>
        private string TaskName(int zone, int task)
        {
            switch (zone * 10 + task)
            {
                case 11: return Loc.T("kingdom.z1.t1");
                case 12: return Loc.T("kingdom.z1.t2");
                case 13: return Loc.T("kingdom.z1.t3");
                case 14: return Loc.T("kingdom.z1.t4");
                case 15: return Loc.T("kingdom.z1.t5");
                case 16: return Loc.T("kingdom.z1.t6");
                case 21: return Loc.T("kingdom.z2.t1");
                case 22: return Loc.T("kingdom.z2.t2");
                case 23: return Loc.T("kingdom.z2.t3");
                case 24: return Loc.T("kingdom.z2.t4");
                case 25: return Loc.T("kingdom.z2.t5");
                case 26: return Loc.T("kingdom.z2.t6");
                case 31: return Loc.T("kingdom.z3.t1");
                case 32: return Loc.T("kingdom.z3.t2");
                case 33: return Loc.T("kingdom.z3.t3");
                case 34: return Loc.T("kingdom.z3.t4");
                case 35: return Loc.T("kingdom.z3.t5");
                case 36: return Loc.T("kingdom.z3.t6");
                case 41: return Loc.T("kingdom.z4.t1");
                case 42: return Loc.T("kingdom.z4.t2");
                case 43: return Loc.T("kingdom.z4.t3");
                case 44: return Loc.T("kingdom.z4.t4");
                case 45: return Loc.T("kingdom.z4.t5");
                case 46: return Loc.T("kingdom.z4.t6");
                case 51: return Loc.T("kingdom.z5.t1");
                case 52: return Loc.T("kingdom.z5.t2");
                case 53: return Loc.T("kingdom.z5.t3");
                case 54: return Loc.T("kingdom.z5.t4");
                case 55: return Loc.T("kingdom.z5.t5");
                default: return Loc.T("kingdom.z5.t6");
            }
        }

        private void BuildFinished(RectTransform body)
        {
            Sprite art = ZoneArt(Restoration.Zones.Count, true);
            if (art != null)
            {
                Image scene = UIFactory.Icon(body, art, Color.white, 0);
                scene.preserveAspect = true;
                UIFactory.Anchor(scene.rectTransform, 0.02f, 0.4f, 0.98f, 0.95f);
            }
            Text done = UIFactory.Label(body, Loc.T("kingdom.finished"), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(done.rectTransform, 0.05f, 0.2f, 0.95f, 0.38f);
            Widgets.TitleOutline(done);
        }

        private async Task BuildAsync(string taskId)
        {
            RestorationDto data = Game.Backend.Profile?.Story?.Restoration;
            RestorationTask task = Restoration.FindTask(taskId);
            if (_busy || data == null || task == null)
            {
                return;
            }
            if (data.StarsAvailable < task.Cost)
            {
                UI.Toast(Loc.T("kingdom.needStars", task.Cost - data.StarsAvailable), 3f);
                return;
            }
            _busy = true;
            RestorationBuildResponse response = await Api(api => api.BuildRestorationAsync(taskId));
            _busy = false;
            if (response == null || this == null)
            {
                return;
            }
            ProfileDto profile = Game.Backend.Profile;
            if (profile?.Story != null)
            {
                profile.Story.Restoration = response.Restoration;
                profile.Pets = response.Pets ?? profile.Pets;
            }
            Game.Backend.ApplyWallet(response.Wallet);
            Game.Backend.ApplyInventory(response.Inventory);
            Game.Telemetry.Track("restoration_build", ("task", taskId), ("zone", response.ZoneCompleted));

            _justBuilt = taskId;
            if (response.ZoneCompleted > 0)
            {
                // Show the finished zone first (with the last piece appearing), then the celebration.
                RestorationZone finished = Restoration.Zones[response.ZoneCompleted - 1];
                var shownRestoration = new RestorationDto
                {
                    StarsAvailable = response.Restoration.StarsAvailable,
                    StarsSpent = response.Restoration.StarsSpent,
                    Built = response.Restoration.Built,
                    CurrentZone = finished.Number,
                    CanBuild = response.Restoration.CanBuild
                };
                if (profile?.Story != null)
                {
                    profile.Story.Restoration = shownRestoration;
                }
                Rebuild();
                await Task.Delay(1600);
                if (this == null)
                {
                    return;
                }
                Game.Audio.PlaySFX(SoundIds.WinFanfare);
                List<RevealItem> items = RevealOverlay.FromReward(response.Reward);
                if (response.PetFragments > 0 && !string.IsNullOrEmpty(response.FragmentsPet))
                {
                    items.Add(new RevealItem { Art = PetsScreen.PetArt(response.FragmentsPet), Caption = Loc.T("pets.fragmentsGain", response.PetFragments), Rare = true });
                }
                Sprite chest = UiKit.Art("chest_open_gold");
                await RevealOverlay.PlayChestAsync(items, ChestBar.ChestArt("gold") ?? chest, chest, Loc.T("kingdom.zoneDone", ZoneName(finished.Number)));
                if (profile?.Story != null)
                {
                    profile.Story.Restoration = response.Restoration;
                }
                if (this != null)
                {
                    Rebuild();
                }
                return;
            }
            Rebuild();
        }
    }
}
