using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>One thing revealed by a summon or a chest.</summary>
    public sealed class RevealItem
    {
        public Sprite Art;
        public string Caption;
        public bool Rare;
    }

    /// <summary>
    /// Full-screen suspense reveal: a container (egg, chest) appears, shakes harder and harder, cracks or opens with a
    /// flash, then the prize pops out. Several prizes play one by one (skippable) and end on a summary grid.
    /// </summary>
    public sealed class RevealOverlay : MonoBehaviour
    {
        private RectTransform _stage;
        private Text _caption;
        private Text _counter;
        private bool _tapped;
        private bool _skip;

        private static GameRoot Game => GameRoot.Instance;

        /// <summary>Summon style: every prize comes out of its own container (closed -> cracked -> flash -> prize).</summary>
        public static Task PlaySummonAsync(IList<RevealItem> items, Sprite closed, Sprite cracked, Sprite opened, string title)
        {
            RevealOverlay overlay = Create(title);
            return overlay.RunAsync(items, closed, cracked, opened, oneContainer: false);
        }

        /// <summary>Chest style: one container opens and all the prizes fly out of it.</summary>
        public static Task PlayChestAsync(IList<RevealItem> items, Sprite closed, Sprite opened, string title)
        {
            RevealOverlay overlay = Create(title);
            return overlay.RunAsync(items, closed, null, opened, oneContainer: true);
        }

        /// <summary>Reward bundle as reveal cards: coins, orbes, power-ups, lives, pass XP, cosmetics.</summary>
        public static List<RevealItem> FromReward(CrushRoyale.Contracts.RewardDto reward)
        {
            Localization loc = Game.Loc;
            var items = new List<RevealItem>();
            if (reward == null)
            {
                return items;
            }
            if (reward.Coins > 0)
            {
                items.Add(new RevealItem { Art = UiKit.Art("item_coins") ?? ArtLibrary.Icon("coin"), Caption = "+" + loc.Number(reward.Coins) });
            }
            if (reward.Orbes > 0)
            {
                items.Add(new RevealItem { Art = UiKit.Art("item_orbs") ?? ArtLibrary.Icon("orb"), Caption = "+" + loc.Number(reward.Orbes), Rare = true });
            }
            foreach (KeyValuePair<string, int> p in reward.PowerUps)
            {
                Sprite art = System.Enum.TryParse(p.Key, out CrushRoyale.Core.Config.PowerUpType type) ? ArtLibrary.PowerUp(type) : null;
                items.Add(new RevealItem { Art = art ?? UiKit.Art("item_bolt"), Caption = loc.T("powerup." + p.Key) + " x" + p.Value });
            }
            if (reward.Lives > 0)
            {
                items.Add(new RevealItem { Art = UiKit.Art("item_heart") ?? ArtLibrary.Icon("heart"), Caption = "+" + reward.Lives });
            }
            if (reward.BattlePassXp > 0)
            {
                items.Add(new RevealItem { Art = UiKit.Art("item_xp"), Caption = "+" + reward.BattlePassXp + " XP" });
            }
            foreach (string cosmetic in reward.Cosmetics)
            {
                items.Add(new RevealItem { Art = UiKit.Art("item_gift"), Caption = loc.T("cosmetic." + cosmetic), Rare = true });
            }
            return items;
        }

        private static RevealOverlay Create(string title)
        {
            Image shade = UIFactory.Panel("Reveal", Game.UI.DialogLayer, new Color(0.02f, 0.01f, 0.06f, 0.94f), rounded: false);
            UIFactory.Stretch(shade.rectTransform);
            RevealOverlay overlay = shade.gameObject.AddComponent<RevealOverlay>();
            Button tap = shade.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(() => overlay._tapped = true);

            Image rays = UIFactory.Icon(shade.transform, ProceduralSprites.Glow(128), new Color(0.55f, 0.3f, 0.9f, 0.35f), 0);
            UIFactory.Anchor(rays.rectTransform, -0.3f, 0.2f, 1.3f, 0.9f);
            rays.gameObject.AddComponent<Pulse>();

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", shade.transform), 0.08f, 0.85f, 0.92f, 0.95f);
            if (UiKit.Ribbon(ribbon) != null)
            {
                Text text = UIFactory.Label(ribbon, title, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(text.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
                Widgets.TitleOutline(text);
            }

            overlay._stage = UIFactory.Anchor(UIFactory.Rect("Stage", shade.transform), 0.1f, 0.3f, 0.9f, 0.8f);
            overlay._caption = UIFactory.Label(shade.transform, string.Empty, Theme.TitleSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(overlay._caption.rectTransform, 0.05f, 0.19f, 0.95f, 0.3f);
            Widgets.TitleOutline(overlay._caption);
            overlay._counter = UIFactory.Label(shade.transform, string.Empty, Theme.BodySize, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(overlay._counter.rectTransform, 0.3f, 0.78f, 0.7f, 0.84f);
            return overlay;
        }

        private async Task RunAsync(IList<RevealItem> items, Sprite closed, Sprite cracked, Sprite opened, bool oneContainer)
        {
            Button skip = null;
            if (items.Count > 1 && !oneContainer)
            {
                skip = UIFactory.Button(transform, Game.Loc.T("reveal.skip"), () => _skip = true, Theme.PanelLight, Theme.BodySize);
                UIFactory.Anchor(skip.GetComponent<RectTransform>(), 0.64f, 0.03f, 0.96f, 0.1f);
            }

            if (oneContainer)
            {
                await CoroutineTask.Run(this, OpenContainer(closed, null, opened, rare: false, keepOpened: true));
                await CoroutineTask.Run(this, FlyOut(items));
            }
            else
            {
                for (int i = 0; i < items.Count && !_skip && this != null; i++)
                {
                    _counter.text = items.Count > 1 ? (i + 1) + " / " + items.Count : string.Empty;
                    _caption.text = string.Empty;
                    await CoroutineTask.Run(this, OpenContainer(closed, cracked, opened, items[i].Rare, keepOpened: false));
                    if (_skip || this == null)
                    {
                        break;
                    }
                    await CoroutineTask.Run(this, ShowPrize(items[i], items.Count == 1 ? 60f : 1.4f));
                }
                if (this == null)
                {
                    return;
                }
                if (skip != null)
                {
                    Destroy(skip.gameObject);
                }
                if (items.Count > 1)
                {
                    _counter.text = string.Empty;
                    _caption.text = string.Empty;
                    UIFactory.Clear(_stage);
                    Summary(items);
                }
            }
            if (this == null)
            {
                return;
            }

            var done = new TaskCompletionSource<bool>();
            Button ok = UIFactory.Button(transform, Game.Loc.T("common.ok"), () => done.TrySetResult(true), Theme.Gold, Theme.HeaderSize);
            UIFactory.Anchor(ok.GetComponent<RectTransform>(), 0.25f, 0.04f, 0.75f, 0.13f);
            ok.gameObject.AddComponent<PopIn>();
            await done.Task;
            if (this != null)
            {
                Destroy(gameObject);
            }
        }

        private IEnumerator OpenContainer(Sprite closed, Sprite cracked, Sprite opened, bool rare, bool keepOpened)
        {
            UIFactory.Clear(_stage);
            Image glow = UIFactory.Icon(_stage, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.4f, 0f), 0);
            UIFactory.Anchor(glow.rectTransform, -0.2f, -0.2f, 1.2f, 1.2f);
            Image container = UIFactory.Icon(_stage, closed, Color.white, 0);
            UIFactory.Anchor(container.rectTransform, 0.2f, 0.1f, 0.8f, 0.9f);
            container.gameObject.AddComponent<PopIn>();
            yield return new WaitForSeconds(0.35f);

            // Suspense: the shake grows; rare prizes glow gold before breaking out.
            _tapped = false;
            float shakeTime = rare ? 1.5f : 0.9f;
            for (float t = 0; t < shakeTime && !_skip; t += Time.deltaTime)
            {
                float k = t / shakeTime;
                float angle = Mathf.Sin(t * (20f + 30f * k)) * (4f + 14f * k);
                container.rectTransform.localEulerAngles = new Vector3(0, 0, angle);
                container.rectTransform.localScale = Vector3.one * (1f + 0.06f * k);
                glow.color = new Color(1f, rare ? 0.8f : 0.6f, rare ? 0.3f : 1f, (rare ? 0.9f : 0.45f) * k);
                if (_tapped)
                {
                    shakeTime = Mathf.Min(shakeTime, t + 0.15f);
                }
                yield return null;
            }
            container.rectTransform.localEulerAngles = Vector3.zero;
            if (cracked != null && !_skip)
            {
                container.sprite = cracked;
                Game.Haptics.Light();
                yield return new WaitForSeconds(0.3f);
            }

            // Flash.
            Game.Audio.PlaySFX(rare ? SoundIds.WinFanfare : SoundIds.Explosion);
            Game.Haptics.Light();
            if (opened != null)
            {
                container.sprite = opened;
            }
            Sprite burstArt = UiKit.Art("burst") ?? ProceduralSprites.Glow(128);
            Image burst = UIFactory.Icon(_stage, burstArt, Color.white, 0);
            UIFactory.Anchor(burst.rectTransform, 0.1f, 0.1f, 0.9f, 0.9f);
            yield return CoroutineTask.Tween(0.35f, k =>
            {
                burst.rectTransform.localScale = Vector3.one * (0.3f + 2.2f * k);
                burst.color = new Color(1f, 1f, 1f, 1f - k);
            });
            Destroy(burst.gameObject);
            if (!keepOpened)
            {
                Destroy(container.gameObject);
            }
            glow.color = new Color(1f, 0.85f, 0.4f, rare ? 0.7f : 0.3f);
        }

        private IEnumerator ShowPrize(RevealItem item, float autoAdvance)
        {
            if (item.Rare)
            {
                Image rays = UIFactory.Icon(_stage, UiKit.Art("burst") ?? ProceduralSprites.Glow(128), new Color(1f, 0.95f, 0.7f, 0.8f), 0);
                UIFactory.Anchor(rays.rectTransform, -0.1f, -0.1f, 1.1f, 1.1f);
                rays.gameObject.AddComponent<Spin>();
            }
            Image prize = UIFactory.Icon(_stage, item.Art, Color.white, 0);
            UIFactory.Anchor(prize.rectTransform, item.Rare ? 0.1f : 0.25f, item.Rare ? 0.05f : 0.2f, item.Rare ? 0.9f : 0.75f, item.Rare ? 0.95f : 0.8f);
            prize.gameObject.AddComponent<PopIn>();
            prize.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            _caption.text = item.Caption;
            _caption.color = item.Rare ? Theme.Gold : Theme.Crystal;
            _caption.gameObject.GetComponent<PopIn>()?.Replay();
            if (_caption.GetComponent<PopIn>() == null)
            {
                _caption.gameObject.AddComponent<PopIn>();
            }
            if (!item.Rare)
            {
                Game.Audio.PlaySFX(SoundIds.Coins);
            }

            _tapped = false;
            for (float t = 0; t < autoAdvance && !_tapped && !_skip; t += Time.deltaTime)
            {
                yield return null;
            }
        }

        private IEnumerator FlyOut(IList<RevealItem> items)
        {
            RectTransform row = UIFactory.Anchor(UIFactory.Rect("Prizes", transform), 0.04f, 0.13f, 0.96f, 0.32f);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            foreach (RevealItem item in items)
            {
                Image card = UIFactory.Panel("Prize", row, Theme.Panel);
                UiKit.CardFrame(card);
                UIFactory.Width(card, Mathf.Min(240f, 960f / Mathf.Max(1, items.Count)));
                if (item.Art != null)
                {
                    Image icon = UIFactory.Icon(card.transform, item.Art, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, 0.12f, 0.36f, 0.88f, 0.92f);
                }
                Text label = UIFactory.Label(card.transform, item.Caption, Theme.SmallSize, item.Rare ? Theme.Gold : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(label.rectTransform, 0.05f, 0.04f, 0.95f, 0.36f);
                Widgets.TitleOutline(label);
                card.gameObject.AddComponent<PopIn>();
                Game.Audio.PlaySFX(SoundIds.Coins);
                yield return new WaitForSeconds(0.28f);
            }
        }

        private void Summary(IList<RevealItem> items)
        {
            GridLayoutGroup grid = _stage.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(160, 220);
            grid.spacing = new Vector2(14, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 5;
            grid.childAlignment = TextAnchor.MiddleCenter;
            for (int i = 0; i < items.Count; i++)
            {
                RevealItem item = items[i];
                Image card = UIFactory.Panel("Pull", _stage, Theme.Panel);
                UiKit.CardFrame(card);
                if (item.Rare)
                {
                    Image glow = UIFactory.Icon(card.transform, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.4f, 0.8f), 0);
                    UIFactory.Stretch(glow.rectTransform, -30, -30, -30, -30);
                    glow.transform.SetAsFirstSibling();
                    glow.gameObject.AddComponent<Pulse>();
                }
                if (item.Art != null)
                {
                    Image icon = UIFactory.Icon(card.transform, item.Art, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, 0.08f, 0.32f, 0.92f, 0.95f);
                }
                Text label = UIFactory.Label(card.transform, item.Caption, Theme.SmallSize - 8, item.Rare ? Theme.Gold : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(label.rectTransform, 0.03f, 0.02f, 0.97f, 0.32f);
                Widgets.TitleOutline(label);
                card.gameObject.AddComponent<PopIn>().Delay = i * 0.05f;
            }
        }
    }

    /// <summary>Slow rotation for light rays behind a rare prize.</summary>
    public sealed class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 40f;

        private void Update() => transform.Rotate(0f, 0f, -DegreesPerSecond * Time.unscaledDeltaTime);
    }
}
