using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// The 4 victory chest slots on the hub: tap a locked chest to start its timer, an unlocking one to open it now
    /// with orbes, a ready one to open it. Countdowns tick locally from the last server snapshot.
    /// </summary>
    public sealed class ChestBar : MonoBehaviour
    {
        private readonly Text[] _labels = new Text[4];
        private readonly Image[] _chests = new Image[4];
        private ChestsDto _data;
        private float _receivedAt;
        private UIRoot _ui;
        private bool _busy;

        private static GameRoot Game => GameRoot.Instance;

        public static Sprite ChestArt(string type) => ArtLibrary.Load("Art/Chests/" + (type ?? "wood").ToLowerInvariant());

        public static ChestBar Create(RectTransform parent, UIRoot ui)
        {
            RectTransform root = UIFactory.Rect("Chests", parent);
            ChestBar bar = root.gameObject.AddComponent<ChestBar>();
            bar._ui = ui;
            for (int i = 0; i < 4; i++)
            {
                int slot = i;
                Button button = UIFactory.Button(root, string.Empty, () => _ = bar.OnTapAsync(slot), new Color(0.06f, 0.04f, 0.14f, 0.82f));
                UiKit.CardFrame(button.GetComponent<Image>());
                UIFactory.Anchor(button.GetComponent<RectTransform>(), i * 0.25f + 0.008f, 0f, (i + 1) * 0.25f - 0.008f, 1f);
                Image chest = UIFactory.Icon(button.transform, null, Color.white, 0);
                UIFactory.Anchor(chest.rectTransform, 0.12f, 0.34f, 0.88f, 1.05f);
                bar._chests[i] = chest;
                Text label = UIFactory.Label(button.transform, string.Empty, Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.36f);
                Outline outline = label.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
                bar._labels[i] = label;
            }
            bar.Apply(Game.Backend.Profile?.Chests);
            return bar;
        }

        private void Apply(ChestsDto data)
        {
            _data = data;
            _receivedAt = Time.realtimeSinceStartup;
            Refresh();
        }

        private void Update()
        {
            if (Time.frameCount % 15 == 0)
            {
                Refresh();
            }
        }

        private int SecondsLeft(ChestSlotDto slot)
        {
            if (slot.Status != "Unlocking")
            {
                return slot.SecondsLeft;
            }
            return Mathf.Max(0, slot.SecondsLeft - (int)(Time.realtimeSinceStartup - _receivedAt));
        }

        private ChestSlotDto Slot(int i) => _data != null && i < _data.Slots.Count ? _data.Slots[i] : null;

        /// <summary>Index of the first empty slot, or -1: where the free chest is shown.</summary>
        private int FirstEmptySlot()
        {
            for (int i = 0; i < 4; i++)
            {
                if (Slot(i) == null)
                {
                    return i;
                }
            }
            return -1;
        }

        private int FreeSecondsLeft() =>
            _data == null ? int.MaxValue : Mathf.Max(0, _data.FreeChestSecondsLeft - (int)(Time.realtimeSinceStartup - _receivedAt));

        private void Refresh()
        {
            Localization loc = Game.Loc;
            for (int i = 0; i < 4; i++)
            {
                ChestSlotDto slot = Slot(i);
                Image chest = _chests[i];
                Text label = _labels[i];
                if (slot == null)
                {
                    // The free chest lives on the first empty slot: ready it is a gift to tap, otherwise it shows the
                    // one countdown that does not need a win. Only the first, so it reads as a single appointment.
                    bool firstEmpty = FirstEmptySlot() == i;
                    if (firstEmpty && _data != null && FreeSecondsLeft() <= 0)
                    {
                        chest.enabled = true;
                        chest.sprite = ChestArt("wood");
                        label.text = loc.T("chest.free");
                        label.color = Theme.Success;
                        float beat = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f);
                        chest.transform.localScale = new Vector3(beat, beat, 1f);
                        continue;
                    }
                    if (firstEmpty && _data != null)
                    {
                        chest.enabled = true;
                        chest.sprite = ChestArt("wood");
                        chest.color = new Color(1f, 1f, 1f, 0.35f);
                        label.text = Duration(FreeSecondsLeft());
                        label.color = Theme.TextMuted;
                        chest.transform.localScale = Vector3.one;
                        continue;
                    }
                    chest.enabled = false;
                    chest.color = Color.white;
                    label.text = loc.T("chest.empty");
                    label.color = Theme.TextMuted;
                    chest.transform.localScale = Vector3.one;
                    continue;
                }
                chest.color = Color.white;
                chest.enabled = true;
                chest.sprite = ChestArt(slot.Type);
                int left = SecondsLeft(slot);
                bool ready = slot.Status == "Ready" || (slot.Status == "Unlocking" && left <= 0);
                if (ready)
                {
                    label.text = loc.T("chest.open");
                    label.color = Theme.Gold;
                    float k = 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 6f);
                    chest.transform.localScale = new Vector3(k, k, 1f);
                }
                else if (slot.Status == "Unlocking")
                {
                    label.text = Duration(left);
                    label.color = Theme.Crystal;
                    chest.transform.localScale = Vector3.one;
                }
                else
                {
                    label.text = Duration(slot.UnlockSeconds);
                    label.color = Theme.Text;
                    chest.transform.localScale = Vector3.one * 0.92f;
                }
            }
        }

        private static string Duration(int seconds)
        {
            if (seconds >= 3600)
            {
                return (seconds / 3600) + "h" + ((seconds % 3600) / 60).ToString("00");
            }
            return (seconds / 60) + ":" + (seconds % 60).ToString("00");
        }

        /// <summary>Takes the chest that filled on its own clock: no win, no payment, just a reason to come back.</summary>
        private async Task TakeFreeAsync()
        {
            _busy = true;
            try
            {
                ChestsDto chests = await Game.Backend.Client.Api.TakeFreeChestAsync();
                if (chests == null)
                {
                    return;
                }
                Store(chests);
                Apply(chests);
                Game.Audio.PlaySFX(SoundIds.Coins);
                _ui.Toast(Game.Loc.T("chest.freeTaken"), 3f);
            }
            finally
            {
                _busy = false;
            }
        }

        private async Task OnTapAsync(int index)
        {
            ChestSlotDto slot = Slot(index);
            if (_busy)
            {
                return;
            }
            if (slot == null)
            {
                if (index == FirstEmptySlot() && FreeSecondsLeft() <= 0 && Game.Backend.IsOnline)
                {
                    await TakeFreeAsync();
                    return;
                }
                // Empty slot: explain where timed chests come from.
                _ui.Toast(Game.Loc.T(index == FirstEmptySlot() ? "chest.freeSoon" : "chest.howTo"), 4f);
                return;
            }
            if (!Game.Backend.IsOnline)
            {
                _ui.Toast(Game.Loc.T("error.offline"));
                return;
            }
            Localization loc = Game.Loc;
            int left = SecondsLeft(slot);
            _busy = true;
            try
            {
                if (slot.Status == "Locked")
                {
                    bool anotherUnlocking = _data.Slots.Exists(s => s != null && s.Slot != index && s.Status == "Unlocking" && SecondsLeft(s) > 0);
                    if (anotherUnlocking)
                    {
                        _ui.Toast(loc.T("chest.oneAtATime"));
                        return;
                    }
                    ChestsDto updated = await Game.Backend.Client.Api.UnlockChestAsync(index);
                    Game.Audio.PlaySFX(SoundIds.Click);
                    Store(updated);
                    Apply(updated);
                    return;
                }
                bool ready = slot.Status == "Ready" || left <= 0;
                if (!ready)
                {
                    int cost = Mathf.Max(1, Mathf.CeilToInt(left / 600f));
                    if (!await _ui.Confirm(loc.T("chest.skipTitle"), loc.T("chest.skipBody", cost)))
                    {
                        return;
                    }
                }
                ChestOpenResponse opened = await Game.Backend.Client.Api.OpenChestAsync(index, !ready);
                ProfileDto profile = Game.Backend.Profile;
                if (profile != null)
                {
                    profile.Pets = opened.Pets ?? profile.Pets;
                }
                Store(opened.Chests);
                Game.Backend.ApplyInventory(opened.Inventory);
                Game.Backend.ApplyWallet(opened.Wallet);
                Apply(opened.Chests);
                Game.Telemetry.Track("chest_open", ("type", opened.Type), ("skipped", !ready));
                await ShowOpeningAsync(opened);
            }
            catch (CrushApiException ex)
            {
                _ui.ShowError(ex);
            }
            finally
            {
                _busy = false;
            }
        }

        private static void Store(ChestsDto chests)
        {
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null && chests != null)
            {
                profile.Chests = chests;
            }
        }

        /// <summary>The chest shakes, bursts open (opened chest art), then each reward flies out with its icon.</summary>
        private static Task ShowOpeningAsync(ChestOpenResponse opened)
        {
            Localization loc = Game.Loc;
            var items = RevealOverlay.FromReward(opened.Reward);
            if (opened.PetFragments > 0 && !string.IsNullOrEmpty(opened.FragmentsPet))
            {
                items.Add(new RevealItem
                {
                    Art = PetsScreen.PetArt(opened.FragmentsPet),
                    Caption = loc.T("pets.fragmentsGain", opened.PetFragments)
                });
            }
            string type = (opened.Type ?? "wood").ToLowerInvariant();
            return RevealOverlay.PlayChestAsync(items, ChestArt(opened.Type), UiKit.Art("chest_open_" + type) ?? ChestArt(opened.Type), loc.T("chest.openedTitle"));
        }
    }
}
