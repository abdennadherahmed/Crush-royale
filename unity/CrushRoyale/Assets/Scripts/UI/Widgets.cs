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
                        buttons[i].color = i == active ? Theme.GoldDark : Theme.PanelLight;
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
                button.GetComponent<Image>().color = current ? Theme.Success : Theme.PanelLight;
                onChange(current);
            }, current ? Theme.Success : Theme.PanelLight, Theme.BodySize, Theme.Text);
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
            row.childForceExpandWidth = row.childForceExpandHeight = true;

            component._lives = UIFactory.Label(bar.transform, string.Empty, Theme.BodySize, Theme.Danger, TextAnchor.MiddleLeft, FontStyle.Bold);
            component._coins = UIFactory.Label(bar.transform, string.Empty, Theme.BodySize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            component._orbes = UIFactory.Label(bar.transform, string.Empty, Theme.BodySize, Theme.Orbe, TextAnchor.MiddleRight, FontStyle.Bold);
            component.Apply(GameRoot.Instance.Backend.Profile);
            return component;
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
