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

        public static Image Panel(string name, Transform parent, Color color, bool rounded = true)
        {
            RectTransform rect = Rect(name, parent);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (rounded)
            {
                image.sprite = ProceduralSprites.RoundedRect(28);
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        public static Text Label(Transform parent, string text, int size = Theme.BodySize, Color? color = null, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal)
        {
            RectTransform rect = Rect("Label", parent);
            Text label = rect.gameObject.AddComponent<Text>();
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

        public static Button Button(Transform parent, string text, Action onClick, Color? color = null, int fontSize = Theme.BodySize, Color? textColor = null)
        {
            Image image = Panel("Button", parent, color ?? Theme.Gold);
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1, 1, 1, 1);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
            button.colors = colors;

            Text label = Label(image.transform, text, fontSize, textColor ?? (color.HasValue ? Theme.Text : Theme.Background), TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform, 16, 16, 8, 8);

            image.gameObject.AddComponent<ButtonFeedback>();
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
            RectTransform scroll = Rect("Scroll", parent);
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

        public static HorizontalLayoutGroup Row(Transform parent, float height, float spacing = 16)
        {
            RectTransform rect = Rect("Row", parent);
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

    /// <summary>Press animation, click sound and light haptic on every button.</summary>
    public sealed class ButtonFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IPointerClickHandler
    {
        private Vector3 _baseScale = Vector3.one;
        private float _target = 1f;

        private void Awake() => _baseScale = transform.localScale;

        public void OnPointerDown(PointerEventData eventData) => _target = 0.94f;

        public void OnPointerUp(PointerEventData eventData) => _target = 1f;

        public void OnPointerExit(PointerEventData eventData) => _target = 1f;

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

        private void Update()
        {
            float current = transform.localScale.x / Mathf.Max(0.0001f, _baseScale.x);
            float next = Mathf.MoveTowards(current, _target, Time.unscaledDeltaTime * 2.5f);
            transform.localScale = _baseScale * next;
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
