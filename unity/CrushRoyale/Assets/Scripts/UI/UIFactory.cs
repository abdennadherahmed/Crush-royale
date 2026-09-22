using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>Builds uGUI widgets from code (portrait 1080x1920 reference). No prefabs or scenes needed.</summary>
    public static class UIFactory
    {
        private static Font _font;

        /// <summary>OS fonts first so Arabic, Cyrillic and CJK render on Android; Unity's built-in font as fallback.</summary>
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    try
                    {
                        _font = Font.CreateDynamicFontFromOSFont(new[] { "Roboto", "Noto Sans", "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans CJK SC", "Noto Naskh Arabic", "Droid Sans Fallback", "Arial" }, 40);
                    }
                    catch (Exception)
                    {
                        _font = null;
                    }
                    if (_font == null)
                    {
                        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    }
                }
                return _font;
            }
        }

        private static Font _titleFont;
        private static bool _titleFontLoaded;

        /// <summary>Cinzel Decorative (OFL) from Resources/Fonts for titles; null when the file is absent.</summary>
        public static Font TitleFont
        {
            get
            {
                if (!_titleFontLoaded)
                {
                    _titleFontLoaded = true;
                    _titleFont = Resources.Load<Font>("Fonts/CinzelDecorative-Bold");
                }
                return _titleFont;
            }
        }

        /// <summary>Bold header-sized text uses the title font when it has every glyph (Latin languages), else the OS font.</summary>
        private static bool UsesTitleFont(string text, int size, FontStyle style)
        {
            Font title = TitleFont;
            if (title == null || style != FontStyle.Bold || size < Theme.HeaderSize || string.IsNullOrEmpty(text))
            {
                return false;
            }
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c) && !title.HasCharacter(c))
                {
                    return false;
                }
            }
            return true;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = parent != null ? parent.gameObject.layer : 5;
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rect, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        /// <summary>Anchors in normalized coordinates (0-1, origin bottom-left) with an optional pixel inset.</summary>
        public static RectTransform Anchor(RectTransform rect, float minX, float minY, float maxX, float maxY, float inset = 0)
        {
            rect.anchorMin = new Vector2(minX, minY);
            rect.anchorMax = new Vector2(maxX, maxY);
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        /// <summary>
        /// Flat surface tones the screens used before the depth pass. Mapping them here is what upgrades every screen
        /// at once: a screen that still asks for Theme.Panel now gets a glass card with a shadow and a lit top lip.
        /// </summary>
        private static bool TryToneFor(Color color, out ProceduralSprites.SurfaceTone tone)
        {
            tone = ProceduralSprites.SurfaceTone.Glass;
            if (color.a < 0.99f)
            {
                // Already a deliberate translucency (overlays, shades): leave it alone.
                return false;
            }
            if (Same(color, Theme.Panel))
            {
                tone = ProceduralSprites.SurfaceTone.Glass;
                return true;
            }
            if (Same(color, Theme.PanelLight))
            {
                tone = ProceduralSprites.SurfaceTone.Raised;
                return true;
            }
            if (Same(color, Theme.BackgroundLight))
            {
                tone = ProceduralSprites.SurfaceTone.Sunken;
                return true;
            }
            // Theme.Background stays flat on purpose: it is used for full-bleed page fills, not for cards.
            return false;
        }

        private static bool Same(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

        /// <summary>
        /// Rounded surface. The four flat theme tones are swapped for the matching glass tile (shadow, translucent
        /// body, rim hairline, inner top gleam) so nothing in the game is a plain rectangle any more; every other
        /// color keeps the flat rounded look, because those are fills, chips and buttons that get their own art.
        /// </summary>
        public static Image Panel(string name, Transform parent, Color color, bool rounded = true)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (!rounded)
            {
                return image;
            }
            if (TryToneFor(color, out ProceduralSprites.SurfaceTone tone))
            {
                return Dress(image, tone);
            }
            image.sprite = ProceduralSprites.RoundedRect(Theme.Radius);
            image.type = Image.Type.Sliced;
            return image;
        }

        /// <summary>Explicit glass surface at a chosen depth; the tint stays white so the whole atlas batches.</summary>
        public static Image Surface(string name, Transform parent, ProceduralSprites.SurfaceTone tone = ProceduralSprites.SurfaceTone.Glass, int radius = Theme.Radius) =>
            Dress(Rect(name, parent).gameObject.AddComponent<Image>(), tone, radius);

        private static Image Dress(Image image, ProceduralSprites.SurfaceTone tone, int radius = Theme.Radius)
        {
            image.sprite = ProceduralSprites.Surface(tone, radius);
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            return image;
        }

        public const int MinFontSize = 24;

        /// <summary>Roles of the type scale. Using these instead of raw sizes is what keeps the rhythm across screens.</summary>
        public enum TextRole
        {
            /// <summary>One per screen at most: the screen's name on its banner.</summary>
            Display,

            Title,

            /// <summary>Section heading inside a page.</summary>
            Header,

            /// <summary>Card heading: the name of the thing the row is about.</summary>
            Subtitle,

            Body,

            /// <summary>Supporting line under a body line (costs, timers, counts).</summary>
            Small,

            /// <summary>Legal-ish detail; never the only place an information appears.</summary>
            Caption
        }

        public static int Size(TextRole role)
        {
            switch (role)
            {
                case TextRole.Display: return Theme.DisplaySize;
                case TextRole.Title: return Theme.TitleSize;
                case TextRole.Header: return Theme.HeaderSize;
                case TextRole.Subtitle: return Theme.SubtitleSize;
                case TextRole.Small: return Theme.SmallSize;
                case TextRole.Caption: return Theme.CaptionSize;
                default: return Theme.BodySize;
            }
        }

        /// <summary>
        /// Label by role: size, weight and color come from the scale instead of being picked per call site. Roles above
        /// Body are bold and gold-less by default so a screen reads as a hierarchy rather than as a pile of gold text.
        /// </summary>
        public static Text Label(Transform parent, string text, TextRole role, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft)
        {
            bool heading = role <= TextRole.Subtitle;
            Color tint = color ?? (role >= TextRole.Small ? Theme.TextMuted : Theme.Text);
            return Label(parent, text, Size(role), tint, align, heading ? FontStyle.Bold : FontStyle.Normal);
        }

        /// <summary>
        /// Dark outline behind text that sits on illustration or on a glossy face. Text over art without it is the
        /// single thing that made the old screens look cheap, so every title and every button label gets one.
        /// </summary>
        public static Text OverArt(Text text, float strength = 1f)
        {
            Outline outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.12f, 0.04f, 0.18f, 0.92f);
            outline.effectDistance = new Vector2(2f * strength, -2f * strength);
            outline.useGraphicAlpha = true;
            return text;
        }

        public static Text Label(Transform parent, string text, int size = Theme.BodySize, Color? color = null, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = Rect("Label", parent);
            Text label = rect.gameObject.AddComponent<Text>();
            // Legibility floor on phones (1080-wide canvas): nothing smaller than 24 px.
            size = Mathf.Max(size, MinFontSize);
            bool title = UsesTitleFont(text, size, style);
            label.font = title ? TitleFont : Font;
            label.text = text;
            label.fontSize = size;
            // The title font file is already bold: no synthetic bold on top of it.
            label.fontStyle = title ? FontStyle.Normal : style;
            label.color = color ?? Theme.Text;
            label.alignment = align;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(14, size / 2);
            label.resizeTextMaxSize = size;
            label.raycastTarget = false;
            Shadow shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.45f);
            shadow.effectDistance = new Vector2(0, -2);
            return label;
        }

        /// <summary>
        /// Emphasis ladder. Gold is scarce on purpose: one Primary per screen, everything else Secondary or Tertiary,
        /// so the eye lands on the action the screen exists for instead of scanning a wall of identical rectangles.
        /// </summary>
        public enum ButtonTier
        {
            /// <summary>The one thing the screen wants you to do. Gold, sculpted, breathing.</summary>
            Primary,

            /// <summary>Everything else that still is an action. Cold stone: same shape, clearly lesser.</summary>
            Secondary,

            /// <summary>Dismiss, "later", "not now". A glass well with muted text, no illustrated face.</summary>
            Tertiary,

            /// <summary>Confirm / claim / collect.</summary>
            Positive,

            /// <summary>Leave the guild, delete the account. Never the default focus of a screen.</summary>
            Destructive
        }

        public static Button Button(Transform parent, string text, Action onClick, ButtonTier tier, int fontSize = Theme.BodySize) =>
            Build(parent, text, onClick, tier, UiKit.StyleOf(tier), fontSize, null, null);

        public static Button Button(Transform parent, string text, Action onClick, Color? color = null, int fontSize = Theme.BodySize, Color? textColor = null)
        {
            // Screens still pass raw colors: keep the face family the color already chose, and only derive the tier
            // (which drives ink color and idle life) from it, so no screen changes look by accident.
            Color tint = color ?? Theme.Gold;
            UiKit.ButtonStyle style = UiKit.TryStyleFor(tint, out UiKit.ButtonStyle found) ? found : UiKit.ButtonStyle.Gold;
            ButtonTier tier;
            switch (style)
            {
                case UiKit.ButtonStyle.Green: tier = ButtonTier.Positive; break;
                case UiKit.ButtonStyle.Red: tier = ButtonTier.Destructive; break;
                case UiKit.ButtonStyle.Purple:
                case UiKit.ButtonStyle.Stone: tier = ButtonTier.Secondary; break;
                default: tier = ButtonTier.Primary; break;
            }
            return Build(parent, text, onClick, tier, style, fontSize, textColor, tint);
        }

        private static Button Build(Transform parent, string text, Action onClick, ButtonTier tier, UiKit.ButtonStyle style, int fontSize, Color? textColor, Color? flat)
        {
            bool ghost = tier == ButtonTier.Tertiary;
            RectTransform rect = Rect("Button", parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = flat ?? Theme.Gold;

            bool illustrated = !ghost && UiKit.Apply(image, UiKit.Button(style));
            if (!illustrated)
            {
                // No kit art (or a ghost button): a sculpted glass tile still beats a flat rectangle.
                Dress(image, ghost ? ProceduralSprites.SurfaceTone.Sunken : ProceduralSprites.SurfaceTone.Gold, Theme.ButtonRadius);
            }

            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.0f, 0.99f, 0.95f, 1f);
            // Pressed darkens as well as shrinking: on a glossy face the scale alone is easy to miss on a small phone.
            colors.pressedColor = new Color(0.74f, 0.72f, 0.78f, 1f);
            colors.disabledColor = new Color(0.45f, 0.44f, 0.5f, 0.55f);
            colors.fadeDuration = 0.06f;
            button.colors = colors;

            // Gold is bright enough to take dark ink, which is what makes a primary button read as pressed metal.
            Color ink = textColor ?? (ghost ? Theme.TextMuted : tier == ButtonTier.Primary && illustrated ? Theme.Hex("2E1704") : Theme.Text);
            Text label = Label(image.transform, text, fontSize, ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            float side = illustrated ? 30 : 20;
            Stretch(label.rectTransform, side, side, 10, 14);
            if (!ghost && ink != Theme.Text)
            {
                // Dark ink already separates itself; a light lift underneath keeps it from looking printed on.
                Shadow lift = label.GetComponent<Shadow>();
                if (lift != null)
                {
                    lift.effectColor = new Color(1f, 0.93f, 0.7f, 0.55f);
                    lift.effectDistance = new Vector2(0, 2);
                }
            }
            else if (!ghost)
            {
                OverArt(label);
            }

            image.gameObject.AddComponent<ButtonFeedback>();
            if (tier == ButtonTier.Primary)
            {
                // The one gold action on the page breathes: on a still screen the eye goes straight to it.
                image.gameObject.AddComponent<Breathe>().Amount = 0.016f;
            }
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }
            return button;
        }

        public static Image Icon(Transform parent, Sprite sprite, Color color, float size)
        {
            RectTransform rect = Rect("Icon", parent);
            rect.sizeDelta = new Vector2(size, size);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Vertical scroll list; returns the content transform (auto-sized, children laid out top to bottom).</summary>
        public static RectTransform ScrollList(Transform parent, float spacing = 16, int padding = 24)
        {
            // Fills its parent by default: without this the list had a zero size and its content collapsed to a point
            // (Settings, Guild, Friends, Battle Pass, Quests). Callers can still re-stretch it with margins.
            RectTransform scroll = Stretch(Rect("Scroll", parent));
            ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 40;

            RectTransform viewport = Stretch(Rect("Viewport", scroll));
            viewport.gameObject.AddComponent<RectMask2D>();
            Image hit = viewport.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0);

            RectTransform content = Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return content;
        }

        /// <summary>
        /// A horizontal row of children sharing the width evenly.
        ///
        /// The rect is stretched over its parent up front. A bare RectTransform starts in the parent's bottom-left
        /// corner at its default size, and a parent that is not itself a layout group never moves it: the row stayed
        /// 100 px wide in the corner and its children split those 100 px between them. That is why the shop's four
        /// tabs were slivers pressed against the left edge, and why the buttons on the friend cards were stacked on
        /// top of each other. A parent that IS a layout group overwrites these anchors a moment later, so stretching
        /// here is free and cannot break the rows that were already correct.
        /// </summary>
        public static HorizontalLayoutGroup Row(Transform parent, float height, float spacing = 16)
        {
            RectTransform rect = Stretch(Rect("Row", parent));
            LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            HorizontalLayoutGroup row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = spacing;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = true;
            row.childAlignment = TextAnchor.MiddleCenter;
            return row;
        }

        public static LayoutElement Height(Component component, float height)
        {
            LayoutElement element = component.GetComponent<LayoutElement>() ?? component.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
            return element;
        }

        public static LayoutElement Width(Component component, float width)
        {
            LayoutElement element = component.GetComponent<LayoutElement>() ?? component.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = 0;
            return element;
        }

        /// <summary>Horizontal progress bar (0-1).</summary>
        public static Image ProgressBar(Transform parent, float value, Color fill, out RectTransform root)
        {
            if (UiKit.Sliced("bar_track", 0.45f) != null)
            {
                return UiKit.Bar(parent, value, fill, out root);
            }
            Image back = Panel("Progress", parent, Theme.BackgroundLight);
            root = back.rectTransform;
            Image bar = Panel("Fill", back.transform, fill);
            bar.rectTransform.anchorMin = Vector2.zero;
            bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
            bar.rectTransform.offsetMin = new Vector2(4, 4);
            bar.rectTransform.offsetMax = new Vector2(-4, -4);
            return bar;
        }

        public static void SetProgress(Image fill, float value)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
        }

        public static void Clear(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }

    /// <summary>
    /// Press feedback on every button: the face sinks into its lip (scale down plus a downward nudge), springs back
    /// with a small overshoot on release, and plays the click sound and a light haptic. The sink is what makes a
    /// button feel like a physical object rather than a coloured rectangle; the darkening comes from the ColorBlock.
    /// Runs only while the state is changing, so a screen full of idle buttons costs nothing per frame.
    /// </summary>
    public sealed class ButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        /// <summary>How deep the face sinks, as a share of its size.</summary>
        public float Depth = 0.055f;

        private RectTransform _rect;
        private Vector3 _baseScale = Vector3.one;
        private Vector2 _basePosition;
        private float _target = 1f;
        private float _value = 1f;
        private float _velocity;
        private bool _reduceMotion;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _baseScale = _rect.localScale;
            _basePosition = _rect.anchoredPosition;
            _reduceMotion = GameRoot.Instance != null && GameRoot.Instance.Save.Settings.ReduceMotion;
            enabled = false;
        }

        public void OnPointerDown(PointerEventData eventData) => Press(0f);

        public void OnPointerUp(PointerEventData eventData) => Press(1f);

        public void OnPointerExit(PointerEventData eventData) => Press(1f);

        public void OnPointerClick(PointerEventData eventData)
        {
            Button button = GetComponent<Button>();
            if (button != null && !button.interactable)
            {
                return;
            }
            GameRoot game = GameRoot.Instance;
            game?.Audio.PlaySFX(Audio.SoundIds.Click);
            game?.Haptics.Light();
        }

        private void Press(float target)
        {
            _target = target;
            if (_reduceMotion)
            {
                // Accessibility: no travel at all, the ColorBlock darkening is the whole feedback.
                return;
            }
            if (target < 0.5f && _value >= 0.999f)
            {
                // Re-read the resting pose on the way down: a layout group may have moved the button since Awake.
                _baseScale = _rect.localScale;
                _basePosition = _rect.anchoredPosition;
            }
            enabled = true;
        }

        private void Update()
        {
            // Critically damped spring: fast to the pressed pose, a touch of overshoot coming back out.
            float stiffness = _target < 0.5f ? 900f : 420f;
            float damping = _target < 0.5f ? 60f : 26f;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            _velocity += (_target - _value) * stiffness * dt;
            _velocity -= _velocity * Mathf.Min(1f, damping * dt);
            _value += _velocity * dt;

            float sink = (1f - _value) * Depth;
            _rect.localScale = _baseScale * (1f - sink);
            _rect.anchoredPosition = _basePosition + new Vector2(0f, -sink * _rect.rect.height * 0.5f);

            if (Mathf.Abs(_target - _value) < 0.002f && Mathf.Abs(_velocity) < 0.01f)
            {
                // Settle exactly on the target pose (held down stays sunk) and stop ticking until the next event.
                _value = _target;
                _velocity = 0f;
                float rest = (1f - _value) * Depth;
                _rect.localScale = _baseScale * (1f - rest);
                _rect.anchoredPosition = _basePosition + new Vector2(0f, -rest * _rect.rect.height * 0.5f);
                enabled = false;
            }
        }
    }

    /// <summary>Keeps content inside the device safe area (notches, rounded corners).</summary>
    public sealed class SafeArea : MonoBehaviour
    {
        private Rect _applied;

        private void OnEnable() => Apply();

        private void Update()
        {
            if (Screen.safeArea != _applied)
            {
                Apply();
            }
        }

        private void Apply()
        {
            _applied = Screen.safeArea;
            var rect = (RectTransform)transform;
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }
            rect.anchorMin = new Vector2(_applied.xMin / Screen.width, _applied.yMin / Screen.height);
            rect.anchorMax = new Vector2(_applied.xMax / Screen.width, _applied.yMax / Screen.height);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }

    /// <summary>Spinning loader.</summary>
    public sealed class Spinner : MonoBehaviour
    {
        private void Update() => transform.Rotate(0, 0, -360f * Time.unscaledDeltaTime);
    }
}
