using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// VIP status: current tier and progress (no prices shown), the daily VIP gift from VIP 6, the benefits of the reached
    /// tiers and of the next one; further tiers stay secret until the previous one is reached.
    /// </summary>
    public sealed class VipScreen : UIScreen
    {
        protected override string BackdropScene => "vip";

        protected override CrushRoyale.Core.Story.Kingdom BackdropKingdom => CrushRoyale.Core.Story.Kingdom.Central;

        public override System.Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("vip.title");
            RectTransform list = UIFactory.ScrollList(body, 22, 32);
            UIFactory.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 170);

            ProfileDto profile = Game.Backend.Profile;
            GameBalance balance = Game.Backend.Balance;
            VipDto vip = profile?.Vip ?? new VipDto();
            BuildHeader(list, vip);
            if (VipGifts.HasGift(vip.Tier))
            {
                BuildGift(list, vip.Tier, profile?.VipGiftAvailable ?? false);
            }

            var system = new VipSystem(balance);
            for (int tier = 1; tier <= balance.Vip.MaxLevel; tier++)
            {
                if (tier <= vip.Tier + 1)
                {
                    TierCard(list, system.GetBenefit((VipTier)tier), tier, vip.Tier, balance);
                }
                else
                {
                    LockedCard(list, tier);
                }
            }

            Button shop = UIFactory.Button(body, Loc.T("vip.toShop"), () => UI.Show<ShopScreen>(1), Theme.Gold, Theme.BodySize + 4);
            UIFactory.Anchor(shop.GetComponent<RectTransform>(), 0.15f, 0.02f, 0.85f, 0.1f);
        }

        private void BuildHeader(Transform list, VipDto vip)
        {
            Image panel = UIFactory.Panel("Status", list, Theme.Panel);
            UIFactory.Height(panel, 400);
            UiKit.FramePanel(panel);

            Sprite crown = UiKit.Art("item_crown") ?? ArtLibrary.Icon("crown");
            if (crown != null)
            {
                Image icon = UIFactory.Icon(panel.transform, crown, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.05f, 0.38f, 0.35f, 0.92f);
                icon.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }
            Text tier = UIFactory.Label(panel.transform, vip.Tier > 0 ? Loc.T("vip.badge", vip.Tier) : Loc.T("vip.none"), 88, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(tier.rectTransform, 0.38f, 0.58f, 0.96f, 0.92f);
            Widgets.TitleOutline(tier);

            bool max = !vip.NextThresholdCents.HasValue;
            string next = max ? Loc.T("shop.vipMax") : Loc.T("vip.progress", Mathf.RoundToInt(vip.Progress * 100), vip.Tier + 1);
            Text nextText = UIFactory.Label(panel.transform, next, Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(nextText.rectTransform, 0.38f, 0.38f, 0.96f, 0.58f);

            UIFactory.ProgressBar(panel.transform, max ? 1f : vip.Progress, Theme.Gold, out RectTransform bar);
            UIFactory.Anchor(bar, 0.06f, 0.2f, 0.94f, 0.33f);
            Text how = UIFactory.Label(panel.transform, Loc.T("vip.how"), Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(how.rectTransform, 0.06f, 0.04f, 0.94f, 0.19f);
        }

        /// <summary>Today's VIP gift (VIP 6+) with its content and the claim button.</summary>
        private void BuildGift(Transform list, int tier, bool available)
        {
            VipGift gift = VipGifts.For(tier, CrushRoyale.Core.Common.TimeUtil.DayIndex(System.DateTime.UtcNow));
            Image panel = UIFactory.Panel("Gift", list, Theme.Panel);
            UIFactory.Height(panel, 380);
            UiKit.FramePanel(panel);
            if (available)
            {
                Outline glow = panel.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                glow.effectDistance = new Vector2(7, -7);
            }

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", panel.transform), 0.08f, 0.76f, 0.92f, 0.99f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, Loc.T("vip.dailyGift"), Theme.BodySize + 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
            Widgets.TitleOutline(title);

            Sprite box = UiKit.Art("item_gift");
            if (box != null)
            {
                Image icon = UIFactory.Icon(panel.transform, box, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.04f, 0.26f, 0.24f, 0.74f);
                if (available)
                {
                    Breathe breathe = icon.gameObject.AddComponent<Breathe>();
                    breathe.Amount = 0.06f;
                    breathe.Speed = 4f;
                }
            }

            List<RevealItem> items = GiftItems(gift);
            RectTransform grid = UIFactory.Anchor(UIFactory.Rect("Content", panel.transform), 0.26f, 0.28f, 0.96f, 0.74f);
            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            const int columns = 3;
            // The cell height follows the number of rows, because a GridLayoutGroup does not clip: with more than
            // six rewards the chips ran straight out of the bottom of their band and printed over the Claim button.
            // The band is 46% of a 380 px panel.
            int rows = Mathf.Max(1, (items.Count + columns - 1) / columns);
            float band = 380f * 0.46f;
            float cellHeight = Mathf.Clamp((band - (rows - 1) * 6f) / rows, 34f, 76f);
            layout.cellSize = new Vector2(170, cellHeight);
            layout.spacing = new Vector2(8, 6);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;
            GridFit.On(layout);
            foreach (RevealItem item in items)
            {
                RectTransform chip = UIFactory.Rect("Chip", grid);
                if (item.Art != null)
                {
                    Image art = UIFactory.Icon(chip, item.Art, Color.white, 0);
                    UIFactory.Anchor(art.rectTransform, 0f, 0.05f, 0.36f, 0.95f);
                }
                Text label = UIFactory.Label(chip, item.Caption, Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(label.rectTransform, 0.38f, 0f, 1f, 1f);
                Widgets.TitleOutline(label);
            }

            if (available)
            {
                Button claim = UIFactory.Button(panel.transform, Loc.T("vip.giftClaim"), () => _ = ClaimGiftAsync(tier), Theme.Success, Theme.BodySize);
                UIFactory.Anchor(claim.GetComponent<RectTransform>(), 0.2f, 0.04f, 0.8f, 0.24f);
                claim.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }
            else
            {
                Text done = UIFactory.Label(panel.transform, Loc.T("vip.giftTomorrow"), Theme.SmallSize, Theme.Success, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(done.rectTransform, 0.05f, 0.05f, 0.95f, 0.24f);
                Widgets.TitleOutline(done);
            }
        }

        private List<RevealItem> GiftItems(VipGift gift)
        {
            var reward = new RewardDto
            {
                Coins = gift.Reward.Coins,
                Orbes = gift.Reward.Orbes,
                Lives = gift.Reward.Lives,
                PowerUps = gift.Reward.PowerUps.ToDictionary(p => p.Key.ToString(), p => p.Value)
            };
            List<RevealItem> items = RevealOverlay.FromReward(reward);
            if (gift.PetFragments > 0)
            {
                items.Add(new RevealItem { Art = PetsScreen.PetArt(gift.FragmentsPet.ToString()), Caption = Loc.T("pets.fragmentsGain", gift.PetFragments), Rare = true });
            }
            return items;
        }

        private async Task ClaimGiftAsync(int tier)
        {
            VipGiftResponse response = await Api(api => api.ClaimVipGiftAsync());
            if (response == null || this == null)
            {
                return;
            }
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null)
            {
                profile.VipGiftAvailable = false;
                profile.Pets = response.Pets ?? profile.Pets;
            }
            Game.Backend.ApplyWallet(response.Wallet);
            Game.Backend.ApplyInventory(response.Inventory);
            Game.Backend.ApplyLives(response.Lives);
            Game.Audio.PlaySFX(SoundIds.WinFanfare);
            Game.Telemetry.Track("vip_gift", ("tier", tier));

            List<RevealItem> items = RevealOverlay.FromReward(response.Reward);
            if (response.PetFragments > 0 && !string.IsNullOrEmpty(response.FragmentsPet))
            {
                items.Add(new RevealItem { Art = PetsScreen.PetArt(response.FragmentsPet), Caption = Loc.T("pets.fragmentsGain", response.PetFragments), Rare = true });
            }
            await RevealOverlay.PlayChestAsync(items, UiKit.Art("item_gift"), UiKit.Art("chest_open_crystal") ?? UiKit.Art("item_gift"), Loc.T("vip.dailyGift"));
            if (this != null)
            {
                Rebuild();
            }
        }

        private void TierCard(Transform list, VipBenefit benefit, int tier, int current, GameBalance balance)
        {
            var lines = new List<(string Icon, string Text)>();
            lines.Add(("item_coins", Loc.T("vip.coinBonus", benefit.CoinBonusPermille / 10)));
            if (benefit.OrbeBonusPermille > 0)
            {
                lines.Add(("item_orbs", Loc.T("vip.orbeBonus", benefit.OrbeBonusPermille / 10)));
            }
            int discount = Mathf.Min(balance.PowerUps.MaxVipDiscountPermille, tier * balance.PowerUps.VipDiscountPerLevelPermille) / 10;
            if (discount > 0)
            {
                lines.Add(("item_bolt", Loc.T("vip.discount", discount)));
            }
            VipBalance v = balance.Vip;
            AddIf(lines, tier == v.FreeLifePerDayFromLevel, "item_heart", "vip.freeLife");
            AddIf(lines, tier == v.BasicCosmeticsFromLevel, "item_gift", "vip.basicCosmetics");
            AddIf(lines, tier == v.RechargeSpeedupFromLevel, "item_hourglass", "vip.recharge");
            AddIf(lines, tier == v.RareCosmeticsFromLevel, "item_pouch", "vip.rareCosmetics");
            AddIf(lines, tier == v.DoubleEventRewardsFromLevel, "item_stars", "vip.doubleEvents");
            AddIf(lines, tier == v.SkipAdPerDayFromLevel, "item_bolt", "vip.skipAd");
            AddIf(lines, tier == v.FreeContinuePerDayFromLevel, "item_xp", "vip.freeContinue");
            if (tier >= v.StealPowerUpFromLevel && benefit.StealChancePermille > 0)
            {
                lines.Add(("item_medal", Loc.T("vip.steal", benefit.StealChancePermille / 10)));
            }
            AddIf(lines, tier == v.RarePerkLevel, "item_crown", "vip.rarePerk");
            VipGift gift = VipGifts.For(tier, 0);
            if (gift != null)
            {
                int boosts = gift.Reward.PowerUps.Values.Sum();
                lines.Add(("item_gift", Loc.T("vip.giftLine", gift.Reward.Orbes, Loc.Number(gift.Reward.Coins), boosts, gift.PetFragments)));
                if (gift.Reward.Lives > 0)
                {
                    lines.Add(("item_heart", Loc.T("vip.giftLives", gift.Reward.Lives)));
                }
            }

            bool reached = tier <= current;
            bool isCurrent = tier == current;
            bool isNext = tier == current + 1;
            Image card = UIFactory.Panel("Tier", list, Theme.Panel);
            float height = 150 + lines.Count * 78;
            UIFactory.Height(card, height);
            UiKit.CardFrame(card);
            if (isCurrent || isNext)
            {
                Outline glow = card.gameObject.AddComponent<Outline>();
                glow.effectColor = isCurrent ? new Color(1f, 0.82f, 0.3f, 0.95f) : new Color(0.45f, 0.9f, 1f, 0.9f);
                glow.effectDistance = new Vector2(7, -7);
            }

            float headerShare = 130f / height;
            RectTransform header = UIFactory.Anchor(UIFactory.Rect("Header", card.transform), 0.03f, 1f - headerShare, 0.97f, 0.99f);
            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Medal", header), 0f, 0.05f, 0.2f, 0.95f);
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            UiKit.RoundBadge(medal, crystal: !reached);
            Text number = UIFactory.Label(medal, tier.ToString(), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(number.rectTransform);
            Widgets.TitleOutline(number);

            Text name = UIFactory.Label(header, Loc.T("vip.badge", tier), Theme.HeaderSize - 4, reached ? Theme.Gold : Theme.Crystal, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.2f, 0.1f, 0.62f, 0.95f);
            Widgets.TitleOutline(name);
            string stateKey = isCurrent ? "vip.current" : reached ? "vip.reached" : "vip.nextLabel";
            Text state = UIFactory.Label(header, Loc.T(stateKey), Theme.SmallSize, isCurrent ? Theme.Gold : reached ? Theme.Success : Theme.Crystal, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Anchor(state.rectTransform, 0.6f, 0.2f, 1f, 0.8f);
            Widgets.TitleOutline(state);

            RectTransform rows = UIFactory.Anchor(UIFactory.Rect("Benefits", card.transform), 0.05f, 0.04f, 0.95f, 1f - headerShare);
            VerticalLayoutGroup layout = rows.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            foreach ((string icon, string text) in lines)
            {
                RectTransform row = UIFactory.Rect("Benefit", rows);
                Sprite art = UiKit.Art(icon);
                if (art != null)
                {
                    Image image = UIFactory.Icon(row, art, Color.white, 0);
                    UIFactory.Anchor(image.rectTransform, 0f, 0.08f, 0.1f, 0.92f);
                }
                Text label = UIFactory.Label(row, text, Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleLeft);
                UIFactory.Anchor(label.rectTransform, 0.12f, 0f, 1f, 1f);
            }
        }

        /// <summary>Tier beyond the next one: rewards stay a mystery until the previous tier is reached.</summary>
        private void LockedCard(Transform list, int tier)
        {
            Image card = UIFactory.Panel("Locked", list, Theme.Panel);
            UIFactory.Height(card, 170);
            UiKit.CardFrame(card);
            card.color = new Color(0.62f, 0.6f, 0.7f, 1f);

            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Medal", card.transform), 0.03f, 0.12f, 0.2f, 0.88f);
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            UiKit.RoundBadge(medal, crystal: true);
            Sprite lockArt = UiKit.Art("item_lock");
            if (lockArt != null)
            {
                Image lockIcon = UIFactory.Icon(medal, lockArt, Color.white, 0);
                UIFactory.Stretch(lockIcon.rectTransform, 22, 22, 18, 18);
            }
            Text name = UIFactory.Label(card.transform, Loc.T("vip.badge", tier) + "  ???", Theme.HeaderSize - 6, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.24f, 0.5f, 0.97f, 0.92f);
            Widgets.TitleOutline(name);
            Text hint = UIFactory.Label(card.transform, Loc.T("vip.unlockFirst", tier - 1), Theme.SmallSize, Theme.Warning, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(hint.rectTransform, 0.24f, 0.1f, 0.97f, 0.5f);
            Widgets.TitleOutline(hint);
        }

        private void AddIf(List<(string, string)> lines, bool condition, string icon, string key)
        {
            if (condition)
            {
                lines.Add((icon, Loc.T(key)));
            }
        }
    }
}
