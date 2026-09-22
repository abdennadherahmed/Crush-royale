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
    /// (dust burst and sparkles when it appears), the stars to spend, and the repair tasks. The last four tasks of a
    /// zone bring it to life (water, banners, torches, smoke, lights) and keep looping afterwards. Finishing a zone
    /// plays a celebration, pays its reward and opens the next one. All motion is dropped under reduced motion.
    /// </summary>
    public sealed class KingdomScreen : UIScreen
    {
        /// <summary>
        /// Where each structure task appears on the zone painting (normalized, origin bottom left), per zone then task.
        /// Matches the Art/Restoration/z{n}_restored paintings.
        /// </summary>
        private static readonly Rect[][] Regions =
        {
            new[] { new Rect(0.4f, 0.2f, 0.28f, 0.47f), new Rect(0f, 0.22f, 0.42f, 0.43f), new Rect(0.08f, 0.68f, 0.34f, 0.32f), new Rect(0.12f, 0f, 0.74f, 0.24f), new Rect(0.68f, 0.4f, 0.19f, 0.6f), new Rect(0.85f, 0.03f, 0.15f, 0.69f) },
            new[] { new Rect(0.33f, 0.03f, 0.47f, 0.47f), new Rect(0.05f, 0.3f, 0.5f, 0.3f), new Rect(0.55f, 0.45f, 0.45f, 0.3f), new Rect(0.84f, 0.58f, 0.16f, 0.42f), new Rect(0.48f, 0.45f, 0.16f, 0.46f), new Rect(0f, 0.2f, 0.28f, 0.48f) },
            new[] { new Rect(0.15f, 0f, 0.7f, 0.28f), new Rect(0f, 0.12f, 0.37f, 0.46f), new Rect(0.41f, 0.44f, 0.17f, 0.38f), new Rect(0.55f, 0.14f, 0.31f, 0.31f), new Rect(0.72f, 0.35f, 0.28f, 0.65f), new Rect(0.02f, 0.56f, 0.27f, 0.37f) },
            new[] { new Rect(0.41f, 0.1f, 0.21f, 0.25f), new Rect(0.34f, 0.05f, 0.12f, 0.6f), new Rect(0.54f, 0.33f, 0.12f, 0.24f), new Rect(0.55f, 0.59f, 0.1f, 0.19f), new Rect(0.45f, 0.43f, 0.1f, 0.19f), new Rect(0.44f, 0.7f, 0.12f, 0.3f) },
            new[] { new Rect(0f, 0.12f, 0.32f, 0.88f), new Rect(0.68f, 0.12f, 0.32f, 0.88f), new Rect(0.22f, 0f, 0.56f, 0.32f), new Rect(0.4f, 0.8f, 0.2f, 0.2f), new Rect(0.3f, 0.25f, 0.4f, 0.28f), new Rect(0.44f, 0.52f, 0.12f, 0.16f) }
        };

        /// <summary>
        /// Where the four "living" tasks of each zone hang their decoration (normalized point on the painting), in task
        /// order 7 to 10: the water, the banners, the fire and the crowning lights of that zone.
        /// </summary>
        private static readonly Vector2[][] Anchors =
        {
            new[] { new Vector2(0.53f, 0.36f), new Vector2(0.24f, 0.78f), new Vector2(0.8f, 0.34f), new Vector2(0.5f, 0.84f) },
            new[] { new Vector2(0.5f, 0.14f), new Vector2(0.9f, 0.82f), new Vector2(0.14f, 0.5f), new Vector2(0.56f, 0.7f) },
            new[] { new Vector2(0.68f, 0.26f), new Vector2(0.2f, 0.4f), new Vector2(0.12f, 0.74f), new Vector2(0.5f, 0.74f) },
            new[] { new Vector2(0.6f, 0.42f), new Vector2(0.4f, 0.3f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.6f) },
            new[] { new Vector2(0.14f, 0.55f), new Vector2(0.84f, 0.45f), new Vector2(0.3f, 0.86f), new Vector2(0.5f, 0.62f) }
        };


        /// <summary>The scene is laid out at roughly this many units, used to size the feathering and the decorations.</summary>
        private const float SceneWidth = 1000f;

        private const float SceneHeight = 560f;

        private string _justBuilt;
        private bool _celebrateZone;
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

            BuildHeader(body, zone, built, data.StarsAvailable);
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

        /// <summary>Zone ribbon, then one line that answers "where am I in the whole restoration" and the star purse.</summary>
        private void BuildHeader(RectTransform body, RestorationZone zone, HashSet<string> built, int stars)
        {
            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", body), 0.02f, 0.925f, 0.98f, 1f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, Loc.T("kingdom.zone", zone.Number, Restoration.Zones.Count, ZoneName(zone.Number)), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.16f, 0.34f, 0.84f, 0.92f);
            Widgets.TitleOutline(title);

            int doneInZone = 0;
            foreach (RestorationTask task in zone.Tasks)
            {
                doneInZone += built.Contains(task.Id) ? 1 : 0;
            }
            int doneOverall = 0;
            foreach (RestorationZone z in Restoration.Zones)
            {
                foreach (RestorationTask task in z.Tasks)
                {
                    doneOverall += built.Contains(task.Id) ? 1 : 0;
                }
            }

            // "Zone 2/5 - 14/50 sites - 7 stars available": the whole restoration at a glance, not just this zone.
            Text header = UIFactory.Label(body, Loc.T("kingdom.header", zone.Number, Restoration.Zones.Count, doneOverall, Restoration.TotalTasks, Loc.Number(stars)),
                Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(header.rectTransform, 0.02f, 0.893f, 0.98f, 0.925f);

            UiKit.Bar(body, doneInZone / (float)zone.Tasks.Count, Theme.Success, out RectTransform bar);
            UIFactory.Anchor(bar, 0.04f, 0.845f, 0.62f, 0.888f);
            Text barText = UIFactory.Label(bar, Loc.T("kingdom.progress", doneInZone, zone.Tasks.Count), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(barText.rectTransform);
            Widgets.TitleOutline(barText);

            Image starChip = UIFactory.Panel("Stars", body, new Color(0.05f, 0.03f, 0.12f, 0.85f));
            UIFactory.Anchor(starChip.rectTransform, 0.65f, 0.84f, 0.96f, 0.893f);
            Sprite starArt = UiKit.Art("item_stars") ?? ArtLibrary.Icon("star");
            if (starArt != null)
            {
                Image icon = UIFactory.Icon(starChip.transform, starArt, Color.white, 0);
                icon.preserveAspect = true;
                UIFactory.Anchor(icon.rectTransform, 0.02f, -0.1f, 0.34f, 1.1f);
                if (!KingdomFx.ReduceMotion)
                {
                    icon.gameObject.AddComponent<Breathe>().Amount = 0.05f;
                }
            }
            Text starText = UIFactory.Label(starChip.transform, Loc.Number(stars), Theme.HeaderSize - 4, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(starText.rectTransform, 0.34f, 0f, 0.98f, 1f);
            Widgets.TitleOutline(starText);
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

            bool zoneDone = zone.Tasks.TrueForAll(t => built.Contains(t.Id));
            if (zoneDone && restored != null)
            {
                // Finished zone: the whole painting comes back to life (sky, background, light).
                baseSprite = restored;
            }
            Image baseImage = art.gameObject.AddComponent<Image>();
            baseImage.sprite = baseSprite;
            baseImage.raycastTarget = false;
            // Without the ruined painting, the restored one (or the kingdom) is shown dark and grey.
            baseImage.color = ruined != null || zoneDone ? Color.white : new Color(0.32f, 0.28f, 0.36f, 1f);
            Sprite reveal = restored ?? fallback;

            for (int i = 0; i < zone.Tasks.Count; i++)
            {
                RestorationTask task = zone.Tasks[i];
                if (!built.Contains(task.Id))
                {
                    continue;
                }
                if (task.Effect == RestorationEffect.Structure)
                {
                    RectTransform window = RevealWindow(art, zone.Number, i, reveal);
                    if (window != null && task.Id == _justBuilt)
                    {
                        StartCoroutine(RevealPiece(window, art));
                    }
                    continue;
                }

                // Living decorations hang on top of the painting and keep looping for good.
                Vector2 anchor = Anchors[zone.Number - 1][Mathf.Clamp(i - 6, 0, 3)];
                KingdomFx.Spawn(art, task.Effect, anchor, SceneHeight * 0.16f);
                if (task.Id == _justBuilt)
                {
                    StartCoroutine(RevealDecoration(art, anchor));
                }
            }

            // Floating dust over the ruins, sparkles over the restored parts (skipped under reduced motion).
            if (!KingdomFx.ReduceMotion)
            {
                for (int i = 0; i < 10; i++)
                {
                    Image mote = UIFactory.Icon(art, ProceduralSprites.Spark(), new Color(1f, 0.95f, 0.7f, 0.7f), 26);
                    mote.rectTransform.anchorMin = mote.rectTransform.anchorMax = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value);
                    mote.raycastTarget = false;
                    mote.gameObject.AddComponent<KingdomFx.Dust>();
                }
            }

            if (_celebrateZone)
            {
                _celebrateZone = false;
                StartCoroutine(CelebrateZone(art));
            }
        }

        /// <summary>Feathered window showing the restored painting through the ruins, for one structure task.</summary>
        private static RectTransform RevealWindow(RectTransform art, int zoneNumber, int taskIndex, Sprite reveal)
        {
            if (reveal == null || taskIndex >= Regions[zoneNumber - 1].Length)
            {
                return null;
            }
            Rect r = Regions[zoneNumber - 1][taskIndex];
            RectTransform window = UIFactory.Anchor(UIFactory.Rect("Built" + taskIndex, art), r.xMin, r.yMin, r.xMax, r.yMax);
            // Feather about a fifth of the smaller side (the scene is roughly 1000 x 560 units).
            Feather(window.gameObject.AddComponent<RectMask2D>(), Mathf.RoundToInt(Mathf.Min(48f, Mathf.Min(r.width * SceneWidth, r.height * SceneHeight) * 0.2f)));
            RectTransform piece = UIFactory.Rect("Restored", window);
            piece.anchorMin = new Vector2(-r.xMin / r.width, -r.yMin / r.height);
            piece.anchorMax = new Vector2((1f - r.xMin) / r.width, (1f - r.yMin) / r.height);
            piece.offsetMin = piece.offsetMax = Vector2.zero;
            Image image = piece.gameObject.AddComponent<Image>();
            image.sprite = reveal;
            image.raycastTarget = false;
            return window;
        }

        /// <summary>
        /// Soft mask edges so a repaired part blends into the ruins instead of showing a hard rectangle.
        /// RectMask2D.softness exists since Unity 2020; set by reflection so older API stubs still compile.
        /// </summary>
        private static void Feather(RectMask2D mask, int pixels)
        {
            System.Reflection.PropertyInfo softness = typeof(RectMask2D).GetProperty("softness");
            if (softness != null && softness.CanWrite)
            {
                softness.SetValue(mask, new Vector2Int(pixels, pixels));
            }
        }

        private IEnumerator RevealPiece(RectTransform window, RectTransform art)
        {
            _justBuilt = null;
            if (KingdomFx.ReduceMotion)
            {
                // Reduced motion: the piece is simply there, with the sound that says the build landed.
                Game.Audio.PlaySFX(SoundIds.Sparkle);
                yield break;
            }
            CanvasGroup group = window.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            Image burst = UIFactory.Icon(art, ProceduralSprites.Glow(128), new Color(1f, 0.9f, 0.5f, 1f), 0);
            burst.raycastTarget = false;
            burst.rectTransform.anchorMin = window.anchorMin;
            burst.rectTransform.anchorMax = window.anchorMax;
            burst.rectTransform.offsetMin = burst.rectTransform.offsetMax = Vector2.zero;
            var sparks = new List<(RectTransform Rect, Vector2 Velocity)>();
            Vector2 center = (window.anchorMin + window.anchorMax) / 2f;
            // Grey dust falls off the rebuilt stone while the golden sparks fly up: a construction, not a fade-in.
            for (int i = 0; i < 24; i++)
            {
                bool dust = i % 3 == 2;
                Image spark = UIFactory.Icon(art, ProceduralSprites.Spark(), dust ? new Color(0.75f, 0.7f, 0.66f, 0.85f) : i % 2 == 0 ? Theme.Gold : Color.white, dust ? 54 : 40);
                spark.raycastTarget = false;
                spark.rectTransform.anchorMin = spark.rectTransform.anchorMax = center;
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                Vector2 velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * UnityEngine.Random.Range(180f, 420f);
                sparks.Add((spark.rectTransform, dust ? new Vector2(velocity.x * 0.6f, -Mathf.Abs(velocity.y) * 0.5f) : velocity));
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

        /// <summary>A decoration switching on: a flash at its anchor, then it keeps looping on its own.</summary>
        private IEnumerator RevealDecoration(RectTransform art, Vector2 anchor)
        {
            _justBuilt = null;
            if (KingdomFx.ReduceMotion)
            {
                Game.Audio.PlaySFX(SoundIds.Sparkle);
                yield break;
            }
            Image flash = UIFactory.Icon(art, ProceduralSprites.Glow(128), new Color(1f, 0.95f, 0.7f, 1f), 0);
            flash.raycastTarget = false;
            flash.rectTransform.anchorMin = flash.rectTransform.anchorMax = anchor;
            flash.rectTransform.sizeDelta = new Vector2(SceneHeight * 0.5f, SceneHeight * 0.5f);
            flash.rectTransform.anchoredPosition = Vector2.zero;
            Game.Audio.PlaySFX(SoundIds.Whoosh);
            const float duration = 0.8f;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                flash.color = new Color(1f, 0.95f, 0.7f, 1f - k);
                flash.rectTransform.localScale = Vector3.one * (0.4f + k * 1.4f);
                yield return null;
            }
            Destroy(flash.gameObject);
            Game.Audio.PlaySFX(SoundIds.Sparkle);
        }

        /// <summary>Zone finished: light sweeps over the whole painting and gold rains on it before the chest opens.</summary>
        private IEnumerator CelebrateZone(RectTransform art)
        {
            if (KingdomFx.ReduceMotion)
            {
                yield break;
            }
            Image wash = UIFactory.Icon(art, ProceduralSprites.Glow(128), new Color(1f, 0.95f, 0.75f, 0f), 0);
            wash.raycastTarget = false;
            UIFactory.Stretch(wash.rectTransform, -60, -60, -60, -60);
            var confetti = new List<(RectTransform Rect, float Speed, float Sway)>();
            for (int i = 0; i < 22; i++)
            {
                Image piece = UIFactory.Icon(art, ProceduralSprites.Spark(), i % 3 == 0 ? Theme.Gold : i % 3 == 1 ? Theme.Crystal : Color.white, 34);
                piece.raycastTarget = false;
                piece.rectTransform.anchorMin = piece.rectTransform.anchorMax = new Vector2(UnityEngine.Random.value, 1.1f);
                piece.rectTransform.anchoredPosition = Vector2.zero;
                confetti.Add((piece.rectTransform, UnityEngine.Random.Range(260f, 520f), UnityEngine.Random.Range(20f, 70f)));
            }
            const float duration = 1.5f;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                wash.color = new Color(1f, 0.95f, 0.75f, 0.45f * Mathf.Sin(k * Mathf.PI));
                foreach ((RectTransform rect, float speed, float sway) in confetti)
                {
                    if (rect != null)
                    {
                        rect.anchoredPosition = new Vector2(Mathf.Sin(t * 3f + speed) * sway, -speed * t);
                        rect.localRotation = Quaternion.Euler(0f, 0f, t * speed * 0.4f);
                    }
                }
                yield return null;
            }
            Destroy(wash.gameObject);
            foreach ((RectTransform rect, float _, float __) in confetti)
            {
                if (rect != null)
                {
                    Destroy(rect.gameObject);
                }
            }
        }

        private void BuildTasks(RectTransform body, RestorationZone zone, HashSet<string> built, int stars)
        {
            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Tasks", body), 0.02f, 0.01f, 0.98f, 0.45f);
            RectTransform list = UIFactory.ScrollList(holder, 12, 8);

            for (int i = 0; i < zone.Tasks.Count; i++)
            {
                RestorationTask task = zone.Tasks[i];
                bool done = built.Contains(task.Id);
                bool affordable = !done && stars >= task.Cost;
                Image card = UIFactory.Panel("Task", list, done ? Theme.Hex("1F5C3A") : Theme.Panel);
                card.gameObject.AddComponent<LayoutElement>().preferredHeight = 150;
                UiKit.CardFrame(card);
                if (!KingdomFx.ReduceMotion)
                {
                    card.gameObject.AddComponent<PopIn>().Delay = i * 0.03f;
                }

                RectTransform badge = UIFactory.Anchor(UIFactory.Rect("Step", card.transform), 0.02f, 0.14f, 0.14f, 0.86f);
                badge.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                UiKit.RoundBadge(badge, crystal: !done);
                Text step = UIFactory.Label(badge, (i + 1).ToString(), Theme.SmallSize, done ? Theme.Success : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(step.rectTransform);
                Widgets.TitleOutline(step);
                Text name = UIFactory.Label(card.transform, TaskName(zone.Number, i + 1), Theme.SmallSize - 4, done ? Theme.Success : Theme.Text, TextAnchor.LowerLeft, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, 0.16f, 0.5f, 0.68f, 0.92f);
                Widgets.TitleOutline(name);

                // Exactly what this build unlocks: the visible change it makes to the scene.
                string promiseText = task.Effect == RestorationEffect.Structure
                    ? Loc.T("kingdom.fx.step", i + 1, StructureSteps(zone))
                    : EffectText(task.Effect);
                Text promise = UIFactory.Label(card.transform, promiseText, Theme.SmallSize - 10, done ? Theme.Text : Theme.TextMuted, TextAnchor.UpperLeft);
                UIFactory.Anchor(promise.rectTransform, 0.16f, 0.1f, 0.68f, 0.5f);

                if (done)
                {
                    Text check = UIFactory.Label(card.transform, "✔ " + Loc.T("kingdom.built"), Theme.SmallSize - 4, Theme.Success, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Anchor(check.rectTransform, 0.7f, 0.2f, 0.98f, 0.8f);
                    continue;
                }
                string id = task.Id;
                Button build = UIFactory.Button(card.transform, Loc.T("kingdom.build", task.Cost), () => _ = BuildAsync(id), affordable ? Theme.Success : Theme.PanelLight, Theme.SmallSize - 4);
                UIFactory.Anchor(build.GetComponent<RectTransform>(), 0.7f, affordable ? 0.2f : 0.42f, 0.98f, 0.8f);
                if (affordable && !KingdomFx.ReduceMotion)
                {
                    build.gameObject.AddComponent<Breathe>().Amount = 0.03f;
                }
                if (!affordable)
                {
                    Text missing = UIFactory.Label(card.transform, Loc.T("kingdom.missing", task.Cost - stars), Theme.SmallSize - 10, Theme.Danger, TextAnchor.MiddleCenter);
                    UIFactory.Anchor(missing.rectTransform, 0.7f, 0.12f, 0.98f, 0.4f);
                }
            }
        }

        /// <summary>How many of the zone's tasks rebuild its structures (the rest bring the picture to life).</summary>
        private static int StructureSteps(RestorationZone zone)
        {
            int count = 0;
            foreach (RestorationTask task in zone.Tasks)
            {
                if (task.Effect == RestorationEffect.Structure)
                {
                    count++;
                }
            }
            return Math.Max(1, count);
        }

        /// <summary>Literal keys so the localization validator sees every promise line.</summary>
        private string EffectText(RestorationEffect effect)
        {
            switch (effect)
            {
                case RestorationEffect.Water: return Loc.T("kingdom.fx.water");
                case RestorationEffect.Banner: return Loc.T("kingdom.fx.banner");
                case RestorationEffect.Torch: return Loc.T("kingdom.fx.torch");
                case RestorationEffect.Smoke: return Loc.T("kingdom.fx.smoke");
                case RestorationEffect.Radiance: return Loc.T("kingdom.fx.radiance");
                default: return Loc.T("kingdom.fx.structure");
            }
        }

        /// <summary>Literal keys so the localization validator sees every task name.</summary>
        private string TaskName(int zone, int task)
        {
            switch (zone * 100 + task)
            {
                case 101: return Loc.T("kingdom.z1.t1");
                case 102: return Loc.T("kingdom.z1.t2");
                case 103: return Loc.T("kingdom.z1.t3");
                case 104: return Loc.T("kingdom.z1.t4");
                case 105: return Loc.T("kingdom.z1.t5");
                case 106: return Loc.T("kingdom.z1.t6");
                case 107: return Loc.T("kingdom.z1.t7");
                case 108: return Loc.T("kingdom.z1.t8");
                case 109: return Loc.T("kingdom.z1.t9");
                case 110: return Loc.T("kingdom.z1.t10");
                case 201: return Loc.T("kingdom.z2.t1");
                case 202: return Loc.T("kingdom.z2.t2");
                case 203: return Loc.T("kingdom.z2.t3");
                case 204: return Loc.T("kingdom.z2.t4");
                case 205: return Loc.T("kingdom.z2.t5");
                case 206: return Loc.T("kingdom.z2.t6");
                case 207: return Loc.T("kingdom.z2.t7");
                case 208: return Loc.T("kingdom.z2.t8");
                case 209: return Loc.T("kingdom.z2.t9");
                case 210: return Loc.T("kingdom.z2.t10");
                case 301: return Loc.T("kingdom.z3.t1");
                case 302: return Loc.T("kingdom.z3.t2");
                case 303: return Loc.T("kingdom.z3.t3");
                case 304: return Loc.T("kingdom.z3.t4");
                case 305: return Loc.T("kingdom.z3.t5");
                case 306: return Loc.T("kingdom.z3.t6");
                case 307: return Loc.T("kingdom.z3.t7");
                case 308: return Loc.T("kingdom.z3.t8");
                case 309: return Loc.T("kingdom.z3.t9");
                case 310: return Loc.T("kingdom.z3.t10");
                case 401: return Loc.T("kingdom.z4.t1");
                case 402: return Loc.T("kingdom.z4.t2");
                case 403: return Loc.T("kingdom.z4.t3");
                case 404: return Loc.T("kingdom.z4.t4");
                case 405: return Loc.T("kingdom.z4.t5");
                case 406: return Loc.T("kingdom.z4.t6");
                case 407: return Loc.T("kingdom.z4.t7");
                case 408: return Loc.T("kingdom.z4.t8");
                case 409: return Loc.T("kingdom.z4.t9");
                case 410: return Loc.T("kingdom.z4.t10");
                case 501: return Loc.T("kingdom.z5.t1");
                case 502: return Loc.T("kingdom.z5.t2");
                case 503: return Loc.T("kingdom.z5.t3");
                case 504: return Loc.T("kingdom.z5.t4");
                case 505: return Loc.T("kingdom.z5.t5");
                case 506: return Loc.T("kingdom.z5.t6");
                case 507: return Loc.T("kingdom.z5.t7");
                case 508: return Loc.T("kingdom.z5.t8");
                case 509: return Loc.T("kingdom.z5.t9");
                default: return Loc.T("kingdom.z5.t10");
            }
        }

        private void BuildFinished(RectTransform body)
        {
            Sprite art = ZoneArt(Restoration.Zones.Count, true);
            RectTransform scene = null;
            if (art != null)
            {
                Image image = UIFactory.Icon(body, art, Color.white, 0);
                image.preserveAspect = true;
                scene = UIFactory.Anchor(image.rectTransform, 0.02f, 0.4f, 0.98f, 0.95f);
            }
            if (scene != null && !KingdomFx.ReduceMotion)
            {
                // The rebuilt capital never goes quiet again: the throne hall keeps its water, fire and crown lights.
                KingdomFx.Spawn(scene, RestorationEffect.Water, new Vector2(0.14f, 0.35f), SceneHeight * 0.16f);
                KingdomFx.Spawn(scene, RestorationEffect.Torch, new Vector2(0.84f, 0.35f), SceneHeight * 0.16f);
                KingdomFx.Spawn(scene, RestorationEffect.Radiance, new Vector2(0.5f, 0.62f), SceneHeight * 0.16f);
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
                // Show the finished zone first (with the last piece appearing and the confetti), then the chest.
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
                _celebrateZone = true;
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
