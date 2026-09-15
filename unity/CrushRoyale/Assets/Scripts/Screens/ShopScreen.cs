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

        protected override void Build()
        {
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
            UIFactory.Height(UIFactory.Label(_list, Loc.T("shop.refreshIn", (int)left.TotalHours, left.Minutes), Theme.SmallSize, Theme.TextMuted), 60);

            foreach (ShopItemDto item in _shop.Items.Where(i => i.Kind == "PowerUp" || i.Kind == "PowerUpBundle"))
            {
                RectTransform card = Widgets.Card(_list, 250, item.IsDeal ? Theme.GoldDark : Theme.Panel);
                string title = Loc.T("powerup." + item.PowerUp) + " x" + item.Quantity + (item.DiscountPermille > 0 ? "  -" + item.DiscountPermille / 10 + "%" : string.Empty);
                UIFactory.Label(card, title, Theme.HeaderSize - 6, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("powerup." + item.PowerUp + ".desc"), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);

                HorizontalLayoutGroup row = UIFactory.Row(card, 90, 16);
                if (item.SoldOut)
                {
                    UIFactory.Label(row.transform, Loc.T("shop.soldOut"), Theme.BodySize, Theme.TextMuted);
                    continue;
                }
                if (item.PriceCoins > 0)
                {
                    UIFactory.Button(row.transform, Loc.T("currency.coins", item.PriceCoins), () => _ = BuyAsync(item, "Coins"), Theme.Gold, Theme.BodySize);
                }
                if (item.PriceOrbes > 0)
                {
                    UIFactory.Button(row.transform, Loc.T("currency.orbes", item.PriceOrbes), () => _ = BuyAsync(item, "Orbes"), Theme.Orbe, Theme.BodySize, Theme.Background);
                }
            }

            UIFactory.Height(UIFactory.Button(_list, Loc.T("shop.refresh", _shop.RefreshCostOrbes), () => _ = RefreshAsync(), Theme.PanelLight, Theme.BodySize, Theme.Text), 120);
        }

        private void FillOrbes()
        {
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null)
            {
                RectTransform vip = Widgets.Card(_list, 200, Theme.PanelLight);
                UIFactory.Label(vip, Loc.T("shop.vip", profile.Vip.Tier), Theme.HeaderSize - 6, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(vip, profile.Vip.NextThresholdCents.HasValue
                    ? Loc.T("shop.vipNext", Mathf.RoundToInt(profile.Vip.Progress * 100))
                    : Loc.T("shop.vipMax"), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
            }
            UIFactory.Height(UIFactory.Label(_list, Loc.T("shop.transparency"), Theme.SmallSize, Theme.TextMuted), 110);

            foreach (ShopItemDto item in _shop.Items.Where(i => i.Kind == "OrbePack"))
            {
                RectTransform card = Widgets.Card(_list, 230);
                UIFactory.Label(card, Loc.T("currency.orbes", Loc.Number(item.OrbesGranted)), Theme.HeaderSize, Theme.Orbe, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("shop.perEuro", item.OrbesPerEuro.ToString("0.0")), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                string price = Game.Iap.LocalizedPrice(item.Sku) ?? (item.PriceCents / 100m).ToString("0.00") + " EUR";
                UIFactory.Button(card, price, () => _ = BuyRealMoneyAsync(item.Sku), Theme.Gold, Theme.BodySize);
            }
        }

        private void FillCoinsAndLives()
        {
            foreach (ShopItemDto item in _shop.Items.Where(i => i.Kind == "CoinPack" || i.Kind == "LivesPack"))
            {
                RectTransform card = Widgets.Card(_list, 200);
                string title = item.Kind == "CoinPack"
                    ? Loc.T("currency.coins", Loc.Number(item.CoinsGranted))
                    : Loc.T("currency.lives", item.Quantity);
                UIFactory.Label(card, title, Theme.HeaderSize - 6, item.Kind == "CoinPack" ? Theme.Gold : Theme.Danger, TextAnchor.MiddleLeft, FontStyle.Bold);

                HorizontalLayoutGroup row = UIFactory.Row(card, 90, 16);
                if (item.Kind == "LivesPack")
                {
                    string price = (item.PriceCoins > 0 ? Loc.T("currency.coins", item.PriceCoins) : string.Empty)
                        + (item.PriceCoins > 0 && item.PriceOrbes > 0 ? " + " : string.Empty)
                        + (item.PriceOrbes > 0 ? Loc.T("currency.orbes", item.PriceOrbes) : string.Empty);
                    UIFactory.Button(row.transform, price, () => _ = BuyAsync(item, item.PriceCoins > 0 ? "Coins" : "Orbes"), Theme.Gold, Theme.BodySize);
                }
                else
                {
                    UIFactory.Button(row.transform, Loc.T("currency.orbes", item.PriceOrbes), () => _ = BuyAsync(item, "Orbes"), Theme.Orbe, Theme.BodySize, Theme.Background);
                }
            }
        }

        private void FillCosmetics()
        {
            foreach (ShopItemDto item in _shop.Items.Where(i => i.Kind == "BattlePass" || i.Kind == "RemoveAds" || i.Kind == "RarePerk"))
            {
                RectTransform card = Widgets.Card(_list, 240, Theme.PanelLight);
                UIFactory.Label(card, Loc.T("shop.item." + item.Kind), Theme.HeaderSize - 6, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("shop.item." + item.Kind + ".desc"), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                HorizontalLayoutGroup row = UIFactory.Row(card, 90, 16);
                if (item.PriceOrbes > 0)
                {
                    UIFactory.Button(row.transform, Loc.T("currency.orbes", item.PriceOrbes), () => _ = BuyAsync(item, "Orbes"), Theme.Orbe, Theme.BodySize, Theme.Background);
                }
                if (item.PriceCents > 0)
                {
                    string price = Game.Iap.LocalizedPrice(item.Sku) ?? (item.PriceCents / 100m).ToString("0.00") + " EUR";
                    UIFactory.Button(row.transform, price, () => _ = BuyRealMoneyAsync(item.Sku), Theme.Gold, Theme.BodySize);
                }
            }

            Widgets.SectionTitle(_list, Loc.T("shop.cosmetics"));
            foreach (ShopItemDto item in _shop.Items.Where(i => i.Kind == "Cosmetic"))
            {
                RectTransform card = Widgets.Card(_list, 190);
                UIFactory.Label(card, Loc.T("cosmetic." + item.CosmeticId), Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                if (item.SoldOut && item.RequiredVip > 0)
                {
                    UIFactory.Label(card, Loc.T("shop.requiresVip", item.RequiredVip), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                }
                else
                {
                    UIFactory.Button(card, Loc.T("currency.orbes", item.PriceOrbes), () => _ = BuyAsync(item, "Orbes"), Theme.Orbe, Theme.BodySize, Theme.Background);
                }
            }

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
                HorizontalLayoutGroup row = UIFactory.Row(_list, 100, 16);
                UIFactory.Label(row.transform, Loc.T("cosmetic." + cosmetic), Theme.BodySize, equipped ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft);
                if (!equipped && !cosmetic.StartsWith("emote.", StringComparison.Ordinal))
                {
                    string id = cosmetic;
                    UIFactory.Width(UIFactory.Button(row.transform, Loc.T("shop.equip"), () => _ = EquipAsync(id), Theme.PanelLight, Theme.SmallSize, Theme.Text), 260);
                }
            }
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
