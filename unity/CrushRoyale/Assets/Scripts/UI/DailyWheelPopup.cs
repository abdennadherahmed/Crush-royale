using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Economy;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Free daily wheel: eight illustrated slices, a gold pointer, a 4-second spin easing out onto the slice the server
    /// rolled, then the prize reveal. Odds are listed under the wheel.
    /// </summary>
    public sealed class DailyWheelPopup : MonoBehaviour
    {
        private static readonly float SliceDegrees = 360f / 8f;

        private RectTransform _wheel;
        private Button _spin;
        private bool _spinning;

        public static void Show()
        {
            GameRoot game = GameRoot.Instance;
            RectTransform box = game.UI.Popup(0.1f, 0.9f);
            box.gameObject.AddComponent<DailyWheelPopup>().Build(box);
        }

        private static string SliceArt(WheelPrize prize)
        {
            switch (prize)
            {
                case WheelPrize.Coins: return "item_coins";
                case WheelPrize.Orbes: return "item_orbs";
                case WheelPrize.Boost: return "item_bolt";
                case WheelPrize.Lives: return "item_heart";
                case WheelPrize.PetFragments: return "item_fragment";
                default: return "item_crown";
            }
        }

        private void Build(RectTransform box)
        {
            GameRoot game = GameRoot.Instance;
            Localization loc = game.Loc;

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", box), -0.03f, 0.9f, 1.03f, 1.03f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, loc.T("wheel.title"), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
            Widgets.TitleOutline(title);

            RectTransform stage = UIFactory.Anchor(UIFactory.Rect("WheelArea", box), 0.06f, 0.33f, 0.94f, 0.88f);
            stage.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            Image halo = UIFactory.Icon(stage, ProceduralSprites.Glow(128), new Color(1f, 0.8f, 0.35f, 0.55f), 0);
            UIFactory.Stretch(halo.rectTransform, -60, -60, -60, -60);
            halo.raycastTarget = false;
            halo.gameObject.AddComponent<Pulse>();

            _wheel = UIFactory.Stretch(UIFactory.Rect("Wheel", stage));
            Sprite wheelArt = UiKit.Art("wheel");
            Image disc = _wheel.gameObject.AddComponent<Image>();
            disc.sprite = wheelArt != null ? wheelArt : ProceduralSprites.Circle();
            disc.color = wheelArt != null ? Color.white : Theme.GoldDark;
            disc.raycastTarget = false;

            IReadOnlyList<WheelSlice> slices = DailyWheel.Slices;
            for (int i = 0; i < slices.Count; i++)
            {
                // Slices are laid clockwise from the top.
                float angle = i * SliceDegrees * Mathf.Deg2Rad;
                var center = new Vector2(0.5f + Mathf.Sin(angle) * 0.31f, 0.5f + Mathf.Cos(angle) * 0.31f);
                RectTransform cell = UIFactory.Rect("Slice" + i, _wheel);
                cell.anchorMin = cell.anchorMax = center;
                cell.sizeDelta = new Vector2(150, 170);
                cell.localRotation = Quaternion.Euler(0, 0, -i * SliceDegrees);
                Sprite art = UiKit.Art(SliceArt(slices[i].Prize));
                if (art != null)
                {
                    Image icon = UIFactory.Icon(cell, art, Color.white, 0);
                    icon.preserveAspect = true;
                    icon.raycastTarget = false;
                    UIFactory.Anchor(icon.rectTransform, 0.08f, 0.3f, 0.92f, 1f);
                }
                Text amount = UIFactory.Label(cell, SliceLabel(loc, slices[i]), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                amount.raycastTarget = false;
                UIFactory.Anchor(amount.rectTransform, -0.2f, 0f, 1.2f, 0.32f);
                Widgets.TitleOutline(amount);
            }

            // Pointer at the top.
            Image pointer = UIFactory.Icon(stage, ProceduralSprites.Gem(ProceduralSprites.GemShape.Triangle), Theme.Gold, 0);
            pointer.raycastTarget = false;
            pointer.rectTransform.anchorMin = pointer.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            pointer.rectTransform.sizeDelta = new Vector2(110, 110);
            pointer.rectTransform.anchoredPosition = new Vector2(0, 10);
            pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, 180f);
            Outline outline = pointer.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.3f, 0.15f, 0f, 1f);
            outline.effectDistance = new Vector2(4, -4);

            // Odds (Google Play: random rewards show their probabilities).
            int total = DailyWheel.TotalWeight;
            var odds = new List<string>();
            foreach (WheelSlice slice in slices)
            {
                odds.Add(SliceLabel(loc, slice) + " " + Mathf.RoundToInt(slice.Weight * 100f / total) + "%");
            }
            Text oddsText = UIFactory.Label(box, loc.T("wheel.odds") + "  " + string.Join(" · ", odds), Theme.SmallSize - 10, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(oddsText.rectTransform, 0.05f, 0.22f, 0.95f, 0.31f);

            bool available = game.Backend.Profile?.WheelAvailable ?? false;
            _spin = UIFactory.Button(box, loc.T(available ? "wheel.spin" : "wheel.tomorrow"), () => _ = SpinAsync(), available ? Theme.Success : Theme.PanelLight, Theme.HeaderSize);
            UIFactory.Anchor(_spin.GetComponent<RectTransform>(), 0.18f, 0.1f, 0.82f, 0.21f);
            _spin.interactable = available && game.Backend.IsOnline;
            if (available)
            {
                _spin.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }
            Button close = UIFactory.Button(box, loc.T("common.close"), Close, Theme.PanelLight, Theme.BodySize);
            UIFactory.Anchor(close.GetComponent<RectTransform>(), 0.3f, 0.015f, 0.7f, 0.085f);
        }

        private static string SliceLabel(Localization loc, WheelSlice slice)
        {
            switch (slice.Prize)
            {
                case WheelPrize.Boost: return "x" + slice.Amount;
                case WheelPrize.Lives: return "+" + slice.Amount;
                case WheelPrize.Jackpot: return loc.T("wheel.jackpot");
                default: return loc.Number(slice.Amount);
            }
        }

        private void Close()
        {
            if (!_spinning && transform.parent != null)
            {
                Destroy(transform.parent.gameObject);
            }
        }

        private async Task SpinAsync()
        {
            GameRoot game = GameRoot.Instance;
            if (_spinning || game.Backend.Client == null)
            {
                return;
            }
            _spinning = true;
            _spin.interactable = false;
            WheelSpinResponse response;
            try
            {
                response = await game.Backend.Client.Api.SpinWheelAsync();
            }
            catch (CrushApiException ex)
            {
                _spinning = false;
                game.UI.ShowError(ex);
                return;
            }
            if (this == null)
            {
                return;
            }

            var done = new TaskCompletionSource<bool>();
            StartCoroutine(Rotate(response.SliceIndex, done));
            await done.Task;

            ProfileDto profile = game.Backend.Profile;
            if (profile != null)
            {
                profile.WheelAvailable = false;
                profile.Pets = response.Pets ?? profile.Pets;
            }
            game.Backend.ApplyWallet(response.Wallet);
            game.Backend.ApplyInventory(response.Inventory);
            game.Telemetry.Track("wheel_spin", ("slice", response.SliceIndex));

            List<RevealItem> items = RevealOverlay.FromReward(response.Reward);
            if (response.PetFragments > 0 && !string.IsNullOrEmpty(response.FragmentsPet))
            {
                items.Add(new RevealItem { Art = PetsScreen.PetArt(response.FragmentsPet), Caption = game.Loc.T("pets.fragmentsGain", response.PetFragments), Rare = true });
            }
            _spinning = false;
            if (this != null && transform.parent != null)
            {
                Destroy(transform.parent.gameObject);
            }
            Sprite gift = UiKit.Art("item_gift");
            await RevealOverlay.PlayChestAsync(items, gift, gift, game.Loc.T("wheel.won"));
            if (game.UI.Current is MainMenuScreen hub)
            {
                hub.Rebuild();
            }
        }

        /// <summary>Five full turns then eases out so the rolled slice stops under the pointer, ticking at each slice.</summary>
        private IEnumerator Rotate(int slice, TaskCompletionSource<bool> done)
        {
            GameRoot game = GameRoot.Instance;
            float from = _wheel.localEulerAngles.z;
            float to = 360f * 5f + slice * SliceDegrees + Random.Range(-14f, 14f);
            const float duration = 4.2f;
            int lastTick = -1;
            for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
            {
                float k = t / duration;
                float eased = 1f - Mathf.Pow(1f - k, 4f);
                float z = Mathf.Lerp(from, to, eased);
                _wheel.localRotation = Quaternion.Euler(0, 0, z);
                int tick = Mathf.FloorToInt((z + SliceDegrees / 2f) / SliceDegrees);
                if (tick != lastTick)
                {
                    lastTick = tick;
                    game.Audio.PlaySFX(SoundIds.Click);
                }
                yield return null;
            }
            _wheel.localRotation = Quaternion.Euler(0, 0, to);
            game.Audio.PlaySFX(SoundIds.WinFanfare);
            yield return new WaitForSecondsRealtime(0.6f);
            done.TrySetResult(true);
        }
    }
}
