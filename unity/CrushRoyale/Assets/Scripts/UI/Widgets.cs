using System;
using System.Collections.Generic;
using CrushRoyale.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>Reusable composite widgets.</summary>
    public static class Widgets
    {
        public static InputField Input(Transform parent, string placeholder, int characterLimit, InputField.ContentType contentType = InputField.ContentType.Standard)
        {
            Image background = UIFactory.Panel("Input", parent, Theme.BackgroundLight);
            InputField field = background.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;

            Text text = UIFactory.Label(background.transform, string.Empty, Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft);
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            UIFactory.Stretch(text.rectTransform, 28, 28, 6, 6);

            Text hint = UIFactory.Label(background.transform, placeholder, Theme.BodySize, Theme.TextMuted, TextAnchor.MiddleLeft, FontStyle.Italic);
            hint.resizeTextForBestFit = false;
            UIFactory.Stretch(hint.rectTransform, 28, 28, 6, 6);

            field.textComponent = text;
            field.placeholder = hint;
            field.characterLimit = characterLimit;
            field.contentType = contentType;
            return field;
        }

        /// <summary>
        /// Tab strip: one raised gold plate among sunken glass wells, instead of a row of identical coloured pills.
        /// Returns a setter to move the highlight; the labels follow (gold ink on the active tab, muted on the rest).
        /// </summary>
        public static Action<int> Tabs(Transform parent, IList<string> labels, Action<int> onSelect)
        {
            HorizontalLayoutGroup row = UIFactory.Row(parent, 110, 10);
            var faces = new List<Image>();
            var texts = new List<Text>();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                Button button = UIFactory.Button(row.transform, labels[i], () => onSelect(index), UIFactory.ButtonTier.Tertiary, Theme.SmallSize + 2);
                faces.Add(button.GetComponent<Image>());
                texts.Add(button.GetComponentInChildren<Text>());
            }
            return active =>
            {
                for (int i = 0; i < faces.Count; i++)
                {
                    if (faces[i] == null)
                    {
                        continue;
                    }
                    bool on = i == active;
                    UiKit.Tab(faces[i], on);
                    if (texts[i] != null)
                    {
                        texts[i].color = on ? Theme.Text : Theme.TextMuted;
                    }
                }
            };
        }

        /// <summary>"Label  [-] 70% [+]" stepper (0-1 by 10%).</summary>
        public static void Stepper(Transform parent, string label, float value, Action<float> onChange)
        {
            HorizontalLayoutGroup row = UIFactory.Row(parent, 110, 12);
            Text name = UIFactory.Label(row.transform, label, Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Width(name, 460);
            Text display = null;
            float current = value;
            UIFactory.Width(UIFactory.Button(row.transform, "-", () =>
            {
                current = Mathf.Clamp01(Mathf.Round((current - 0.1f) * 10f) / 10f);
                display.text = Mathf.RoundToInt(current * 100) + "%";
                onChange(current);
            }, Theme.PanelLight, Theme.HeaderSize, Theme.Text), 120);
            display = UIFactory.Label(row.transform, Mathf.RoundToInt(current * 100) + "%", Theme.BodySize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            // Wide enough for "100%" (a narrow wrapped label only showed its last line: "0%").
            display.horizontalOverflow = HorizontalWrapMode.Overflow;
            UIFactory.Width(display, 170);
            UIFactory.Width(UIFactory.Button(row.transform, "+", () =>
            {
                current = Mathf.Clamp01(Mathf.Round((current + 0.1f) * 10f) / 10f);
                display.text = Mathf.RoundToInt(current * 100) + "%";
                onChange(current);
            }, Theme.PanelLight, Theme.HeaderSize, Theme.Text), 120);
        }

        public static void Toggle(Transform parent, string label, bool value, Action<bool> onChange)
        {
            HorizontalLayoutGroup row = UIFactory.Row(parent, 110, 12);
            Text name = UIFactory.Label(row.transform, label, Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Width(name, 640);
            bool current = value;
            Button button = null;
            button = UIFactory.Button(row.transform, OnOff(current), () =>
            {
                current = !current;
                button.GetComponentInChildren<Text>().text = OnOff(current);
                UiKit.Recolor(button.GetComponent<Image>(), current ? Theme.Success : Theme.PanelLight);
                onChange(current);
            }, current ? Theme.Success : Theme.PanelLight, Theme.BodySize, Theme.Text);
        }

        /// <summary>Thick dark outline used on titles laid over illustrated ribbons and buttons.</summary>
        public static void TitleOutline(Text text)
        {
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.16f, 0.04f, 0.22f, 0.95f);
            outline.effectDistance = new Vector2(3, -3);
        }

        /// <summary>Row of reward chips (icon + amount) filling <paramref name="parent"/>.</summary>
        public static void RewardChips(RectTransform parent, RewardDto reward)
        {
            HorizontalLayoutGroup row = parent.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            foreach (RevealItem item in RevealOverlay.FromReward(reward))
            {
                RectTransform chip = UIFactory.Rect("Chip", parent);
                UIFactory.Width(chip.gameObject.AddComponent<LayoutElement>(), 190);
                if (item.Art != null)
                {
                    Image icon = UIFactory.Icon(chip, item.Art, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, 0f, 0.05f, 0.4f, 0.95f);
                }
                Text label = UIFactory.Label(chip, item.Caption, Theme.SmallSize - 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(label.rectTransform, 0.42f, 0f, 1f, 1f);
                TitleOutline(label);
            }
        }

        /// <summary>Illustrated empty state: glowing icon, friendly message and an optional call to action.</summary>
        public static RectTransform EmptyState(Transform parent, string icon, string message, string action = null, Action onAction = null)
        {
            Image card = UIFactory.Panel("Empty", parent, Theme.Panel);
            UIFactory.Height(card, action != null ? 520 : 400);
            UiKit.GlassCard(card);
            card.gameObject.AddComponent<PopIn>();
            float iconBottom = action != null ? 0.5f : 0.38f;
            Image glow = UIFactory.Icon(card.transform, ProceduralSprites.Glow(128), new Color(0.55f, 0.85f, 1f, 0.45f), 0);
            UIFactory.Anchor(glow.rectTransform, 0.3f, iconBottom - 0.04f, 0.7f, 0.98f);
            glow.gameObject.AddComponent<Pulse>();
            Sprite art = UiKit.Art(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(card.transform, art, Color.white, 0);
                image.preserveAspect = true;
                UIFactory.Anchor(image.rectTransform, 0.36f, iconBottom, 0.64f, 0.93f);
                image.gameObject.AddComponent<Breathe>().Amount = 0.04f;
            }
            Text text = UIFactory.Label(card.transform, message, Theme.BodySize - 2, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(text.rectTransform, 0.06f, action != null ? 0.26f : 0.06f, 0.94f, iconBottom);
            TitleOutline(text);
            if (action != null && onAction != null)
            {
                Button button = UIFactory.Button(card.transform, action, onAction, Theme.Gold, Theme.BodySize);
                UIFactory.Anchor(button.GetComponent<RectTransform>(), 0.2f, 0.05f, 0.8f, 0.22f);
                button.gameObject.AddComponent<Breathe>().Amount = 0.025f;
            }
            return card.rectTransform;
        }

        /// <summary>"Loading" label with a spinning ring in front of it (a bare word looked like a frozen screen).</summary>
        public static Text Loading(Transform parent, Localization loc, float height = 0f)
        {
            Text label = UIFactory.Label(parent, loc.T("common.loading"), Theme.BodySize, Theme.TextMuted);
            if (height > 0f)
            {
                UIFactory.Height(label, height);
            }
            Image ring = UIFactory.Icon(label.transform, ProceduralSprites.Ring(), Theme.Crystal, 64);
            ring.rectTransform.anchorMin = ring.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            ring.rectTransform.anchoredPosition = new Vector2(-label.preferredWidth * 0.5f - 56f, 0f);
            ring.gameObject.AddComponent<Spinner>();
            return label;
        }

        public static Text SectionTitle(Transform parent, string text)
        {
            Text title = UIFactory.Label(parent, text, Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Height(title, 90);
            UIFactory.OverArt(title);
            // Hairline under the heading: separates blocks without adding yet another box to the stack.
            RectTransform rule = UIFactory.Anchor(UIFactory.Rect("Rule", title.transform), 0f, 0.02f, 1f, 0.02f);
            rule.sizeDelta = new Vector2(0, 3);
            Image line = rule.gameObject.AddComponent<Image>();
            line.color = Theme.Hairline;
            line.raycastTarget = false;
            return title;
        }

        /// <summary>Ornamental gold rail with a crystal centre, for separating blocks inside a scroll list.</summary>
        public static void Separator(Transform parent)
        {
            if (UiKit.Divider(parent) == null)
            {
                Image line = UIFactory.Panel("Separator", parent, Theme.Hairline, rounded: false);
                UIFactory.Height(line, 3);
            }
        }

        /// <summary>Card with a vertical layout; returns the content transform.</summary>
        public static RectTransform Card(Transform parent, float height, Color? color = null)
        {
            Image card = UIFactory.Panel("Card", parent, color ?? Theme.Panel);
            if (!color.HasValue)
            {
                // Glass by default: the backdrop art shows through and the row gets a shadow and a lit top lip.
                UiKit.GlassCard(card);
            }
            UIFactory.Height(card, height);
            VerticalLayoutGroup layout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 20, 20);
            layout.spacing = 8;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = true;
            return card.rectTransform;
        }

        /// <summary>Is a server-gated feature available (offline practice only allows story and PvP practice)?</summary>
        public static bool FeatureUnlocked(string feature)
        {
            GameRoot game = GameRoot.Instance;
            if (!game.Backend.IsOnline)
            {
                return feature == "Story" || feature == "PvpPractice";
            }
            if (feature == "Story")
            {
                return true;
            }
            List<string> unlocked = game.Backend.Profile?.Story?.UnlockedFeatures;
            return unlocked != null && unlocked.Contains(feature);
        }

        private static string OnOff(bool value) => GameRoot.Instance.Loc.T(value ? "common.on" : "common.off");

        /// <summary>Puts the power-up illustration on the left of a button and shifts its label to the right.</summary>
        public static void AddPowerUpIcon(Button button, CrushRoyale.Core.Config.PowerUpType type)
        {
            Sprite art = ArtLibrary.PowerUp(type);
            if (art == null)
            {
                return;
            }
            Text label = button.GetComponentInChildren<Text>();
            Image icon = UIFactory.Icon(button.transform, art, Color.white, 0);
            UIFactory.Anchor(icon.rectTransform, 0.03f, 0.1f, 0.33f, 0.9f);
            if (label != null)
            {
                UIFactory.Anchor(label.rectTransform, 0.34f, 0.05f, 0.97f, 0.95f);
            }
        }

        /// <summary>Full-screen kingdom illustration behind everything else, darkened so text stays readable. Null without art.</summary>
        /// <summary>Dark gradient over the top or bottom of the screen so HUD elements stay readable on bright art.</summary>
        public static Image Fade(Transform parent, bool top, float height, float alpha)
        {
            RectTransform rect = UIFactory.Rect(top ? "FadeTop" : "FadeBottom", parent);
            UIFactory.Anchor(rect, 0, top ? 1f - height : 0f, 1, top ? 1f : height);
            rect.SetSiblingIndex(Mathf.Min(1, parent.childCount - 1));
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = ProceduralSprites.VerticalFade(top);
            image.color = new Color(0.03f, 0.015f, 0.08f, alpha);
            image.raycastTarget = false;
            return image;
        }

        public static Image Backdrop(Transform parent, CrushRoyale.Core.Story.Kingdom kingdom, float brightness = 0.72f) =>
            Place(parent, ArtLibrary.Background(kingdom), brightness);

        /// <summary>
        /// Per-screen backdrop. <paramref name="sceneId"/> names an image in Resources/Art/Backgrounds ("hub",
        /// "shop", "vip", "settings"...); a "scene_" prefixed file wins, then the bare name, and when neither exists
        /// the castle art the whole game used before is kept, so a screen never ends up on a flat colour.
        /// Adds the tilt parallax and a slow drift of dust motes on top (both off under reduced motion).
        /// </summary>
        public static Image Backdrop(Transform parent, string sceneId, float brightness = 0.55f)
        {
            Sprite sprite = null;
            if (!string.IsNullOrEmpty(sceneId))
            {
                sprite = ArtLibrary.Load("Art/Backgrounds/scene_" + sceneId) ?? ArtLibrary.Load("Art/Backgrounds/" + sceneId);
            }
            return Place(parent, sprite ?? ArtLibrary.Background(CrushRoyale.Core.Story.Kingdom.Central), brightness);
        }

        private static Image Place(Transform parent, Sprite sprite, float brightness)
        {
            if (sprite == null)
            {
                return null;
            }

            // Wrapper slightly larger than the screen that drifts with the phone's tilt (2.5D parallax depth).
            RectTransform holder = UIFactory.Stretch(UIFactory.Rect("Backdrop", parent));
            holder.SetAsFirstSibling();
            holder.localScale = new Vector3(1.06f, 1.06f, 1f);
            holder.gameObject.AddComponent<Parallax>();
            RectTransform rect = UIFactory.Stretch(UIFactory.Rect("Art", holder));
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.color = new Color(brightness, brightness, brightness * 1.06f, 1f);
            AspectRatioFitter fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;

            // Motes drift in their own layer above the art but behind everything else: the screen is never dead still.
            Dust.Attach(UIFactory.Stretch(UIFactory.Rect("Dust", holder)));
            return image;
        }
    }

    /// <summary>
    /// Slow drift of dust motes over a backdrop. The particles are created once and then only have their position and
    /// alpha written, so the effect costs no allocation per frame; it switches itself off under reduced motion.
    /// </summary>
    public sealed class Dust : MonoBehaviour
    {
        /// <summary>Few on purpose: enough to feel alive on a phone, cheap enough not to cost a draw call batch.</summary>
        public int Count = 14;

        private RectTransform _rect;
        private RectTransform[] _motes;
        private Image[] _images;
        private Vector2[] _velocity;
        private float[] _phase;

        public static Dust Attach(RectTransform parent)
        {
            if (GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion)
            {
                return null;
            }
            return parent.gameObject.AddComponent<Dust>();
        }

        private void Start()
        {
            _rect = (RectTransform)transform;
            _motes = new RectTransform[Count];
            _images = new Image[Count];
            _velocity = new Vector2[Count];
            _phase = new float[Count];
            Sprite sprite = ProceduralSprites.Glow(32);
            for (int i = 0; i < Count; i++)
            {
                float size = UnityEngine.Random.Range(6f, 16f);
                Image image = UIFactory.Icon(_rect, sprite, new Color(1f, 0.93f, 0.72f, UnityEngine.Random.Range(0.12f, 0.34f)), size);
                image.rectTransform.anchorMin = image.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                image.rectTransform.anchoredPosition = new Vector2(UnityEngine.Random.Range(-560f, 560f), UnityEngine.Random.Range(-960f, 960f));
                _motes[i] = image.rectTransform;
                _images[i] = image;
                _velocity[i] = new Vector2(UnityEngine.Random.Range(-6f, 6f), UnityEngine.Random.Range(9f, 26f));
                _phase[i] = UnityEngine.Random.value * 6.28f;
            }
        }

        private void Update()
        {
            if (_motes == null)
            {
                return;
            }
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;
            for (int i = 0; i < _motes.Length; i++)
            {
                RectTransform mote = _motes[i];
                Vector2 position = mote.anchoredPosition + _velocity[i] * dt;
                position.x += Mathf.Sin(now * 0.6f + _phase[i]) * 8f * dt;
                if (position.y > 1000f)
                {
                    // Wrap instead of respawning: no allocation, and the field never thins out.
                    position = new Vector2(UnityEngine.Random.Range(-560f, 560f), -1000f);
                }
                mote.anchoredPosition = position;
                Color color = _images[i].color;
                _images[i].color = new Color(color.r, color.g, color.b, Mathf.Abs(Mathf.Sin(now * 0.35f + _phase[i])) * 0.3f + 0.08f);
            }
        }
    }

    /// <summary>
    /// Keeps a fixed column count inside the real width: the canvas is narrower than 1080 units on tall phones, so
    /// fixed cell widths overflowed on the right. Cell width follows the rect; height keeps its value.
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    public sealed class GridFit : MonoBehaviour
    {
        /// <summary>Narrower than this and the rect is simply not laid out yet, whatever the number says.</summary>
        private const float MinCellWidth = 40f;

        private GridLayoutGroup _grid;

        public static GridFit On(GridLayoutGroup grid)
        {
            GridFit fit = grid.GetComponent<GridFit>() ?? grid.gameObject.AddComponent<GridFit>();
            fit._grid = grid;
            fit.Refresh();
            return fit;
        }

        private void OnEnable() => Refresh();

        private void OnRectTransformDimensionsChange() => Refresh();

        private void Refresh()
        {
            if (_grid == null)
            {
                _grid = GetComponent<GridLayoutGroup>();
            }
            float width = ((RectTransform)transform).rect.width;
            int columns = Mathf.Max(1, _grid.constraintCount);
            if (width <= 1f || _grid.constraint != GridLayoutGroup.Constraint.FixedColumnCount)
            {
                return;
            }
            float usable = width - _grid.padding.left - _grid.padding.right - _grid.spacing.x * (columns - 1);
            if (usable < columns * MinCellWidth)
            {
                // The rect is not laid out yet: padding and spacing alone are wider than it. Computing from that gave
                // a negative cell, which was clamped to 10 px and never revisited, and the PvP boosts came out as
                // one-pixel columns with their names running vertically. Keep the authored size until a real width
                // arrives; OnRectTransformDimensionsChange brings us back.
                return;
            }
            float cell = usable / columns;
            if (Mathf.Abs(cell - _grid.cellSize.x) > 0.5f)
            {
                _grid.cellSize = new Vector2(cell, _grid.cellSize.y);
            }
        }
    }

    /// <summary>Springy scale-in when a popup, card or reward appears.</summary>
    public sealed class PopIn : MonoBehaviour
    {
        public float Delay;
        public float Duration = 0.32f;

        private float _start = -1f;

        /// <summary>Plays the pop again (e.g. a caption whose text just changed).</summary>
        public void Replay()
        {
            enabled = false;
            enabled = true;
        }

        private void OnEnable()
        {
            // The start time is taken on the first frame so a Delay set right after AddComponent is honoured.
            _start = -1f;
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            if (_start < 0f)
            {
                _start = Time.unscaledTime + Delay;
            }
            float t = (Time.unscaledTime - _start) / Duration;
            if (t < 0f)
            {
                transform.localScale = Vector3.zero;
                return;
            }
            if (t >= 1f)
            {
                transform.localScale = Vector3.one;
                enabled = false;
                return;
            }
            // Ease-out-back: overshoots a little then settles.
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float k = 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.6f, 1f, k);
        }
    }

    /// <summary>
    /// Tilt parallax: the layer drifts a few pixels against the phone's tilt (accelerometer), smoothed, so backgrounds
    /// feel deeper than the UI in front of them. Without an accelerometer (editor) it slowly sways instead.
    /// Disabled with reduced motion.
    /// </summary>
    public sealed class Parallax : MonoBehaviour
    {
        public float Range = 26f;

        private RectTransform _rect;
        private Vector2 _offset;
        private float _phase;
        private bool _reduceMotion;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _phase = UnityEngine.Random.value * 6f;
            _reduceMotion = GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion;
        }

        private void Update()
        {
            if (_reduceMotion)
            {
                return;
            }
            Vector3 tilt = Input.acceleration;
            Vector2 target = tilt.sqrMagnitude > 0.01f
                ? new Vector2(Mathf.Clamp(-tilt.x, -1f, 1f), Mathf.Clamp(-(tilt.y + 0.55f), -1f, 1f)) * Range
                : new Vector2(Mathf.Sin(Time.unscaledTime * 0.21f + _phase), Mathf.Sin(Time.unscaledTime * 0.17f + _phase) * 0.6f) * Range * 0.5f;
            _offset = Vector2.Lerp(_offset, target, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 4f));
            _rect.anchoredPosition = _offset;
        }
    }

    /// <summary>
    /// Idle "breathing" of a character or a call-to-action (scale around the pivot). This is the gentle life that
    /// keeps a primary button from looking like a printed rectangle; it stands down while the button is being
    /// pressed, because ButtonFeedback owns the scale then, and entirely under reduced motion.
    /// </summary>
    public sealed class Breathe : MonoBehaviour
    {
        public float Amount = 0.014f;
        public float Speed = 1.7f;

        private float _phase;
        private ButtonFeedback _press;

        private void Awake()
        {
            _phase = UnityEngine.Random.value * 6f;
            _press = GetComponent<ButtonFeedback>();
            if (GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (_press != null && _press.enabled)
            {
                return;
            }
            float k = Mathf.Sin(Time.unscaledTime * Speed + _phase);
            transform.localScale = new Vector3(1f - k * Amount * 0.4f, 1f + k * Amount, 1f);
        }
    }

    /// <summary>Soft alpha (or scale) pulse for halos and notification badges.</summary>
    public sealed class Pulse : MonoBehaviour
    {
        public bool Scale;
        public float Speed = 2.2f;

        private Image _image;
        private float _baseAlpha;

        private void Awake()
        {
            _image = GetComponent<Image>();
            _baseAlpha = _image != null ? _image.color.a : 1f;
            if (GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion)
            {
                // Accessibility: a pulsing halo is motion too. The element keeps its resting alpha.
                enabled = false;
            }
        }

        private void Update()
        {
            float k = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Speed);
            if (Scale)
            {
                transform.localScale = Vector3.one * (1f + 0.12f * k);
            }
            else if (_image != null)
            {
                Color c = _image.color;
                _image.color = new Color(c.r, c.g, c.b, _baseAlpha * (0.65f + 0.35f * k));
            }
        }
    }

    /// <summary>Top bar: coins, orbes, lives with recharge countdown. Updates itself from profile changes.</summary>
    public sealed class CurrencyBar : MonoBehaviour
    {
        private Text _coins;
        private Text _orbes;
        private Text _lives;
        private float _rechargeReceivedAt;
        private LivesDto _livesData;

        public static CurrencyBar Create(Transform parent)
        {
            Image bar = UIFactory.Panel("CurrencyBar", parent, Theme.BackgroundLight);
            CurrencyBar component = bar.gameObject.AddComponent<CurrencyBar>();
            HorizontalLayoutGroup row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 24, 8, 8);
            row.spacing = 16;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            component._lives = CurrencyLabel(bar.transform, "heart", Theme.Danger);
            component._coins = CurrencyLabel(bar.transform, "coin", Theme.Gold);
            component._orbes = CurrencyLabel(bar.transform, "orb", Theme.Orbe);
            component.Apply(GameRoot.Instance.Backend.Profile);
            return component;
        }

        /// <summary>Illustrated icon (when available) followed by a label that shares the remaining width.</summary>
        private static Text CurrencyLabel(Transform bar, string icon, Color color)
        {
            Sprite art = ArtLibrary.Icon(icon);
            if (art != null)
            {
                UIFactory.Width(UIFactory.Icon(bar, art, Color.white, 64), 64);
            }
            Text label = UIFactory.Label(bar, string.Empty, Theme.BodySize, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            return label;
        }

        private void OnEnable() => GameRoot.Instance.Backend.ProfileChanged += Apply;

        private void OnDisable()
        {
            if (GameRoot.Instance != null)
            {
                GameRoot.Instance.Backend.ProfileChanged -= Apply;
            }
        }

        private void Apply(ProfileDto profile)
        {
            if (_coins == null)
            {
                return;
            }
            Localization loc = GameRoot.Instance.Loc;
            if (profile == null)
            {
                _coins.text = loc.T("common.offline");
                _orbes.text = string.Empty;
                _lives.text = string.Empty;
                return;
            }
            _coins.text = loc.T("currency.coins", loc.Number(profile.Wallet.Coins));
            _orbes.text = loc.T("currency.orbes", loc.Number(profile.Wallet.Orbes));
            _livesData = profile.Lives;
            _rechargeReceivedAt = Time.realtimeSinceStartup;
            RefreshLives();
        }

        private void Update()
        {
            if (_livesData != null && Time.frameCount % 30 == 0)
            {
                RefreshLives();
            }
        }

        private void RefreshLives()
        {
            if (_livesData == null)
            {
                return;
            }
            Localization loc = GameRoot.Instance.Loc;
            float elapsed = Time.realtimeSinceStartup - _rechargeReceivedAt;

            // A window of free play replaces the counter entirely: during it there is nothing to count down to, and
            // showing a life total would suggest a wall that is not there.
            float freeLeft = _livesData.UnlimitedSecondsLeft - elapsed;
            if (freeLeft > 0)
            {
                int minutes = (int)(freeLeft / 60);
                _lives.text = loc.T("currency.unlimitedLives", minutes + ":" + ((int)freeLeft % 60).ToString("00"));
                return;
            }

            float remaining = Mathf.Max(0, _livesData.RechargeSeconds - elapsed);
            string timer = _livesData.Lives < _livesData.MaxRegen && remaining > 0
                ? " " + (int)(remaining / 60) + ":" + ((int)remaining % 60).ToString("00")
                : string.Empty;
            _lives.text = loc.T("currency.lives", _livesData.Lives) + timer;
        }
    }
}
