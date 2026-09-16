using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.UI
{
    /// <summary>
    /// Pre-match boost selection shared by story stages and ranked PvP: illustrated cards with the owned count,
    /// selected slots on top, lock reasons, and a "Get" button that buys the boost or shows where to earn it.
    /// </summary>
    public sealed class LoadoutPicker
    {
        private readonly HashSet<string> _selected;
        private readonly bool _pvp;
        private readonly Action _rebuild;
        private readonly List<Image> _slotIcons = new List<Image>();
        private readonly Dictionary<string, (Image Frame, GameObject Check)> _cards = new Dictionary<string, (Image, GameObject)>();
        private Text _slotsTitle;

        private LoadoutPicker(HashSet<string> selected, bool pvp, Action rebuild)
        {
            _selected = selected;
            _pvp = pvp;
            _rebuild = rebuild;
        }

        private static GameRoot Game => GameRoot.Instance;

        private static Localization Loc => GameRoot.Instance.Loc;

        /// <summary>Adds the slot bar and the boost grid to a vertical list. <paramref name="rebuild"/> refreshes the screen after a purchase.</summary>
        public static void Build(Transform list, HashSet<string> selected, bool pvp, Action rebuild)
        {
            var picker = new LoadoutPicker(selected, pvp, rebuild);
            picker.BuildSlots(list);
            picker.BuildGrid(list);
            picker.Refresh();
        }

        private void BuildSlots(Transform list)
        {
            int slots = Game.Backend.Balance.PowerUps.LoadoutSlots;
            Image panel = UIFactory.Panel("Slots", list, Theme.Panel);
            UIFactory.Height(panel, 230);
            UiKit.CardFrame(panel);

            _slotsTitle = UIFactory.Label(panel.transform, string.Empty, Theme.BodySize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_slotsTitle.rectTransform, 0.04f, 0.68f, 0.96f, 0.95f);
            Widgets.TitleOutline(_slotsTitle);

            RectTransform row = UIFactory.Anchor(UIFactory.Rect("Row", panel.transform), 0.1f, 0.08f, 0.9f, 0.68f);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 40;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            for (int i = 0; i < slots; i++)
            {
                RectTransform slot = UIFactory.Rect("Slot", row);
                UIFactory.Width(slot.gameObject.AddComponent<LayoutElement>(), 130);
                if (UiKit.RoundBadge(slot, crystal: true) == null)
                {
                    UIFactory.Stretch(UIFactory.Panel("Back", slot, Theme.BackgroundLight).rectTransform);
                }
                Image icon = UIFactory.Icon(slot, null, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.14f, 0.14f, 0.86f, 0.86f);
                _slotIcons.Add(icon);
            }
        }

        private void BuildGrid(Transform list)
        {
            ProfileDto profile = Game.Backend.Profile;
            GameBalance balance = Game.Backend.Balance;
            League highest = Enum.TryParse(profile.Pvp.HighestLeague, out League parsed) ? parsed : League.Bronze;

            RectTransform gridRect = UIFactory.Rect("Loadout", list);
            List<PowerUpDefinition> defs = balance.PowerUps.Definitions.Where(d => _pvp || !d.PvpOnly).ToList();
            int rows = (defs.Count + 2) / 3;
            UIFactory.Height(gridRect, rows * 330 + (rows - 1) * 18);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(312, 330);
            grid.spacing = new Vector2(18, 18);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.childAlignment = TextAnchor.UpperCenter;

            foreach (PowerUpDefinition def in defs)
            {
                string type = def.Type.ToString();
                int count = profile.Inventory.PowerUps.TryGetValue(type, out int n) ? n : 0;
                bool leagueLocked = highest < def.UnlockLeague;
                Card(gridRect, def, count, leagueLocked);
            }
        }

        private void Card(Transform grid, PowerUpDefinition def, int count, bool leagueLocked)
        {
            string type = def.Type.ToString();
            Image frame = UIFactory.Panel("Boost", grid, Theme.Panel);
            UiKit.CardFrame(frame);
            Button button = frame.gameObject.AddComponent<Button>();
            button.targetGraphic = frame;
            frame.gameObject.AddComponent<ButtonFeedback>();

            Sprite art = ArtLibrary.PowerUp(def.Type);
            if (art != null)
            {
                Image icon = UIFactory.Icon(frame.transform, art, leagueLocked || count == 0 ? new Color(0.45f, 0.45f, 0.5f, 1f) : Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.18f, 0.4f, 0.82f, 0.92f);
            }
            Text name = UIFactory.Label(frame.transform, Loc.T("powerup." + type), Theme.SmallSize - 2, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.05f, 0.24f, 0.95f, 0.42f);
            Widgets.TitleOutline(name);

            // Owned count in a gold coin at the top-right corner.
            RectTransform badge = UIFactory.Anchor(UIFactory.Rect("Count", frame.transform), 0.7f, 0.76f, 0.98f, 1.02f);
            badge.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            if (UiKit.RoundBadge(badge, crystal: false) == null)
            {
                UIFactory.Stretch(UIFactory.Panel("Back", badge, Theme.GoldDark).rectTransform);
            }
            Text countText = UIFactory.Label(badge, "x" + count, Theme.SmallSize, count > 0 ? Theme.Text : Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(countText.rectTransform, 6, 6, 6, 6);
            Widgets.TitleOutline(countText);

            Text check = UIFactory.Label(frame.transform, "✔", Theme.HeaderSize, Theme.Success, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Anchor(check.rectTransform, 0.06f, 0.72f, 0.4f, 0.98f);
            Widgets.TitleOutline(check);
            check.gameObject.SetActive(false);
            _cards[type] = (frame, check.gameObject);

            if (leagueLocked)
            {
                Sprite lockArt = UiKit.Art("item_lock") ?? ArtLibrary.Icon("lock");
                if (lockArt != null)
                {
                    Image lockIcon = UIFactory.Icon(frame.transform, lockArt, Color.white, 0);
                    UIFactory.Anchor(lockIcon.rectTransform, 0.36f, 0.5f, 0.64f, 0.82f);
                }
                Text league = UIFactory.Label(frame.transform, Loc.T("stage.boostLeague", Loc.T("league." + def.UnlockLeague)), Theme.SmallSize - 6, Theme.Warning, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(league.rectTransform, 0.06f, 0.05f, 0.94f, 0.24f);
                button.onClick.AddListener(() => UI.Toast(Loc.T("stage.boostLeagueToast", Loc.T("league." + def.UnlockLeague))));
                return;
            }

            if (count == 0)
            {
                Button get = UIFactory.Button(frame.transform, Loc.T("stage.get"), () => _ = GetItemPopup.ShowAsync(def.Type, _rebuild), Theme.Success, Theme.SmallSize - 2);
                UIFactory.Anchor(get.GetComponent<RectTransform>(), 0.1f, 0.04f, 0.9f, 0.24f);
                button.onClick.AddListener(() => _ = GetItemPopup.ShowAsync(def.Type, _rebuild));
                return;
            }

            Text hint = UIFactory.Label(frame.transform, Loc.T("stage.tapToEquip"), Theme.SmallSize - 8, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(hint.rectTransform, 0.06f, 0.06f, 0.94f, 0.24f);
            button.onClick.AddListener(() => Toggle(type));
        }

        private static UIRoot UI => GameRoot.Instance.UI;

        private void Toggle(string type)
        {
            int slots = Game.Backend.Balance.PowerUps.LoadoutSlots;
            if (!_selected.Remove(type))
            {
                if (_selected.Count >= slots)
                {
                    UI.Toast(Loc.T("stage.slotsFull", slots));
                    return;
                }
                _selected.Add(type);
                Game.Audio.PlaySFX(SoundIds.Click);
            }
            Refresh();
        }

        private void Refresh()
        {
            int slots = Game.Backend.Balance.PowerUps.LoadoutSlots;
            _slotsTitle.text = Loc.T("stage.slots", _selected.Count, slots);
            List<string> chosen = _selected.ToList();
            for (int i = 0; i < _slotIcons.Count; i++)
            {
                Sprite art = i < chosen.Count && Enum.TryParse(chosen[i], out PowerUpType t) ? ArtLibrary.PowerUp(t) : null;
                _slotIcons[i].sprite = art;
                _slotIcons[i].enabled = art != null;
            }
            foreach (KeyValuePair<string, (Image Frame, GameObject Check)> card in _cards)
            {
                bool on = _selected.Contains(card.Key);
                card.Value.Check.SetActive(on);
                card.Value.Frame.transform.localScale = on ? Vector3.one * 1.04f : Vector3.one;
                Outline glow = card.Value.Frame.GetComponent<Outline>();
                if (on && glow == null)
                {
                    glow = card.Value.Frame.gameObject.AddComponent<Outline>();
                    glow.effectColor = new Color(1f, 0.85f, 0.3f, 0.9f);
                    glow.effectDistance = new Vector2(6, -6);
                }
                if (glow != null)
                {
                    glow.enabled = on;
                }
            }
        }
    }

    /// <summary>"How do I get this?" window: buy one now with coins or orbes, or jump to the places that give it for free.</summary>
    public static class GetItemPopup
    {
        public static async Task ShowAsync(PowerUpType type, Action bought)
        {
            GameRoot game = GameRoot.Instance;
            Localization loc = game.Loc;
            UIRoot ui = game.UI;
            RectTransform box = ui.Popup(0.2f, 0.8f);

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", box), -0.03f, 0.88f, 1.03f, 1.03f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, loc.T("powerup." + type), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
            Widgets.TitleOutline(title);

            Sprite art = ArtLibrary.PowerUp(type);
            if (art != null)
            {
                Image icon = UIFactory.Icon(box, art, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.06f, 0.7f, 0.32f, 0.87f);
                icon.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }
            Text desc = UIFactory.Label(box, loc.T("powerup." + type + ".desc"), Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Anchor(desc.rectTransform, 0.34f, 0.7f, 0.94f, 0.87f);

            void Close()
            {
                if (box != null)
                {
                    UnityEngine.Object.Destroy(box.parent.gameObject);
                }
            }

            // Buy now (always-available single items).
            Text buyTitle = UIFactory.Label(box, loc.T("get.buyNow"), Theme.BodySize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(buyTitle.rectTransform, 0.07f, 0.63f, 0.93f, 0.69f);
            RectTransform buyRow = UIFactory.Anchor(UIFactory.Rect("Buy", box), 0.07f, 0.53f, 0.93f, 0.625f);
            HorizontalLayoutGroup buyLayout = buyRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            buyLayout.spacing = 20;
            buyLayout.childControlWidth = buyLayout.childControlHeight = true;
            buyLayout.childForceExpandWidth = buyLayout.childForceExpandHeight = true;
            Text pending = UIFactory.Label(buyRow, loc.T("common.loading"), Theme.SmallSize, Theme.TextMuted);

            // Free sources.
            Text freeTitle = UIFactory.Label(box, loc.T("get.free"), Theme.BodySize, Theme.Crystal, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(freeTitle.rectTransform, 0.07f, 0.46f, 0.93f, 0.52f);
            RectTransform sources = UIFactory.Anchor(UIFactory.Rect("Sources", box), 0.07f, 0.13f, 0.93f, 0.455f);
            VerticalLayoutGroup sourceLayout = sources.gameObject.AddComponent<VerticalLayoutGroup>();
            sourceLayout.spacing = 12;
            sourceLayout.childControlWidth = sourceLayout.childControlHeight = true;
            sourceLayout.childForceExpandWidth = sourceLayout.childForceExpandHeight = true;
            Source(sources, "item_gift", loc.T("get.sourceChests"), () => { Close(); ui.ShowRoot<MainMenuScreen>(); });
            Source(sources, "item_medal", loc.T("get.sourceQuests"), () => { Close(); ui.Show<QuestsScreen>(); });
            Source(sources, "item_ticket", loc.T("get.sourcePass"), () => { Close(); ui.Show<BattlePassScreen>(); });
            Source(sources, "item_trophy", loc.T("get.sourceAchievements"), () => { Close(); ui.Show<AchievementsScreen>(); });

            Button close = UIFactory.Button(box, loc.T("common.close"), Close, Theme.PanelLight, Theme.BodySize);
            UIFactory.Anchor(close.GetComponent<RectTransform>(), 0.3f, 0.025f, 0.7f, 0.11f);

            if (!game.Backend.IsOnline)
            {
                pending.text = loc.T("error.offline");
                return;
            }
            ShopResponse shop;
            try
            {
                shop = await game.Backend.Client.Api.GetShopAsync();
            }
            catch (CrushApiException ex)
            {
                if (pending != null)
                {
                    pending.text = loc.T("error." + ex.Code);
                }
                return;
            }
            if (box == null)
            {
                return;
            }
            UnityEngine.Object.Destroy(pending.gameObject);
            ShopItemDto item = shop.Items.FirstOrDefault(i => i.Id == "powerup." + type);
            if (item == null)
            {
                UIFactory.Label(buyRow, loc.T("get.notInShop"), Theme.SmallSize, Theme.TextMuted);
                return;
            }
            if (item.PriceCoins > 0)
            {
                UIFactory.Button(buyRow, loc.T("currency.coins", loc.Number(item.PriceCoins)), () => _ = BuyAsync(item, "Coins", Close, bought), Theme.Gold, Theme.BodySize);
            }
            if (item.PriceOrbes > 0)
            {
                UIFactory.Button(buyRow, loc.T("currency.orbes", loc.Number(item.PriceOrbes)), () => _ = BuyAsync(item, "Orbes", Close, bought), Theme.Orbe, Theme.BodySize);
            }
        }

        private static void Source(Transform parent, string icon, string text, Action go)
        {
            Image row = UIFactory.Panel("Source", parent, Theme.Panel);
            UiKit.CardFrame(row);
            Sprite art = UiKit.Art(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(row.transform, art, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.02f, 0.1f, 0.14f, 0.9f);
            }
            Text label = UIFactory.Label(row.transform, text, Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.16f, 0.05f, 0.66f, 0.95f);
            Button button = UIFactory.Button(row.transform, GameRoot.Instance.Loc.T("get.go"), go, Theme.Success, Theme.SmallSize);
            UIFactory.Anchor(button.GetComponent<RectTransform>(), 0.68f, 0.14f, 0.97f, 0.86f);
        }

        private static async Task BuyAsync(ShopItemDto item, string method, Action close, Action bought)
        {
            GameRoot game = GameRoot.Instance;
            PurchaseResponse response;
            try
            {
                using (game.UI.Loading())
                {
                    response = await game.Backend.Client.Api.PurchaseAsync(item.Id, method);
                }
            }
            catch (CrushApiException ex)
            {
                game.UI.ShowError(ex);
                return;
            }
            game.Backend.ApplyWallet(response.Wallet);
            game.Audio.PlaySFX(SoundIds.Coins);
            game.UI.Toast(game.Loc.T("shop.bought") + "\n" + MainMenuScreen.RewardText(game.Loc, response.Granted));
            await game.Backend.RefreshProfileAsync();
            close();
            bought?.Invoke();
        }
    }
}
