using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Task 12 shop UI: daily power-up offers (refreshable), orbe packs with transparent orbes-per-euro value,
    /// coins and lives, cosmetics, battle pass, remove ads, crown, VIP progress.
    /// </summary>
    public sealed class ShopScreen : UIScreen
    {
        private static readonly string[] TabKeys = { "shop.tab.powerups", "shop.tab.orbes", "shop.tab.coins", "shop.tab.cosmetics" };

        private int _tab;
        private ShopResponse _shop;
        private RectTransform _list;

        public override Type BackTarget => typeof(MainMenuScreen);

        private bool _argsApplied;

        protected override void Build()
        {
            if (!_argsApplied && Args is int tab)
            {
                _tab = Mathf.Clamp(tab, 0, TabKeys.Length - 1);
            }
            _argsApplied = true;
            RectTransform body = Frame("shop.title");
            CurrencyBar bar = CurrencyBar.Create(body);
            UIFactory.Anchor((RectTransform)bar.transform, 0.02f, 0.93f, 0.98f, 1f);

            RectTransform tabs = UIFactory.Anchor(UIFactory.Rect("Tabs", body), 0.02f, 0.85f, 0.98f, 0.925f);
            tabs.gameObject.AddComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            Action<int> setTab = Widgets.Tabs(tabs, TabKeys.Select(k => Loc.T(k)).ToList(), index =>
            {
                _tab = index;
                Rebuild();
            });
            setTab(_tab);

            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.845f);
            _list = UIFactory.ScrollList(holder, 18, 32);
            UIFactory.Stretch((RectTransform)_list.parent.parent);
            Fill();
        }

        public override async Task OnShownAsync()
        {
            if (_shop == null)
            {
                await ReloadAsync();
            }
        }

        private async Task ReloadAsync()
        {
            ShopResponse shop = await Api(api => api.GetShopAsync());
            if (shop == null || this == null)
            {
                return;
            }
            _shop = shop;
            Game.Backend.ApplyWallet(shop.Wallet);
            Rebuild();
        }

        private void Fill()
        {
            if (_shop == null)
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted), 120);
                return;
            }

            switch (_tab)
            {
                case 0:
                    FillPowerUps();
                    break;
                case 1:
                    FillOrbes();
                    break;
                case 2:
                    FillCoinsAndLives();
                    break;
                default:
                    FillCosmetics();
                    break;
            }
        }

        private void FillPowerUps()
        {
            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(_shop.NextRefreshUnixMs) - DateTimeOffset.UtcNow;
            InfoBar(_list, "item_hourglass", Loc.T("shop.refreshIn", (int)left.TotalHours, left.Minutes));

            Widgets.SectionTitle(_list, Loc.T("shop.dailyDeals"));
            List<ShopItemDto> daily = _shop.Items.Where(i => (i.Kind == "PowerUp" || i.Kind == "PowerUpBundle") && !i.Id.StartsWith("powerup.", StringComparison.Ordinal)).ToList();
            Grid(_list, daily, 2, 520, (cell, item) => PowerUpCard(cell, item, deal: item.IsDeal));
            UIFactory.Height(UIFactory.Button(_list, Loc.T("shop.refresh", _shop.RefreshCostOrbes), () => _ = RefreshAsync(), Theme.PanelLight, Theme.BodySize), 120);

            List<ShopItemDto> always = _shop.Items.Where(i => i.Kind == "PowerUp" && i.Id.StartsWith("powerup.", StringComparison.Ordinal)).ToList();
            if (always.Count > 0)
            {
                Widgets.SectionTitle(_list, Loc.T("shop.alwaysAvailable"));
                Grid(_list, always, 3, 430, (cell, item) => PowerUpCard(cell, item, deal: false));
            }
        }

        private void PowerUpCard(RectTransform cell, ShopItemDto item, bool deal)
        {
            Image card = UIFactory.Panel("Offer", cell, Theme.Panel);
            UIFactory.Stretch(card.rectTransform);
            UiKit.CardFrame(card);
            if (deal)
            {
                Outline glow = card.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                glow.effectDistance = new Vector2(6, -6);
            }
            bool compact = cell.rect.height < 450 && !deal;
            Image halo = UIFactory.Icon(card.transform, ProceduralSprites.Glow(128), new Color(Theme.Orbe.r, Theme.Orbe.g, Theme.Orbe.b, 0.4f), 0);
            UIFactory.Anchor(halo.rectTransform, 0.1f, 0.42f, 0.9f, 1f);
            Sprite art = Enum.TryParse(item.PowerUp, out CrushRoyale.Core.Config.PowerUpType type) ? ArtLibrary.PowerUp(type) : null;
            if (art != null)
            {
                Image icon = UIFactory.Icon(card.transform, art, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.2f, 0.5f, 0.8f, 0.95f);
                if (deal)
                {
                    icon.gameObject.AddComponent<Breathe>().Amount = 0.03f;
                }
            }
            if (item.Quantity > 1)
            {
                Text qty = UIFactory.Label(card.transform, "x" + item.Quantity, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.Anchor(qty.rectTransform, 0.5f, 0.5f, 0.94f, 0.66f);
                Widgets.TitleOutline(qty);
            }
            Text name = UIFactory.Label(card.transform, Loc.T("powerup." + item.PowerUp), Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.05f, 0.37f, 0.95f, 0.5f);
            Widgets.TitleOutline(name);
            if (!compact)
            {
                Text desc = UIFactory.Label(card.transform, Loc.T("powerup." + item.PowerUp + ".desc"), Theme.SmallSize - 8, Theme.TextMuted, TextAnchor.UpperCenter);
                UIFactory.Anchor(desc.rectTransform, 0.06f, 0.25f, 0.94f, 0.37f);
            }
            if (item.DiscountPermille > 0)
            {
                Badge(card.transform, "-" + item.DiscountPermille / 10 + "%", Theme.Danger, 0.62f, 0.86f, 1.04f, 1.02f);
            }
            if (deal)
            {
                Badge(card.transform, Loc.T("shop.bestDeal"), Theme.Gold, -0.04f, 0.86f, 0.5f, 1.02f);
            }

            float bottom = compact ? 0.26f : 0.23f;
            if (item.SoldOut)
            {
                Text sold = UIFactory.Label(card.transform, Loc.T("shop.soldOut"), Theme.BodySize, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(sold.rectTransform, 0.05f, 0.04f, 0.95f, bottom);
                card.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                return;
            }
            bool both = item.PriceCoins > 0 && item.PriceOrbes > 0;
            if (item.PriceCoins > 0)
            {
                PriceButton(card.transform, "item_coins", Loc.Number(item.PriceCoins), Theme.Gold, () => _ = BuyAsync(item, "Coins"), 0.05f, 0.04f, both ? 0.49f : 0.95f, bottom);
            }
            if (item.PriceOrbes > 0)
            {
                PriceButton(card.transform, "item_orbs", Loc.Number(item.PriceOrbes), Theme.Orbe, () => _ = BuyAsync(item, "Orbes"), both ? 0.51f : 0.05f, 0.04f, 0.95f, bottom);
            }
        }

        private void FillOrbes()
        {
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null)
            {
                Image vip = UIFactory.Panel("Vip", _list, Theme.Panel);
                UIFactory.Height(vip, 220);
                UiKit.FramePanel(vip);
                Sprite crown = UiKit.Art("item_crown");
                if (crown != null)
                {
                    Image icon = UIFactory.Icon(vip.transform, crown, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, 0.04f, 0.15f, 0.2f, 0.85f);
                }
                Text tier = UIFactory.Label(vip.transform, Loc.T("shop.vip", profile.Vip.Tier), Theme.HeaderSize - 4, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(tier.rectTransform, 0.22f, 0.55f, 0.66f, 0.88f);
                Widgets.TitleOutline(tier);
                UIFactory.ProgressBar(vip.transform, profile.Vip.NextThresholdCents.HasValue ? profile.Vip.Progress : 1f, Theme.Gold, out RectTransform bar);
                UIFactory.Anchor(bar, 0.22f, 0.18f, 0.66f, 0.46f);
                Button details = UIFactory.Button(vip.transform, Loc.T("vip.details"), () => UI.Show<VipScreen>(), Theme.PanelLight, Theme.SmallSize);
                UIFactory.Anchor(details.GetComponent<RectTransform>(), 0.69f, 0.22f, 0.96f, 0.78f);
            }
            InfoBar(_list, "item_orbs", Loc.T("shop.transparency"));

            List<ShopItemDto> packs = _shop.Items.Where(i => i.Kind == "OrbePack").ToList();
            decimal best = packs.Count > 0 ? packs.Max(p => p.OrbesPerEuro) : 0;
            string popular = packs.Count > 2 ? packs[packs.Count / 2].Id : null;
            Grid(_list, packs, 2, 470, (cell, item) =>
            {
                Image card = UIFactory.Panel("Pack", cell, Theme.Panel);
                UIFactory.Stretch(card.rectTransform);
                UiKit.CardFrame(card);
                int index = packs.IndexOf(item);
                Image halo = UIFactory.Icon(card.transform, ProceduralSprites.Glow(128), new Color(Theme.Orbe.r, Theme.Orbe.g, Theme.Orbe.b, 0.3f + 0.08f * index), 0);
                UIFactory.Anchor(halo.rectTransform, 0.05f, 0.35f, 0.95f, 1f);
                Sprite art = UiKit.Art(index >= packs.Count - 2 ? "item_pouch" : "item_orbs") ?? ArtLibrary.Icon("orb");
                if (art != null)
                {
                    float grow = Mathf.Min(0.12f, index * 0.025f);
                    Image icon = UIFactory.Icon(card.transform, art, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, 0.24f - grow, 0.46f - grow * 0.5f, 0.76f + grow, 0.9f + grow * 0.3f);
                }
                Text amount = UIFactory.Label(card.transform, Loc.Number(item.OrbesGranted), Theme.HeaderSize + 4, Theme.Orbe, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(amount.rectTransform, 0.05f, 0.32f, 0.95f, 0.47f);
                Widgets.TitleOutline(amount);
                Text perEuro = UIFactory.Label(card.transform, Loc.T("shop.perEuro", item.OrbesPerEuro.ToString("0.0")), Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.MiddleCenter);
                UIFactory.Anchor(perEuro.rectTransform, 0.05f, 0.23f, 0.95f, 0.32f);
                if (item.OrbesPerEuro == best && packs.Count > 1)
                {
                    Badge(card.transform, Loc.T("shop.bestValue"), Theme.Success, -0.04f, 0.86f, 0.62f, 1.02f);
                }
                else if (item.Id == popular)
                {
                    Badge(card.transform, Loc.T("shop.popular"), Theme.Danger, -0.04f, 0.86f, 0.62f, 1.02f);
                }
                string price = Game.Iap.LocalizedPrice(item.Sku) ?? (item.PriceCents / 100m).ToString("0.00") + " €";
                Button buy = UIFactory.Button(card.transform, price, () => _ = BuyRealMoneyAsync(item.Sku), Theme.Success, Theme.BodySize);
                UIFactory.Anchor(buy.GetComponent<RectTransform>(), 0.08f, 0.04f, 0.92f, 0.21f);
            });
        }

        private void FillCoinsAndLives()
        {
            List<ShopItemDto> coins = _shop.Items.Where(i => i.Kind == "CoinPack").ToList();
            if (coins.Count > 0)
            {
                Widgets.SectionTitle(_list, Loc.T("shop.coinsSection"));
                Grid(_list, coins, 2, 420, (cell, item) =>
                {
                    int index = coins.IndexOf(item);
                    Image card = CardIn(cell);
                    Icon(card.transform, "item_coins", 0.2f - index * 0.03f, 0.45f, 0.8f + index * 0.03f, 0.92f);
                    Amount(card.transform, Loc.Number(item.CoinsGranted), Theme.Gold);
                    if (index == coins.Count - 1 && coins.Count > 1)
                    {
                        Badge(card.transform, Loc.T("shop.bestValue"), Theme.Success, -0.04f, 0.86f, 0.62f, 1.02f);
                    }
                    PriceButton(card.transform, "item_orbs", Loc.Number(item.PriceOrbes), Theme.Orbe, () => _ = BuyAsync(item, "Orbes"), 0.08f, 0.04f, 0.92f, 0.24f);
                });
            }

            List<ShopItemDto> lives = _shop.Items.Where(i => i.Kind == "LivesPack").ToList();
            if (lives.Count > 0)
            {
                Widgets.SectionTitle(_list, Loc.T("shop.livesSection"));
                Grid(_list, lives, 2, 420, (cell, item) =>
                {
                    Image card = CardIn(cell);
                    Icon(card.transform, "item_heart", 0.25f, 0.45f, 0.75f, 0.9f);
                    Amount(card.transform, "+" + item.Quantity, Theme.Danger);
                    if (item.IsDeal)
                    {
                        Badge(card.transform, Loc.T("shop.bestDeal"), Theme.Gold, -0.04f, 0.86f, 0.62f, 1.02f);
                    }
                    bool both = item.PriceCoins > 0 && item.PriceOrbes > 0;
                    if (item.PriceCoins > 0)
                    {
                        PriceButton(card.transform, "item_coins", Loc.Number(item.PriceCoins), Theme.Gold, () => _ = BuyAsync(item, "Coins"), 0.05f, 0.04f, both ? 0.49f : 0.95f, 0.24f);
                    }
                    if (item.PriceOrbes > 0)
                    {
                        PriceButton(card.transform, "item_orbs", Loc.Number(item.PriceOrbes), Theme.Orbe, () => _ = BuyAsync(item, "Orbes"), both ? 0.51f : 0.05f, 0.04f, 0.95f, 0.24f);
                    }
                });
            }
        }

        private void FillCosmetics()
        {
            foreach (ShopItemDto item in _shop.Items.Where(i => i.Kind == "BattlePass" || i.Kind == "RemoveAds" || i.Kind == "RarePerk"))
            {
                Image card = UIFactory.Panel("Special", _list, Theme.Panel);
                UIFactory.Height(card, 300);
                UiKit.FramePanel(card);
                string icon = item.Kind == "BattlePass" ? "item_ticket" : item.Kind == "RarePerk" ? "item_crown" : "item_lock";
                Icon(card.transform, icon, 0.04f, 0.2f, 0.26f, 0.8f);
                Text title = UIFactory.Label(card.transform, Loc.T("shop.item." + item.Kind), Theme.HeaderSize - 8, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(title.rectTransform, 0.28f, 0.66f, 0.96f, 0.9f);
                Widgets.TitleOutline(title);
                Text desc = UIFactory.Label(card.transform, Loc.T("shop.item." + item.Kind + ".desc"), Theme.SmallSize - 4, Theme.Text, TextAnchor.UpperLeft);
                UIFactory.Anchor(desc.rectTransform, 0.28f, 0.36f, 0.96f, 0.66f);
                bool both = item.PriceOrbes > 0 && item.PriceCents > 0;
                if (item.PriceOrbes > 0)
                {
                    PriceButton(card.transform, "item_orbs", Loc.Number(item.PriceOrbes), Theme.Orbe, () => _ = BuyAsync(item, "Orbes"), 0.28f, 0.08f, both ? 0.61f : 0.95f, 0.32f);
                }
                if (item.PriceCents > 0)
                {
                    string price = Game.Iap.LocalizedPrice(item.Sku) ?? (item.PriceCents / 100m).ToString("0.00") + " €";
                    Button buy = UIFactory.Button(card.transform, price, () => _ = BuyRealMoneyAsync(item.Sku), Theme.Success, Theme.BodySize);
                    UIFactory.Anchor(buy.GetComponent<RectTransform>(), both ? 0.63f : 0.28f, 0.08f, 0.95f, 0.32f);
                }
            }

            Widgets.SectionTitle(_list, Loc.T("shop.cosmetics"));
            List<ShopItemDto> cosmetics = _shop.Items.Where(i => i.Kind == "Cosmetic").ToList();
            Grid(_list, cosmetics, 2, 330, (cell, item) =>
            {
                Image card = CardIn(cell);
                Icon(card.transform, "item_gift", 0.3f, 0.5f, 0.7f, 0.92f);
                Text name = UIFactory.Label(card.transform, Loc.T("cosmetic." + item.CosmeticId), Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, 0.05f, 0.3f, 0.95f, 0.5f);
                Widgets.TitleOutline(name);
                if (item.SoldOut && item.RequiredVip > 0)
                {
                    Badge(card.transform, Loc.T("shop.requiresVip", item.RequiredVip), Theme.GoldDark, 0.1f, 0.05f, 0.9f, 0.27f);
                }
                else
                {
                    PriceButton(card.transform, "item_orbs", Loc.Number(item.PriceOrbes), Theme.Orbe, () => _ = BuyAsync(item, "Orbes"), 0.1f, 0.04f, 0.9f, 0.28f);
                }
            });

            ProfileDto profile = Game.Backend.Profile;
            if (profile == null)
            {
                return;
            }
            Widgets.SectionTitle(_list, Loc.T("shop.owned"));
            foreach (string cosmetic in profile.Inventory.Cosmetics)
            {
                bool equipped = cosmetic == profile.Inventory.EquippedFrame || cosmetic == profile.Inventory.EquippedBoardSkin
                    || cosmetic == profile.Inventory.EquippedPieceSkin || cosmetic == profile.Inventory.EquippedTitle || cosmetic == profile.Inventory.EquippedOutfit;
                Image row = UIFactory.Panel("Owned", _list, Theme.Panel);
                UIFactory.Height(row, 120);
                UiKit.CardFrame(row);
                Text label = UIFactory.Label(row.transform, (equipped ? "✔ " : string.Empty) + Loc.T("cosmetic." + cosmetic), Theme.BodySize - 4, equipped ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(label.rectTransform, 0.05f, 0.1f, 0.68f, 0.9f);
                if (!equipped && !cosmetic.StartsWith("emote.", StringComparison.Ordinal))
                {
                    string id = cosmetic;
                    Button equip = UIFactory.Button(row.transform, Loc.T("shop.equip"), () => _ = EquipAsync(id), Theme.Gold, Theme.SmallSize);
                    UIFactory.Anchor(equip.GetComponent<RectTransform>(), 0.7f, 0.15f, 0.96f, 0.85f);
                }
            }
        }

        private static void Grid(Transform list, List<ShopItemDto> items, int columns, float cellHeight, Action<RectTransform, ShopItemDto> fill)
        {
            if (items.Count == 0)
            {
                return;
            }
            RectTransform gridRect = UIFactory.Rect("Grid", list);
            int rows = (items.Count + columns - 1) / columns;
            UIFactory.Height(gridRect, rows * cellHeight + (rows - 1) * 22);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            float width = (1080f - 64f - (columns - 1) * 22f) / columns;
            grid.cellSize = new Vector2(width, cellHeight);
            grid.spacing = new Vector2(22, 22);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            foreach (ShopItemDto item in items)
            {
                RectTransform cell = UIFactory.Rect("Cell", gridRect);
                cell.sizeDelta = new Vector2(width, cellHeight);
                fill(cell, item);
            }
        }

        private static Image CardIn(RectTransform cell)
        {
            Image card = UIFactory.Panel("Card", cell, Theme.Panel);
            UIFactory.Stretch(card.rectTransform);
            UiKit.CardFrame(card);
            return card;
        }

        private static void Icon(Transform parent, string name, float minX, float minY, float maxX, float maxY)
        {
            Sprite art = UiKit.Art(name);
            if (art != null)
            {
                Image icon = UIFactory.Icon(parent, art, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, minX, minY, maxX, maxY);
            }
        }

        private static void Amount(Transform parent, string text, Color color)
        {
            Text amount = UIFactory.Label(parent, text, Theme.HeaderSize + 4, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(amount.rectTransform, 0.05f, 0.27f, 0.95f, 0.45f);
            Widgets.TitleOutline(amount);
        }

        private static void Badge(Transform parent, string text, Color color, float minX, float minY, float maxX, float maxY)
        {
            Button badge = UIFactory.Button(parent, text, null, color, Theme.SmallSize - 4);
            badge.interactable = false;
            badge.GetComponent<Image>().raycastTarget = false;
            RectTransform rect = UIFactory.Anchor(badge.GetComponent<RectTransform>(), minX, minY, maxX, maxY);
            rect.localEulerAngles = new Vector3(0, 0, minX < 0.3f ? 4f : -4f);
            ColorBlock colors = badge.colors;
            colors.disabledColor = Color.white;
            badge.colors = colors;
        }

        private static void PriceButton(Transform parent, string icon, string price, Color color, Action onClick, float minX, float minY, float maxX, float maxY)
        {
            Button button = UIFactory.Button(parent, price, onClick, color, Theme.BodySize - 2);
            RectTransform rect = UIFactory.Anchor(button.GetComponent<RectTransform>(), minX, minY, maxX, maxY);
            Sprite art = UiKit.Art(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(rect, art, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.03f, 0.1f, 0.3f, 0.9f);
                Text label = button.GetComponentInChildren<Text>();
                UIFactory.Anchor(label.rectTransform, 0.28f, 0.08f, 0.96f, 0.92f);
            }
        }

        private static void InfoBar(Transform list, string icon, string text)
        {
            Image bar = UIFactory.Panel("Info", list, Theme.Panel);
            UIFactory.Height(bar, 110);
            UiKit.CardFrame(bar);
            Icon(bar.transform, icon, 0.03f, 0.1f, 0.11f, 0.9f);
            Text label = UIFactory.Label(bar.transform, text, Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.13f, 0.05f, 0.97f, 0.95f);
        }

        private async Task BuyAsync(ShopItemDto item, string method)
        {
            PurchaseResponse response = await Api(api => api.PurchaseAsync(item.Id, method));
            if (response == null)
            {
                return;
            }
            Game.Backend.ApplyWallet(response.Wallet);
            Game.Backend.ApplyLives(response.Lives);
            Game.Audio.PlaySFX(SoundIds.Coins);
            UI.Toast(Loc.T("shop.bought") + "\n" + MainMenuScreen.RewardText(Loc, response.Granted));
            await Game.Backend.RefreshProfileAsync();
            await ReloadAsync();
        }

        private async Task BuyRealMoneyAsync(string sku)
        {
            try
            {
                IapValidateResponse granted;
                using (UI.Loading())
                {
                    granted = await Game.Iap.BuyAsync(sku);
                }
                Game.Audio.PlaySFX(SoundIds.Coins);
                UI.Toast(granted.AlreadyGranted ? Loc.T("shop.alreadyGranted") : Loc.T("shop.bought") + "\n" + MainMenuScreen.RewardText(Loc, granted.Reward));
                await Game.Backend.RefreshProfileAsync();
                await ReloadAsync();
            }
            catch (CrushApiException ex) when (ex.Code == "Cancelled")
            {
                // Player closed the Google Play sheet.
            }
            catch (CrushApiException ex)
            {
                UI.ShowError(ex);
            }
        }

        private async Task RefreshAsync()
        {
            if (!await UI.Confirm(Loc.T("shop.refreshTitle"), Loc.T("shop.refresh", _shop.RefreshCostOrbes)))
            {
                return;
            }
            ShopResponse shop = await Api(api => api.RefreshShopAsync());
            if (shop != null)
            {
                _shop = shop;
                Game.Backend.ApplyWallet(shop.Wallet);
                Rebuild();
            }
        }

        private async Task EquipAsync(string cosmetic)
        {
            InventoryDto inventory = await Api(api => api.EquipAsync(cosmetic));
            if (inventory != null)
            {
                Game.Backend.ApplyInventory(inventory);
                Rebuild();
            }
        }
    }
}
