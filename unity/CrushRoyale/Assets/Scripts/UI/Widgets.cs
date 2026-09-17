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

        /// <summary>Row of tab buttons; returns a setter to highlight the active tab.</summary>
        public static Action<int> Tabs(Transform parent, IList<string> labels, Action<int> onSelect)
        {
            HorizontalLayoutGroup row = UIFactory.Row(parent, 110, 12);
            var buttons = new List<Image>();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                Button button = UIFactory.Button(row.transform, labels[i], () => onSelect(index), Theme.PanelLight, Theme.SmallSize + 2, Theme.Text);
                buttons.Add(button.GetComponent<Image>());
            }
            return active =>
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    if (buttons[i] != null)
                    {
                        UiKit.Recolor(buttons[i], i == active ? Theme.GoldDark : Theme.PanelLight);
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
            display = UIFactory.Label(row.transform, Mathf.RoundToInt(current * 100) + "%", Theme.BodySize, Theme.Gold);
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

        public static Text SectionTitle(Transform parent, string text)
        {
            Text title = UIFactory.Label(parent, text, Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Height(title, 90);
            return title;
        }

        /// <summary>Card with a vertical layout; returns the content transform.</summary>
        public static RectTransform Card(Transform parent, float height, Color? color = null)
        {
            Image card = UIFactory.Panel("Card", parent, color ?? Theme.Panel);
            if (!color.HasValue)
            {
                UiKit.CardFrame(card);
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

        public static Image Backdrop(Transform parent, CrushRoyale.Core.Story.Kingdom kingdom, float brightness = 0.72f)
        {
            Sprite sprite = ArtLibrary.Background(kingdom);
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
            return image;
        }
    }

    /// <summary>
    /// Keeps a fixed column count inside the real width: the canvas is narrower than 1080 units on tall phones, so
    /// fixed cell widths overflowed on the right. Cell width follows the rect; height keeps its value.
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    public sealed class GridFit : MonoBehaviour
    {
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
            float cell = (width - _grid.padding.left - _grid.padding.right - _grid.spacing.x * (columns - 1)) / columns;
            if (Mathf.Abs(cell - _grid.cellSize.x) > 0.5f)
            {
                _grid.cellSize = new Vector2(Mathf.Max(10f, cell), _grid.cellSize.y);
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

    /// <summary>Idle "breathing" of a character or a call-to-action (scale around the pivot).</summary>
    public sealed class Breathe : MonoBehaviour
    {
        public float Amount = 0.014f;
        public float Speed = 1.7f;

        private float _phase;

        private void Awake() => _phase = UnityEngine.Random.value * 6f;

        private void Update()
        {
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
            float remaining = Mathf.Max(0, _livesData.RechargeSeconds - (Time.realtimeSinceStartup - _rechargeReceivedAt));
            string timer = _livesData.Lives < _livesData.MaxRegen && remaining > 0
                ? " " + (int)(remaining / 60) + ":" + ((int)remaining % 60).ToString("00")
                : string.Empty;
            _lives.text = loc.T("currency.lives", _livesData.Lives) + timer;
        }
    }
}
