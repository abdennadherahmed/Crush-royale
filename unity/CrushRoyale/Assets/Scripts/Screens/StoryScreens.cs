using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// World map: the illustrated continent with its 5 kingdoms (acts) on top, then the selected chapter as a winding
    /// path of 20 stage medallions over the kingdom's scenery (stars, bosses, the hero on the current stage, friends).
    /// </summary>
    public sealed class WorldMapScreen : UIScreen
    {
        /// <summary>Offline practice lets players try the first stages without an account.</summary>
        public const int OfflineStageCap = 50;

        private const float NodeSpacing = 250f;

        // Kingdom hotspots on the world map illustration (normalized), in act order (North, East, West, South, Central).
        private static readonly Vector2[] KingdomSpots =
        {
            new Vector2(0.56f, 0.76f), new Vector2(0.74f, 0.58f), new Vector2(0.22f, 0.56f), new Vector2(0.5f, 0.22f), new Vector2(0.5f, 0.47f)
        };

        private int _act = -1;
        private int _chapter = -1;
        private Dictionary<int, List<string>> _friendsByStage = new Dictionary<int, List<string>>();

        public override System.Type BackTarget => typeof(MainMenuScreen);

        // Acts 1-5 are the kingdoms in enum order (North, East, West, South, Central).
        protected override Kingdom BackdropKingdom => (Kingdom)Mathf.Clamp(_act - 1, 0, 4);

        private int HighestUnlocked => Game.Backend.IsOnline
            ? Game.Backend.Profile.Story.HighestUnlockedStage
            : Mathf.Clamp(Game.Save.Settings.LastSeenStage, 1, OfflineStageCap);

        protected override void Build()
        {
            StoryBalance story = Game.Backend.Balance.Story;
            int stagesPerAct = story.ChaptersPerAct * story.StagesPerChapter;
            int highest = Mathf.Min(HighestUnlocked, story.TotalStages);
            if (_act < 1)
            {
                _act = Mathf.Clamp((highest - 1) / stagesPerAct + 1, 1, story.Acts);
            }
            if (_chapter < 1)
            {
                int currentChapter = (highest - 1) / story.StagesPerChapter + 1;
                int firstOfAct = (_act - 1) * story.ChaptersPerAct + 1;
                _chapter = Mathf.Clamp(currentChapter, firstOfAct, firstOfAct + story.ChaptersPerAct - 1);
            }

            RectTransform body = Frame("map.title");
            BuildWorld(body, story, highest);
            BuildChapterBar(body, story, highest);
            BuildPath(body, story, highest);
        }

        public override async Task OnShownAsync()
        {
            if (!Game.Backend.IsOnline || !Widgets.FeatureUnlocked("Friends"))
            {
                return;
            }
            WorldMapResponse map = await Api(api => api.GetWorldMapAsync(), loading: false);
            if (map == null || this == null)
            {
                return;
            }
            _friendsByStage = map.Friends.GroupBy(f => f.Stage).ToDictionary(g => g.Key, g => g.Select(f => f.DisplayName).ToList());
            Rebuild();
        }

        private void BuildWorld(RectTransform body, StoryBalance story, int highest)
        {
            Image frame = UIFactory.Panel("World", body, Theme.Panel);
            UIFactory.Anchor(frame.rectTransform, 0.02f, 0.67f, 0.98f, 1f);
            UiKit.FramePanel(frame);
            RectTransform inner = UIFactory.Stretch(UIFactory.Rect("Inner", frame.transform), 22, 22, 22, 22);
            inner.gameObject.AddComponent<RectMask2D>();
            Sprite world = ArtLibrary.Load("Art/Backgrounds/world_map");
            RectTransform pins = inner;
            if (world != null)
            {
                Image map = UIFactory.Icon(inner, world, Color.white, 0);
                pins = map.rectTransform;
                UIFactory.Stretch(map.rectTransform);
                map.preserveAspect = false;
                map.rectTransform.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                map.rectTransform.GetComponent<AspectRatioFitter>().aspectRatio = world.rect.width / world.rect.height;
            }
            else
            {
                Widgets.Backdrop(inner, Kingdom.Central, 0.8f);
            }

            int stagesPerAct = story.ChaptersPerAct * story.StagesPerChapter;
            for (int act = 1; act <= story.Acts && act <= KingdomSpots.Length; act++)
            {
                int a = act;
                bool unlocked = (act - 1) * stagesPerAct + 1 <= highest;
                Vector2 spot = KingdomSpots[act - 1];
                RectTransform pin = UIFactory.Rect("Kingdom" + act, pins);
                pin.anchorMin = pin.anchorMax = spot;
                pin.sizeDelta = new Vector2(act == _act ? 150 : 120, act == _act ? 150 : 120);
                Button button = pin.gameObject.AddComponent<Button>();
                Image hit = pin.gameObject.AddComponent<Image>();
                hit.color = new Color(0, 0, 0, 0);
                button.targetGraphic = hit;
                button.onClick.AddListener(() =>
                {
                    if (!unlocked)
                    {
                        UI.Toast(Loc.T("map.kingdomLocked"));
                        return;
                    }
                    _act = a;
                    _chapter = (a - 1) * story.ChaptersPerAct + 1;
                    int currentChapter = (highest - 1) / story.StagesPerChapter + 1;
                    if (currentChapter >= _chapter && currentChapter < _chapter + story.ChaptersPerAct)
                    {
                        _chapter = currentChapter;
                    }
                    Rebuild();
                });
                pin.gameObject.AddComponent<ButtonFeedback>();

                UiKit.RoundBadge(pin, crystal: act != _act);
                if (act == _act)
                {
                    Image ring = UIFactory.Icon(pin, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.35f, 0.9f), 0);
                    UIFactory.Stretch(ring.rectTransform, -40, -40, -40, -40);
                    ring.gameObject.AddComponent<Pulse>();
                    ring.transform.SetAsFirstSibling();
                }
                if (!unlocked)
                {
                    Sprite lockArt = UiKit.Art("item_lock") ?? ArtLibrary.Icon("lock");
                    if (lockArt != null)
                    {
                        Image lockIcon = UIFactory.Icon(pin, lockArt, Color.white, 0);
                        UIFactory.Stretch(lockIcon.rectTransform, 28, 28, 22, 22);
                    }
                }
                else
                {
                    Text number = UIFactory.Label(pin, RomanAct(act), Theme.HeaderSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Stretch(number.rectTransform);
                    Widgets.TitleOutline(number);
                }
                Text name = UIFactory.Label(pin, Loc.T("kingdom." + (Kingdom)(act - 1)), Theme.SmallSize - 4, act == _act ? Theme.Gold : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, -0.8f, -0.42f, 1.8f, -0.02f);
                Widgets.TitleOutline(name);
            }
        }

        private static string RomanAct(int act)
        {
            switch (act)
            {
                case 1: return "I";
                case 2: return "II";
                case 3: return "III";
                case 4: return "IV";
                default: return "V";
            }
        }

        private void BuildChapterBar(RectTransform body, StoryBalance story, int highest)
        {
            int firstChapter = (_act - 1) * story.ChaptersPerAct + 1;
            int lastChapter = firstChapter + story.ChaptersPerAct - 1;
            int lastVisible = (highest - 1) / story.StagesPerChapter + 1;

            Image bar = UIFactory.Panel("Chapter", body, Theme.Panel);
            UIFactory.Anchor(bar.rectTransform, 0.02f, 0.575f, 0.98f, 0.665f);
            UiKit.CardFrame(bar);

            Button prev = UIFactory.Button(bar.transform, "<", () => { _chapter--; Rebuild(); }, _chapter > firstChapter ? Theme.Gold : Theme.PanelLight, Theme.HeaderSize);
            RectTransform prevRect = UIFactory.Anchor(prev.GetComponent<RectTransform>(), 0.02f, 0.14f, 0.14f, 0.86f);
            prevRect.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            prev.interactable = _chapter > firstChapter;
            Button next = UIFactory.Button(bar.transform, ">", () => { _chapter++; Rebuild(); }, _chapter < Mathf.Min(lastChapter, lastVisible) ? Theme.Gold : Theme.PanelLight, Theme.HeaderSize);
            RectTransform nextRect = UIFactory.Anchor(next.GetComponent<RectTransform>(), 0.86f, 0.14f, 0.98f, 0.86f);
            nextRect.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            next.interactable = _chapter < Mathf.Min(lastChapter, lastVisible);

            Text title = UIFactory.Label(bar.transform, Loc.T("chapter.title", _chapter, Loc.T("chapter." + _chapter + ".name")), Theme.BodySize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.16f, 0.45f, 0.84f, 0.95f);
            Widgets.TitleOutline(title);

            string stars = Game.Backend.Profile?.Story?.StarsByStage ?? string.Empty;
            int firstStage = (_chapter - 1) * story.StagesPerChapter + 1;
            int earned = 0;
            for (int id = firstStage; id < firstStage + story.StagesPerChapter && id - 1 < stars.Length; id++)
            {
                earned += Mathf.Max(0, stars[id - 1] - '0');
            }
            Text starText = UIFactory.Label(bar.transform, "★ " + earned + " / " + story.StagesPerChapter * 3, Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(starText.rectTransform, 0.16f, 0.06f, 0.84f, 0.45f);
        }

        private void BuildPath(RectTransform body, StoryBalance story, int highest)
        {
            RectTransform viewport = UIFactory.Anchor(UIFactory.Rect("PathView", body), 0.02f, 0.01f, 0.98f, 0.565f);
            viewport.gameObject.AddComponent<RectMask2D>();
            Image hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0.25f);
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40;

            int count = story.StagesPerChapter;
            float height = (count + 1) * NodeSpacing;
            RectTransform content = UIFactory.Rect("Path", viewport);
            content.anchorMin = new Vector2(0, 0);
            content.anchorMax = new Vector2(1, 0);
            content.pivot = new Vector2(0.5f, 0);
            content.sizeDelta = new Vector2(0, height);
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;
            scroll.viewport = viewport;

            // Kingdom scenery, repeated (mirrored every other tile) along the path.
            Sprite scenery = ArtLibrary.Background(BackdropKingdom);
            if (scenery != null)
            {
                float tile = 1400f;
                for (int i = 0; i * tile < height; i++)
                {
                    RectTransform segment = UIFactory.Rect("Scenery", content);
                    segment.anchorMin = new Vector2(0, 0);
                    segment.anchorMax = new Vector2(1, 0);
                    segment.pivot = new Vector2(0.5f, 0);
                    segment.sizeDelta = new Vector2(0, tile);
                    segment.anchoredPosition = new Vector2(0, i * tile);
                    segment.gameObject.AddComponent<RectMask2D>();
                    Image image = UIFactory.Icon(segment, scenery, new Color(0.62f, 0.6f, 0.68f, 1f), 0);
                    UIFactory.Stretch(image.rectTransform);
                    AspectRatioFitter fitter = image.gameObject.AddComponent<AspectRatioFitter>();
                    fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                    fitter.aspectRatio = scenery.rect.width / scenery.rect.height;
                    if (i % 2 == 1)
                    {
                        image.rectTransform.localScale = new Vector3(1, -1, 1);
                    }
                }
            }

            string stars = Game.Backend.Profile?.Story?.StarsByStage ?? string.Empty;
            int firstStage = (_chapter - 1) * story.StagesPerChapter + 1;
            Vector2 previous = Vector2.zero;
            RectTransform currentNode = null;
            for (int i = 0; i < count; i++)
            {
                int stageId = firstStage + i;
                Vector2 position = new Vector2(Mathf.Sin(i * 0.9f) * 290f, NodeSpacing * (i + 0.8f));
                if (i > 0)
                {
                    Dots(content, previous, position, stageId <= highest);
                }
                RectTransform node = StageNode(content, stageId, position, highest, stars);
                if (stageId == highest)
                {
                    currentNode = node;
                }
                previous = position;
            }

            // Start scrolled so the current stage (or the chapter start) is visible.
            float target = currentNode != null ? currentNode.anchoredPosition.y : 0f;
            StartCoroutine(ScrollTo(scroll, target, height));
        }

        /// <summary>Waits one frame for the viewport size, then centers the path on <paramref name="y"/>.</summary>
        private static System.Collections.IEnumerator ScrollTo(ScrollRect scroll, float y, float height)
        {
            yield return null;
            if (scroll == null)
            {
                yield break;
            }
            float view = ((RectTransform)scroll.viewport).rect.height;
            scroll.verticalNormalizedPosition = Mathf.Clamp01((y - view * 0.4f) / Mathf.Max(1f, height - view));
        }

        private static void Dots(RectTransform content, Vector2 from, Vector2 to, bool reached)
        {
            for (int d = 1; d <= 4; d++)
            {
                Vector2 p = Vector2.Lerp(from, to, d / 5f);
                Image dot = UIFactory.Icon(content, ProceduralSprites.Circle(), reached ? Theme.Gold : new Color(0.1f, 0.08f, 0.2f, 0.85f), 18);
                dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                dot.rectTransform.anchoredPosition = p;
            }
        }

        private RectTransform StageNode(RectTransform content, int stageId, Vector2 position, int highest, string stars)
        {
            bool unlocked = stageId <= highest && (Game.Backend.IsOnline || stageId <= OfflineStageCap);
            bool current = stageId == highest;
            int starCount = stageId - 1 < stars.Length ? stars[stageId - 1] - '0' : 0;
            StageData stage = Game.Backend.Catalog.Get(stageId);
            float size = stage.IsBoss ? 190f : current ? 160f : 140f;

            RectTransform node = UIFactory.Rect("Stage" + stageId, content);
            node.anchorMin = node.anchorMax = new Vector2(0.5f, 0f);
            node.sizeDelta = new Vector2(size, size);
            node.anchoredPosition = position;
            Image hit = node.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            Button button = node.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            node.gameObject.AddComponent<ButtonFeedback>();
            int id = stageId;
            button.onClick.AddListener(() =>
            {
                if (unlocked)
                {
                    UI.Show<StagePreviewScreen>(id);
                }
                else
                {
                    UI.Toast(Loc.T("map.stageLocked"));
                }
            });

            Image badge = UiKit.RoundBadge(node, crystal: current || !unlocked);
            if (current)
            {
                Image glow = UIFactory.Icon(node, ProceduralSprites.Glow(128), new Color(0.45f, 0.9f, 1f, 0.9f), 0);
                UIFactory.Stretch(glow.rectTransform, -50, -50, -50, -50);
                glow.gameObject.AddComponent<Pulse>();
                glow.transform.SetAsFirstSibling();
            }
            if (badge != null && !unlocked)
            {
                badge.color = new Color(0.35f, 0.33f, 0.45f, 1f);
            }

            if (stage.IsBoss)
            {
                Sprite bossArt = ArtLibrary.Boss(stage);
                if (bossArt != null)
                {
                    Image boss = UIFactory.Icon(node, bossArt, unlocked ? Color.white : new Color(0.3f, 0.3f, 0.35f, 1f), 0);
                    UIFactory.Stretch(boss.rectTransform, 18, 18, 18, 18);
                }
                Text tag = UIFactory.Label(node, Loc.T("map.boss"), Theme.SmallSize - 2, Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(tag.rectTransform, -0.2f, 0.88f, 1.2f, 1.12f);
                Widgets.TitleOutline(tag);
            }
            Text number = UIFactory.Label(node, unlocked || stage.IsBoss ? stageId.ToString() : string.Empty, stage.IsBoss ? Theme.SmallSize : Theme.BodySize + 2, Theme.Text, stage.IsBoss ? TextAnchor.LowerCenter : TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(number.rectTransform, 0, 0, 0, stage.IsBoss ? 6 : 0);
            Widgets.TitleOutline(number);
            if (!unlocked && !stage.IsBoss)
            {
                Sprite lockArt = UiKit.Art("item_lock") ?? ArtLibrary.Icon("lock");
                if (lockArt != null)
                {
                    Image lockIcon = UIFactory.Icon(node, lockArt, new Color(1f, 1f, 1f, 0.9f), 0);
                    UIFactory.Stretch(lockIcon.rectTransform, size * 0.28f, size * 0.28f, size * 0.22f, size * 0.22f);
                }
            }

            // Stars under completed stages.
            if (starCount > 0)
            {
                RectTransform row = UIFactory.Anchor(UIFactory.Rect("Stars", node), 0.05f, -0.22f, 0.95f, 0.08f);
                HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = layout.childForceExpandHeight = true;
                Sprite star = ArtLibrary.Icon("star");
                for (int s = 0; s < 3; s++)
                {
                    if (star != null)
                    {
                        UIFactory.Icon(row, star, s < starCount ? Color.white : new Color(0.2f, 0.18f, 0.3f, 1f), 0);
                    }
                }
            }

            // The hero stands on the current stage.
            if (current)
            {
                ProfileDto profile = Game.Backend.Profile;
                string gender = profile?.Hero?.Gender ?? Game.Save.Settings.HeroGender ?? "female";
                Sprite portrait = ArtLibrary.Character(gender == "male" ? "hero" : "heroine");
                if (portrait != null)
                {
                    RectTransform marker = UIFactory.Anchor(UIFactory.Rect("Hero", node), 0.1f, 0.95f, 0.9f, 1.75f);
                    Image ring = UIFactory.Icon(marker, ProceduralSprites.Circle(), Theme.Gold, 0);
                    UIFactory.Stretch(ring.rectTransform);
                    Image face = UIFactory.Icon(marker, portrait, Color.white, 0);
                    UIFactory.Stretch(face.rectTransform, 8, 8, 8, 8);
                    marker.gameObject.AddComponent<Breathe>().Amount = 0.06f;
                }
            }

            if (_friendsByStage.TryGetValue(stageId, out List<string> friends) && friends.Count > 0)
            {
                Image chip = UIFactory.Panel("Friends", node, Theme.Crystal);
                UIFactory.Anchor(chip.rectTransform, 0.72f, 0.7f, 1.35f, 1.0f);
                Text initials = UIFactory.Label(chip.transform, string.Join("", friends.Take(3).Select(f => f.Substring(0, 1))), Theme.SmallSize - 4, Theme.Background, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(initials.rectTransform);
            }
            return node;
        }
    }

    /// <summary>Stage details, objectives, power-up loadout and play (consumes a life server-side).</summary>
    public sealed class StagePreviewScreen : UIScreen
    {
        private int _stageId;
        private readonly HashSet<string> _selected = new HashSet<string>();

        protected override Kingdom BackdropKingdom => Game.Backend.Catalog.Get(_stageId).Kingdom;

        protected override void Build()
        {
            _stageId = Args is int id ? id : 1;
            StageData stage = Game.Backend.Catalog.Get(_stageId);
            RectTransform body = Frame("stage.title");
            RectTransform list = UIFactory.ScrollList(body, 22, 36);
            UIFactory.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 240);

            // Header: ribbon with the stage number and kingdom, stars already earned below.
            RectTransform header = UIFactory.Rect("Header", list);
            UIFactory.Height(header, 190);
            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", header), 0.02f, 0.25f, 0.98f, 1f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, Loc.T("stage.heading", stage.Id, Loc.T("kingdom." + stage.Kingdom)), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.18f, 0.34f, 0.82f, 0.92f);
            Widgets.TitleOutline(title);
            string stars = Game.Backend.Profile?.Story?.StarsByStage ?? string.Empty;
            int earned = stage.Id - 1 < stars.Length ? stars[stage.Id - 1] - '0' : 0;
            StarRow(header, earned, 0.32f, 0f, 0.68f, 0.3f);

            if (stage.IsBoss)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("boss." + stage.BossKind) + " - " + Loc.T(stage.BossId + ".name"), Theme.BodySize, Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold), 80);
                Sprite bossArt = ArtLibrary.Boss(stage);
                if (bossArt != null)
                {
                    Image boss = UIFactory.Icon(list, bossArt, Color.white, 360);
                    UIFactory.Height(boss, 360);
                    boss.gameObject.AddComponent<Breathe>().Amount = 0.02f;
                }
            }

            // Objectives as big illustrated chips.
            Image goals = UIFactory.Panel("Goals", list, Theme.Panel);
            UIFactory.Height(goals, 300);
            UiKit.FramePanel(goals);
            Text goalsTitle = UIFactory.Label(goals.transform, Loc.T("stage.objectives"), Theme.BodySize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(goalsTitle.rectTransform, 0.05f, 0.72f, 0.95f, 0.92f);
            Widgets.TitleOutline(goalsTitle);
            RectTransform chips = UIFactory.Anchor(UIFactory.Rect("Chips", goals.transform), 0.06f, 0.26f, 0.94f, 0.72f);
            HorizontalLayoutGroup chipRow = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            chipRow.spacing = 20;
            chipRow.childAlignment = TextAnchor.MiddleCenter;
            chipRow.childControlWidth = chipRow.childControlHeight = true;
            chipRow.childForceExpandWidth = chipRow.childForceExpandHeight = true;
            foreach (StageObjective objective in stage.Objectives)
            {
                GoalChip(chips, stage, objective);
            }
            string limits = Loc.T("stage.limits", stage.MoveLimit, stage.TimeLimitMs / 1000) + "   ·   " + Loc.T("stage.difficulty", Mathf.RoundToInt(stage.DifficultyPercent));
            Text limitText = UIFactory.Label(goals.transform, limits, Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(limitText.rectTransform, 0.06f, 0.07f, 0.94f, 0.26f);

            // Star thresholds.
            Image thresholds = UIFactory.Panel("Stars", list, Theme.Panel);
            UIFactory.Height(thresholds, 150);
            UiKit.CardFrame(thresholds);
            long[] scores = { stage.TargetScore, stage.TwoStarScore, stage.ThreeStarScore };
            for (int i = 0; i < 3; i++)
            {
                float x = i / 3f;
                StarRow(thresholds.transform, i + 1, x + 0.03f, 0.52f, x + 0.3f, 0.92f, i + 1);
                Text score = UIFactory.Label(thresholds.transform, Loc.Number(scores[i]), Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(score.rectTransform, x + 0.02f, 0.08f, x + 0.31f, 0.5f);
            }
            if (stage.StoneCount > 0 || stage.IceCells > 0)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("stage.obstacles", stage.StoneCount, stage.IceCells), Theme.SmallSize, Theme.TextMuted), 60);
            }

            if (Game.Backend.IsOnline)
            {
                Widgets.SectionTitle(list, Loc.T("stage.boostsTitle"));
                LoadoutPicker.Build(list, _selected, pvp: false, rebuild: Rebuild);
                RewardRow(list, stage.RewardCoins, stage.RewardOrbes);
            }
            else
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("result.practice"), Theme.SmallSize, Theme.Warning), 90);
            }

            Button play = UIFactory.Button(body, Loc.T("stage.play"), () => _ = PlayAsync(), Theme.Success, Theme.HeaderSize);
            UIFactory.Anchor(play.GetComponent<RectTransform>(), 0.15f, 0.02f, 0.85f, 0.115f);
            play.gameObject.AddComponent<Breathe>().Amount = 0.02f;
        }

        /// <summary>Row of star icons, the first <paramref name="filled"/> lit.</summary>
        private static void StarRow(Transform parent, int filled, float minX, float minY, float maxX, float maxY, int total = 3)
        {
            RectTransform row = UIFactory.Anchor(UIFactory.Rect("StarRow", parent), minX, minY, maxX, maxY);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            Sprite star = ArtLibrary.Icon("star");
            for (int i = 0; i < total; i++)
            {
                if (star != null)
                {
                    UIFactory.Icon(row, star, i < filled ? Color.white : new Color(0.25f, 0.22f, 0.35f, 1f), 0);
                }
                else
                {
                    UIFactory.Label(row, "★", Theme.HeaderSize, i < filled ? Theme.Gold : Theme.TextMuted);
                }
            }
        }

        private void GoalChip(Transform parent, StageData stage, StageObjective objective)
        {
            RectTransform chip = UIFactory.Rect("Chip", parent);
            Sprite art;
            switch (objective.Type)
            {
                case ObjectiveType.CollectColor: art = ArtLibrary.Gem(objective.Color); break;
                case ObjectiveType.ClearIce: art = ArtLibrary.Ice(); break;
                case ObjectiveType.BreakStones: art = ArtLibrary.Stone(); break;
                case ObjectiveType.DefeatBoss: art = ArtLibrary.Boss(stage); break;
                default: art = UiKit.Art("item_stars") ?? ArtLibrary.Icon("star"); break;
            }
            if (art != null)
            {
                Image icon = UIFactory.Icon(chip, art, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0f, 0.05f, 0.4f, 0.95f);
            }
            string label = objective.Type == ObjectiveType.CollectColor
                ? Loc.T("objective.CollectColor", Loc.T("color." + objective.Color))
                : Loc.T("objective." + objective.Type);
            Text name = UIFactory.Label(chip, label, Theme.SmallSize - 2, Theme.TextMuted, TextAnchor.LowerLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.42f, 0.52f, 1f, 0.95f);
            Text target = UIFactory.Label(chip, objective.Target > 0 ? Loc.Number(objective.Target) : Loc.T("stage.all"), Theme.HeaderSize, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Anchor(target.rectTransform, 0.42f, 0.02f, 1f, 0.55f);
            Widgets.TitleOutline(target);
        }

        /// <summary>Win rewards with the coin and orb illustrations.</summary>
        public static void RewardRow(Transform list, long coins, long orbes)
        {
            Localization loc = GameRoot.Instance.Loc;
            Image panel = UIFactory.Panel("Rewards", list, Theme.Panel);
            UIFactory.Height(panel, 170);
            UiKit.CardFrame(panel);
            Text title = UIFactory.Label(panel.transform, loc.T("stage.rewardsTitle"), Theme.SmallSize + 2, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.05f, 0.1f, 0.34f, 0.9f);
            Widgets.TitleOutline(title);
            RewardItem(panel.transform, "item_coins", "+" + loc.Number(coins), Theme.Gold, 0.35f);
            if (orbes > 0)
            {
                RewardItem(panel.transform, "item_orbs", "+" + loc.Number(orbes), Theme.Orbe, 0.67f);
            }
        }

        private static void RewardItem(Transform parent, string icon, string text, Color color, float x)
        {
            Sprite art = UiKit.Art(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(parent, art, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, x, 0.12f, x + 0.12f, 0.88f);
            }
            Text label = UIFactory.Label(parent, text, Theme.HeaderSize - 4, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, x + 0.13f, 0.1f, x + 0.32f, 0.9f);
            Widgets.TitleOutline(label);
        }

        private async Task PlayAsync()
        {
            if (!Game.Backend.IsOnline)
            {
                UI.Show<GameplayScreen>(MatchLaunch.OfflineStory(_stageId), addToHistory: false);
                return;
            }

            ProfileDto profile = Game.Backend.Profile;
            if (profile.Lives.Lives <= 0)
            {
                bool buy = await UI.Confirm(Loc.T("lives.emptyTitle"), Loc.T("lives.emptyBody", profile.Lives.NextLifeCoins, profile.Lives.NextLifeOrbes), Loc.T("lives.buy"));
                if (!buy)
                {
                    return;
                }
                PurchaseResponse bought = await Api(api => api.BuyLivesAsync(1));
                if (bought == null)
                {
                    return;
                }
                Game.Backend.ApplyWallet(bought.Wallet);
                Game.Backend.ApplyLives(bought.Lives);
            }

            MatchStartResponse ticket = await Api(api => api.StartStageAsync(_stageId, new StartStageRequest { Loadout = _selected.ToList() }));
            if (ticket == null)
            {
                return;
            }
            Game.Backend.ApplyLives(ticket.Lives);
            if (ticket.BalanceHash != Game.Backend.Client.BalanceHash)
            {
                await Game.Backend.Client.RefreshConfigAsync();
            }
            UI.Show<GameplayScreen>(MatchLaunch.Online(ticket), addToHistory: false);
        }
    }

    /// <summary>Ranked PvP lobby: loadout, matchmaking with widening range, versus screen. Offline: practice against a bot ghost.</summary>
    public sealed class PvpScreen : UIScreen
    {
        private readonly HashSet<string> _selected = new HashSet<string>();
        private Text _status;
        private Button _find;
        private bool _searching;

        public override System.Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("pvp.title");
            RectTransform list = UIFactory.ScrollList(body, 20, 40);
            UIFactory.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 380);

            ProfileDto profile = Game.Backend.Profile;
            if (profile != null)
            {
                Text league = UIFactory.Label(list, Loc.T("league." + profile.Pvp.League), 90, Theme.League(profile.Pvp.League), TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Height(league, 140);
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.record", profile.Pvp.Trophies, profile.Pvp.Wins, profile.Pvp.Losses, profile.Pvp.WinStreak), Theme.BodySize), 80);
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.rules"), Theme.SmallSize, Theme.TextMuted), 150);

                Widgets.SectionTitle(list, Loc.T("stage.boostsTitle"));
                LoadoutPicker.Build(list, _selected, pvp: true, rebuild: Rebuild);
            }
            else
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.practiceBody"), Theme.BodySize, Theme.Warning), 200);
            }

            _status = UIFactory.Label(body, string.Empty, Theme.BodySize, Theme.Crystal);
            UIFactory.Anchor(_status.rectTransform, 0.05f, 0.13f, 0.95f, 0.19f);
            _find = UIFactory.Button(body, Loc.T(profile != null ? "pvp.find" : "pvp.practice"), () => _ = FindAsync());
            UIFactory.Anchor(_find.GetComponent<RectTransform>(), 0.15f, 0.02f, 0.85f, 0.11f);
            if (profile != null)
            {
                Button code = UIFactory.Button(body, Loc.T("challenge.haveCode"), () => _ = ChallengeFlow.PromptCodeAsync(UI, Game), Theme.PanelLight, Theme.SmallSize, Theme.Text);
                UIFactory.Anchor(code.GetComponent<RectTransform>(), 0.25f, 0.195f, 0.75f, 0.245f);
            }
        }

        public override bool HandleBack()
        {
            if (!_searching)
            {
                return false;
            }
            _ = CancelAsync();
            return true;
        }

        private async Task FindAsync()
        {
            if (_searching)
            {
                await CancelAsync();
                return;
            }

            if (!Game.Backend.IsOnline)
            {
                await PlayBotAsync(League.Bronze);
                return;
            }

            // First minutes: the very first duel is a friendly one against a gentle bot, so it ends in a win.
            ProfileDto me = Game.Backend.Profile;
            if (me != null && me.Pvp.Wins + me.Pvp.Losses == 0 && !Game.Save.Settings.FirstDuelDone)
            {
                Game.Save.Settings.FirstDuelDone = true;
                Game.Save.SaveSettings();
                await UI.Alert(Loc.T("pvp.firstDuelTitle"), Loc.T("pvp.firstDuelBody"));
                await PlayBotAsync(League.Bronze, easy: true);
                return;
            }

            MatchmakingStatusResponse status = await Api(api => api.RequestMatchAsync(new MatchmakingRequest { Loadout = _selected.ToList() }));
            if (status == null)
            {
                return;
            }

            _searching = true;
            bool offered = false;
            _find.GetComponentInChildren<Text>().text = Loc.T("common.cancel");
            while (_searching && this != null)
            {
                _status.text = Loc.T("pvp.searching", status.WaitedMs / 1000, status.Range);
                if (status.Status == "Matched" && status.Match != null)
                {
                    _searching = false;
                    await ShowVersusAsync(status.Match);
                    UI.Show<GameplayScreen>(MatchLaunch.Online(status.Match), addToHistory: false);
                    return;
                }
                if (status.Status == "TimedOut" || status.Status == "Cancelled" || status.Status == "None")
                {
                    _searching = false;
                    _status.text = Loc.T("pvp.noOpponent");
                    _find.GetComponentInChildren<Text>().text = Loc.T("pvp.find");
                    await OfferBotAsync();
                    return;
                }
                if (status.WaitedMs >= BotOfferAfterMs && !offered)
                {
                    offered = true;
                    _ = OfferBotWhileSearchingAsync();
                }

                await Task.Delay(1000);
                try
                {
                    status = await Game.Backend.Client.Api.GetMatchmakingStatusAsync();
                }
                catch (CrushApiException ex)
                {
                    if (!ex.IsNetwork)
                    {
                        UI.ShowError(ex);
                        _searching = false;
                    }
                }
            }
        }

        /// <summary>Few players online (launch, night): after this wait a training bot is offered instead of an empty queue.</summary>
        private const int BotOfferAfterMs = 20000;

        private async Task OfferBotWhileSearchingAsync()
        {
            if (await UI.Confirm(Loc.T("pvp.botTitle"), Loc.T("pvp.botBody")) && _searching && this != null)
            {
                await CancelAsync();
                await PlayBotAsync(CurrentLeague());
            }
        }

        private async Task OfferBotAsync()
        {
            if (await UI.Confirm(Loc.T("pvp.botTitle"), Loc.T("pvp.botBody")) && this != null)
            {
                await PlayBotAsync(CurrentLeague());
            }
        }

        private League CurrentLeague()
        {
            ProfileDto profile = Game.Backend.Profile;
            return profile != null && System.Enum.TryParse(profile.Pvp.League, out League league) ? league : League.Bronze;
        }

        /// <summary>Training duel (no trophies) against a human-paced bot of the given league.</summary>
        private async Task PlayBotAsync(League league, bool easy = false)
        {
            using (UI.Loading())
            {
                ulong seed = (ulong)Random.Range(1, int.MaxValue) * 2654435761UL;
                MatchLaunch launch = await Task.Run(() => MatchLaunch.OfflinePvp(Game.Backend.Balance, seed, league, easy));
                UI.Show<GameplayScreen>(launch, addToHistory: false);
            }
        }

        private async Task CancelAsync()
        {
            _searching = false;
            if (_status != null)
            {
                _status.text = string.Empty;
                _find.GetComponentInChildren<Text>().text = Loc.T("pvp.find");
            }
            await Api(api => api.CancelMatchmakingAsync(), loading: false);
        }

        private async Task ShowVersusAsync(MatchStartResponse match)
        {
            RectTransform box = UI.Popup(0.3f, 0.7f);
            ProfileDto me = Game.Backend.Profile;
            GhostDto ghost = match.Ghost;

            Text versus = UIFactory.Label(box, "VS", 140, Theme.RedSurge, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(versus.rectTransform, 0.3f, 0.35f, 0.7f, 0.65f);
            Text left = UIFactory.Label(box, me.DisplayName + "\n" + Loc.T("league." + me.Pvp.League) + " " + me.Pvp.Trophies, Theme.BodySize, Theme.League(me.Pvp.League));
            UIFactory.Anchor(left.rectTransform, 0.02f, 0.66f, 0.98f, 0.95f);

            string opponent = ghost != null
                ? ghost.OpponentName + "\n" + Loc.T("league." + ghost.OpponentLeague) + " " + ghost.OpponentTrophies + "\n" + string.Join(", ", ghost.OpponentLoadout.Select(l => Loc.T("powerup." + l.Type)))
                : Loc.T("pvp.liveOpponent");
            Text right = UIFactory.Label(box, opponent, Theme.BodySize, Theme.Crystal);
            UIFactory.Anchor(right.rectTransform, 0.02f, 0.05f, 0.98f, 0.34f);

            Game.Audio.PlaySFX(SoundIds.RedSurge);
            await Task.Delay(2500);
            if (box != null)
            {
                Destroy(box.parent.gameObject);
            }
        }
    }
}
