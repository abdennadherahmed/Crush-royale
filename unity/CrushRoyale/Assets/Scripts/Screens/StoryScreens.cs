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
        private readonly Dictionary<int, Vector2> _nodePositions = new Dictionary<int, Vector2>();
        private RectTransform _heroMarker;

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
            MapAnimator.Create(pins);

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
            int max = story.StagesPerChapter * 3;

            // Star count on its own bar (left), the three star chests as real buttons (right): nothing overlaps the count.
            UIFactory.ProgressBar(bar.transform, earned / (float)Mathf.Max(1, max), Theme.Gold, out RectTransform progress);
            UIFactory.Anchor(progress, 0.16f, 0.1f, 0.52f, 0.42f);
            Text starText = UIFactory.Label(progress, "★ " + earned + " / " + max, Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(starText.rectTransform);
            Widgets.TitleOutline(starText);
            for (int tier = 0; tier < ChapterChests.Tiers; tier++)
            {
                float x0 = 0.545f + tier * 0.1f;
                RectTransform slot = UIFactory.Anchor(UIFactory.Rect("BarChest" + tier, bar.transform), x0, 0.04f, x0 + 0.095f, 0.5f);
                BarChest(slot, tier, earned, story);
            }
        }

        /// <summary>Compact star chest in the chapter bar: tap to open once the stars are there.</summary>
        private void BarChest(RectTransform slot, int tier, int earned, StoryBalance story)
        {
            int need = ChapterChests.StarsNeeded(story, tier);
            bool claimed = IsChestClaimed(_chapter, tier);
            bool ready = !claimed && earned >= need;
            int chapter = _chapter;

            Image hit = slot.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            Button button = slot.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            slot.gameObject.AddComponent<ButtonFeedback>();
            if (ready)
            {
                Image glow = UIFactory.Icon(slot, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.35f, 0.9f), 0);
                UIFactory.Stretch(glow.rectTransform, -24, -24, -24, -24);
                glow.raycastTarget = false;
                glow.gameObject.AddComponent<Pulse>();
            }
            Image art = UIFactory.Icon(slot, ChestArtFor(tier, open: claimed), ready || claimed ? Color.white : new Color(0.5f, 0.5f, 0.6f, 1f), 0);
            art.preserveAspect = true;
            art.raycastTarget = false;
            UIFactory.Anchor(art.rectTransform, 0f, 0.28f, 1f, 1.25f);
            if (ready)
            {
                Breathe breathe = art.gameObject.AddComponent<Breathe>();
                breathe.Amount = 0.07f;
                breathe.Speed = 5f;
            }
            Text label = UIFactory.Label(slot, claimed ? "✔" : ready ? "!" : need + "★", Theme.SmallSize - 8, claimed ? Theme.Success : ready ? Theme.Gold : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.raycastTarget = false;
            UIFactory.Anchor(label.rectTransform, -0.2f, -0.05f, 1.2f, 0.34f);
            Widgets.TitleOutline(label);
            button.onClick.AddListener(() => TapChest(chapter, tier, earned, need, claimed));
        }

        private void TapChest(int chapter, int tier, int earned, int need, bool claimed)
        {
            if (claimed)
            {
                UI.Toast(Loc.T("map.chestClaimed"));
            }
            else if (earned < need)
            {
                UI.Toast(Loc.T("map.chestNeed", need - earned, need));
            }
            else if (!Game.Backend.IsOnline)
            {
                UI.Toast(Loc.T("error.offline"));
            }
            else
            {
                _ = ClaimChestAsync(chapter, tier);
            }
        }

        private bool IsChestClaimed(int chapter, int tier) =>
            Game.Backend.Profile?.Story?.ClaimedChapterChests?.Contains(ChapterChests.Key(chapter, tier)) ?? false;

        private static Sprite ChestArtFor(int tier, bool open)
        {
            string[] types = { "silver", "gold", "crystal" };
            return (open ? UiKit.Art("chest_open_" + types[tier]) : null) ?? ChestBar.ChestArt(types[tier]);
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
            _nodePositions.Clear();
            _heroMarker = null;
            for (int i = 0; i < count; i++)
            {
                int stageId = firstStage + i;
                Vector2 position = new Vector2(Mathf.Sin(i * 0.9f) * 290f, NodeSpacing * (i + 0.8f));
                if (i > 0)
                {
                    Dots(content, previous, position, stageId <= highest);
                }
                _nodePositions[stageId] = position;
                RectTransform node = StageNode(content, stageId, position, highest, stars);
                if (stageId == highest)
                {
                    currentNode = node;
                }
                previous = position;
            }

            // Star chests beside stages 10, 15 and 20 of the chapter, on the other side of the path.
            int earned = 0;
            for (int id = firstStage; id < firstStage + story.StagesPerChapter && id - 1 < stars.Length; id++)
            {
                earned += Mathf.Max(0, stars[id - 1] - '0');
            }
            int[] besides = { story.StagesPerChapter / 2, story.StagesPerChapter * 3 / 4, story.StagesPerChapter };
            for (int tier = 0; tier < ChapterChests.Tiers; tier++)
            {
                int stageId = firstStage + besides[tier] - 1;
                if (_nodePositions.TryGetValue(stageId, out Vector2 at))
                {
                    // Opposite side of the path, kept well inside the narrowest phone width.
                    ChestNode(content, tier, new Vector2(at.x > 0 ? -250f : 250f, at.y + 20f), earned, story);
                }
            }

            // The hero walks from the stage of the last visit to the new current stage.
            if (currentNode != null && _nodePositions.TryGetValue(highest, out Vector2 heroAt))
            {
                int from = Game.Save.Settings.LastMapStage;
                _heroMarker = HeroMarker(content, heroAt);
                if (from > 0 && from < highest && _nodePositions.ContainsKey(from))
                {
                    _heroMarker.anchoredPosition = _nodePositions[from] + new Vector2(0, 120f);
                    StartCoroutine(WalkHero(from, highest));
                }
                if (Game.Save.Settings.LastMapStage != highest)
                {
                    Game.Save.Settings.LastMapStage = highest;
                    Game.Save.SaveSettings();
                }
            }

            // Start scrolled so the current stage (or the chapter start) is visible.
            float target = currentNode != null ? currentNode.anchoredPosition.y : 0f;
            StartCoroutine(ScrollTo(scroll, target, height));
        }

        private RectTransform HeroMarker(RectTransform content, Vector2 at)
        {
            ProfileDto profile = Game.Backend.Profile;
            string gender = profile?.Hero?.Gender ?? Game.Save.Settings.HeroGender ?? "female";
            RectTransform marker = UIFactory.Rect("Hero", content);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0f);
            marker.sizeDelta = new Vector2(130, 130);
            marker.anchoredPosition = at + new Vector2(0, 120f);
            Image shadow = UIFactory.Icon(marker, ProceduralSprites.Glow(64), new Color(0, 0, 0, 0.5f), 0);
            UIFactory.Anchor(shadow.rectTransform, 0.1f, -0.25f, 0.9f, 0.05f);
            Image ring = UIFactory.Icon(marker, ProceduralSprites.Circle(), Theme.Gold, 0);
            UIFactory.Stretch(ring.rectTransform);
            Sprite portrait = ArtLibrary.Character(gender == "male" ? "hero" : "heroine");
            if (portrait != null)
            {
                Image face = UIFactory.Icon(marker, portrait, Color.white, 0);
                UIFactory.Stretch(face.rectTransform, 8, 8, 8, 8);
            }
            string pet = profile?.Pets?.Equipped;
            Sprite petArt = string.IsNullOrEmpty(pet) ? null : PetsScreen.PetArt(pet);
            if (petArt != null)
            {
                Image petImage = UIFactory.Icon(marker, petArt, Color.white, 0);
                UIFactory.Anchor(petImage.rectTransform, 0.72f, -0.2f, 1.3f, 0.45f);
                Breathe hop = petImage.gameObject.AddComponent<Breathe>();
                hop.Amount = 0.08f;
                hop.Speed = 4f;
            }
            marker.gameObject.AddComponent<Breathe>().Amount = 0.06f;
            return marker;
        }

        /// <summary>Hops from node to node along the path, then celebrates on the new stage.</summary>
        private System.Collections.IEnumerator WalkHero(int from, int to)
        {
            yield return new WaitForSeconds(0.5f);
            for (int stage = from + 1; stage <= to && _heroMarker != null; stage++)
            {
                if (!_nodePositions.TryGetValue(stage, out Vector2 next))
                {
                    continue;
                }
                Vector2 start = _heroMarker.anchoredPosition;
                Vector2 end = next + new Vector2(0, 120f);
                Game.Audio.PlaySFX(SoundIds.PetHop, UnityEngine.Random.Range(0.95f, 1.1f));
                for (float t = 0; t < 0.45f && _heroMarker != null; t += Time.deltaTime)
                {
                    float k = t / 0.45f;
                    _heroMarker.anchoredPosition = Vector2.Lerp(start, end, k) + new Vector2(0, Mathf.Sin(k * Mathf.PI) * 70f);
                    yield return null;
                }
                if (_heroMarker != null)
                {
                    _heroMarker.anchoredPosition = end;
                }
            }
            if (_heroMarker != null)
            {
                Game.Audio.PlaySFX(SoundIds.Sparkle);
                _heroMarker.gameObject.AddComponent<PopIn>();
            }
        }

        private void ChestNode(RectTransform content, int tier, Vector2 at, int earned, StoryBalance story)
        {
            int need = ChapterChests.StarsNeeded(story, tier);
            bool claimed = IsChestClaimed(_chapter, tier);
            bool ready = !claimed && earned >= need;
            int chapter = _chapter;

            RectTransform node = UIFactory.Rect("StarChest" + tier, content);
            node.anchorMin = node.anchorMax = new Vector2(0.5f, 0f);
            node.sizeDelta = new Vector2(170, 170);
            node.anchoredPosition = at;
            Image hit = node.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);
            Button button = node.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            node.gameObject.AddComponent<ButtonFeedback>();

            if (ready)
            {
                Image glow = UIFactory.Icon(node, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.35f, 0.95f), 0);
                UIFactory.Stretch(glow.rectTransform, -50, -50, -50, -50);
                glow.gameObject.AddComponent<Pulse>();
            }
            Image art = UIFactory.Icon(node, ChestArtFor(tier, open: claimed), ready || claimed ? Color.white : new Color(0.5f, 0.5f, 0.6f, 1f), 0);
            UIFactory.Stretch(art.rectTransform);
            if (ready)
            {
                Breathe breathe = art.gameObject.AddComponent<Breathe>();
                breathe.Amount = 0.06f;
                breathe.Speed = 5f;
            }

            Image plate = UIFactory.Panel("Need", node, claimed ? Theme.Success : ready ? Theme.GoldDark : Theme.Panel);
            UIFactory.Anchor(plate.rectTransform, 0.05f, -0.22f, 0.95f, 0.04f);
            string label = claimed ? "✔" : ready ? Loc.T("map.chestOpen") : Mathf.Min(earned, need) + "/" + need + " ★";
            Text text = UIFactory.Label(plate.transform, label, Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(text.rectTransform, 4, 4, 2, 2);
            Widgets.TitleOutline(text);

            button.onClick.AddListener(() => TapChest(chapter, tier, earned, need, claimed));
        }

        private async Task ClaimChestAsync(int chapter, int tier)
        {
            ChapterChestResponse response = await Api(api => api.ClaimChapterChestAsync(chapter, tier));
            if (response == null || this == null)
            {
                return;
            }
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null)
            {
                profile.Story = response.Story ?? profile.Story;
                profile.Pets = response.Pets ?? profile.Pets;
            }
            Game.Backend.ApplyWallet(response.Wallet);
            Game.Backend.ApplyInventory(response.Inventory);
            Game.Telemetry.Track("chapter_chest", ("chapter", chapter), ("tier", tier));

            List<RevealItem> items = RevealOverlay.FromReward(response.Reward);
            if (response.PetFragments > 0 && !string.IsNullOrEmpty(response.FragmentsPet))
            {
                items.Add(new RevealItem { Art = PetsScreen.PetArt(response.FragmentsPet), Caption = Loc.T("pets.fragmentsGain", response.PetFragments), Rare = true });
            }
            await RevealOverlay.PlayChestAsync(items, ChestArtFor(tier, open: false), ChestArtFor(tier, open: true), Loc.T("map.chestTitle", ChapterChests.StarsNeeded(Game.Backend.Balance.Story, tier)));
            if (this != null)
            {
                Rebuild();
            }
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
            // Sawtooth labels (red Hard, purple Super hard) and a clock on timed stages.
            if (!stage.IsBoss && stage.Tier != StageTier.Normal)
            {
                bool super = stage.Tier == StageTier.SuperHard;
                Color tierColor = super ? Theme.Hex("B05CFF") : Theme.Hex("FF4B5C");
                Image ring = UIFactory.Icon(node, ProceduralSprites.Glow(128), new Color(tierColor.r, tierColor.g, tierColor.b, 0.85f), 0);
                UIFactory.Stretch(ring.rectTransform, -34, -34, -34, -34);
                ring.transform.SetAsFirstSibling();
                ring.raycastTarget = false;
                if (unlocked)
                {
                    ring.gameObject.AddComponent<Pulse>();
                }
                Text tierTag = UIFactory.Label(node, Loc.T(super ? "map.superHard" : "map.hard"), Theme.SmallSize - 6, tierColor, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(tierTag.rectTransform, -0.4f, 0.9f, 1.4f, 1.18f);
                Widgets.TitleOutline(tierTag);
            }
            if (stage.Timed)
            {
                Sprite hourglass = UiKit.Art("item_hourglass");
                if (hourglass != null)
                {
                    Image clock = UIFactory.Icon(node, hourglass, unlocked ? Color.white : new Color(0.5f, 0.5f, 0.6f, 1f), 0);
                    clock.preserveAspect = true;
                    clock.raycastTarget = false;
                    UIFactory.Anchor(clock.rectTransform, 0.66f, 0.62f, 1.02f, 1.02f);
                }
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

            if (_friendsByStage.TryGetValue(stageId, out List<string> friends) && friends.Count > 0)
            {
                // Up to 3 friend avatars fanned out on the left of the stage, with their names.
                string[] portraits = { "kael", "mira", "thorin", "zara", "elian", "mark", "soren", "lyra" };
                for (int f = 0; f < friends.Count && f < 3; f++)
                {
                    string friend = friends[f] ?? string.Empty;
                    RectTransform avatar = UIFactory.Rect("Friend", node);
                    avatar.anchorMin = avatar.anchorMax = new Vector2(-0.25f - f * 0.35f, 0.75f - f * 0.1f);
                    avatar.sizeDelta = new Vector2(80, 80);
                    UiKit.RoundBadge(avatar, crystal: true);
                    Sprite face = ArtLibrary.Character(portraits[Mathf.Abs(friend.GetHashCode()) % portraits.Length]);
                    if (face != null)
                    {
                        Image faceImage = UIFactory.Icon(avatar, face, Color.white, 0);
                        UIFactory.Stretch(faceImage.rectTransform, 9, 9, 9, 9);
                    }
                    Text friendName = UIFactory.Label(avatar, friend.Length > 10 ? friend.Substring(0, 10) : friend, Theme.SmallSize - 10, Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Anchor(friendName.rectTransform, -0.6f, -0.45f, 1.6f, 0f);
                    Widgets.TitleOutline(friendName);
                }
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

            // Difficulty tier and win-streak bonus.
            if (stage.Tier != StageTier.Normal)
            {
                bool super = stage.Tier == StageTier.SuperHard;
                Image tier = UIFactory.Panel("Tier", list, super ? Theme.Hex("7A1FA2") : Theme.Hex("B3263E"));
                UIFactory.Height(tier, 120);
                Text tierText = UIFactory.Label(tier.transform, Loc.T(super ? "stage.tierSuperHard" : "stage.tierHard") + "\n" + Loc.T("stage.tierReward"), Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(tierText.rectTransform, 12, 12, 4, 4);
                Widgets.TitleOutline(tierText);
                tier.gameObject.AddComponent<Breathe>().Amount = 0.01f;
            }
            int streak = Game.Backend.Profile?.Story?.WinStreak ?? 0;
            bool alreadyWon = earned > 0;
            if (Game.Backend.IsOnline && streak > 0 && !alreadyWon)
            {
                int boosters = 0;
                foreach (int step in Game.Backend.Balance.Story.StreakBoosterWins)
                {
                    boosters += streak >= step ? 1 : 0;
                }
                Image flame = UIFactory.Panel("Streak", list, Theme.GoldDark);
                UIFactory.Height(flame, 100);
                string streakText = boosters > 0 ? Loc.T("streak.boosters", streak, boosters) : Loc.T("streak.now", streak);
                Text flameText = UIFactory.Label(flame.transform, streakText, Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(flameText.rectTransform, 12, 12, 4, 4);
                Widgets.TitleOutline(flameText);
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
            string limits = (stage.Timed ? Loc.T("stage.limitsTimed", stage.TimeLimitMs / 1000) : Loc.T("stage.limitsMoves", stage.MoveLimit))
                + "   ·   " + Loc.T("stage.difficulty", Mathf.RoundToInt(stage.DifficultyPercent));
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
            var mechanics = new List<string>();
            if (stage.TimeBombCount > 0)
            {
                mechanics.Add(Loc.T("mechanic.bomb.title"));
            }
            if (stage.BlightCount > 0)
            {
                mechanics.Add(Loc.T("mechanic.blight.title"));
            }
            if (stage.EggCount > 0)
            {
                mechanics.Add(Loc.T("mechanic.egg.title"));
            }
            if (mechanics.Count > 0)
            {
                UIFactory.Height(UIFactory.Label(list, string.Join("  ·  ", mechanics), Theme.SmallSize, Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold), 60);
            }
            if (stage.StoneCount > 0 || stage.IceCells > 0)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("stage.obstacles", stage.StoneCount, stage.IceCells), Theme.SmallSize, Theme.TextMuted), 60);
            }

            if (Game.Backend.IsOnline)
            {
                Widgets.SectionTitle(list, Loc.T("stage.boostsTitle"));
                LoadoutPicker.Build(list, _selected, pvp: false, rebuild: Rebuild, movesStage: stage.MoveLimit > 0);
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
                BuildNextLeague(list, profile);
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.rules"), Theme.SmallSize, Theme.TextMuted), 150);

                Widgets.SectionTitle(list, Loc.T("stage.boostsTitle"));
                LoadoutPicker.Build(list, _selected, pvp: true, rebuild: Rebuild);
            }
            else
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.practiceBody"), Theme.BodySize, Theme.Warning), 200);
            }

            BuildFooter(body, profile);
        }

        /// <summary>
        /// How far the next league is, and what reaching it pays the first time.
        ///
        /// A trophy count on its own is a number; the league above it with a distance and a prize is a goal, and it
        /// is the difference between closing the arena and playing one more match.
        /// </summary>
        private void BuildNextLeague(Transform list, ProfileDto profile)
        {
            TrophyBalance trophies = Game.Backend.Balance.Trophies;
            if (!System.Enum.TryParse(profile.Pvp.League, out League current) || current >= League.Master)
            {
                return;
            }
            League next = current + 1;
            int floor = CrushRoyale.Core.Pvp.LeagueTable.LowerBound(current, trophies);
            int target = CrushRoyale.Core.Pvp.LeagueTable.LowerBound(next, trophies);
            int span = System.Math.Max(1, target - floor);
            float done = Mathf.Clamp01((profile.Pvp.Trophies - floor) / (float)span);

            Image card = UIFactory.Panel("NextLeague", list, Theme.Panel);
            UIFactory.Height(card, 170);
            UiKit.CardFrame(card);

            Text title = UIFactory.Label(card.transform, Loc.T("pvp.nextLeague", Loc.T("league." + next)),
                Theme.SmallSize, Theme.League(next.ToString()), TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.05f, 0.58f, 0.95f, 0.92f);
            Widgets.TitleOutline(title);

            UIFactory.ProgressBar(card.transform, done, Theme.League(next.ToString()), out RectTransform bar);
            UIFactory.Anchor(bar, 0.05f, 0.28f, 0.95f, 0.52f);
            Text count = UIFactory.Label(bar, Loc.T("pvp.nextLeagueTrophies", System.Math.Max(0, target - profile.Pvp.Trophies)),
                Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(count.rectTransform);
            Widgets.TitleOutline(count);

            int[] table = Game.Backend.Balance.Trophies.FirstReachOrbesByLeague;
            int bonus = (int)next < table.Length ? table[(int)next] : 0;
            if (bonus > 0)
            {
                Text prize = UIFactory.Label(card.transform, Loc.T("pvp.nextLeagueBonus", Loc.Number(bonus)),
                    Theme.SmallSize - 6, Theme.Orbe, TextAnchor.MiddleCenter);
                UIFactory.Anchor(prize.rectTransform, 0.05f, 0.04f, 0.95f, 0.26f);
            }
        }

        private void BuildFooter(RectTransform body, ProfileDto profile)
        {
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

        /// <summary>One player of the versus popup: framed avatar, name, title plaque, league line.</summary>
        private void Side(RectTransform box, string name, string title, string frame, Sprite portrait, string line, Color lineColor, bool top)
        {
            float y0 = top ? 0.6f : 0.04f;
            float y1 = top ? 0.96f : 0.4f;
            RectTransform area = UIFactory.Anchor(UIFactory.Rect(top ? "Me" : "Opponent", box), 0.04f, y0, 0.96f, y1);
            RectTransform avatar = CosmeticLook.Avatar(area, portrait, frame, 190);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0.17f, 0.52f);
            avatar.gameObject.AddComponent<PopIn>().Delay = top ? 0f : 0.25f;
            Text nameText = UIFactory.Label(area, name, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(nameText.rectTransform, 0.36f, 0.66f, 1f, 0.98f);
            Widgets.TitleOutline(nameText);
            if (!string.IsNullOrEmpty(title))
            {
                RectTransform plate = CosmeticLook.TitlePlate(area, Loc, title, Theme.SmallSize - 2);
                UIFactory.Anchor(plate, 0.34f, 0.4f, 0.96f, 0.68f);
            }
            Text lineText = UIFactory.Label(area, line, Theme.SmallSize, lineColor, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Anchor(lineText.rectTransform, 0.36f, 0.02f, 1f, 0.4f);
            Widgets.TitleOutline(lineText);
        }

        private async Task ShowVersusAsync(MatchStartResponse match)
        {
            RectTransform box = UI.Popup(0.22f, 0.78f);
            ProfileDto me = Game.Backend.Profile;
            GhostDto ghost = match.Ghost;
            string myGender = me.Hero?.Gender ?? Game.Save.Settings.HeroGender ?? "female";

            Side(box, me.DisplayName, me.Inventory?.EquippedTitle, me.Inventory?.EquippedFrame, ArtLibrary.Character(myGender == "male" ? "hero" : "heroine"),
                Loc.T("league." + me.Pvp.League) + "  " + Loc.Number(me.Pvp.Trophies), Theme.League(me.Pvp.League), top: true);

            Text versus = UIFactory.Label(box, "VS", 150, Theme.RedSurge, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(versus.rectTransform, 0.3f, 0.42f, 0.7f, 0.58f);
            Widgets.TitleOutline(versus);
            versus.gameObject.AddComponent<Pulse>().Scale = true;

            if (ghost != null)
            {
                string[] portraits = { "kael", "mira", "thorin", "zara", "elian", "mark", "soren" };
                Sprite face = ArtLibrary.Character(portraits[Mathf.Abs((ghost.OpponentName ?? string.Empty).GetHashCode()) % portraits.Length]);
                string loadout = string.Join(", ", ghost.OpponentLoadout.Select(l => Loc.T("powerup." + l.Type)));
                Side(box, ghost.OpponentName, ghost.OpponentTitle, ghost.OpponentFrame, face,
                    Loc.T("league." + ghost.OpponentLeague) + "  " + Loc.Number(ghost.OpponentTrophies) + (loadout.Length > 0 ? "\n" + loadout : string.Empty),
                    Theme.League(ghost.OpponentLeague), top: false);
            }
            else
            {
                Text right = UIFactory.Label(box, Loc.T("pvp.liveOpponent"), Theme.BodySize, Theme.Crystal);
                UIFactory.Anchor(right.rectTransform, 0.05f, 0.05f, 0.95f, 0.4f);
            }

            Game.Audio.PlaySFX(SoundIds.RedSurge);
            await Task.Delay(2500);
            if (box != null)
            {
                Destroy(box.parent.gameObject);
            }
        }
    }
}
