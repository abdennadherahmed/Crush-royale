using System.Collections.Generic;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>VIP status: current tier, progress to the next one, and what each of the 10 tiers grants.</summary>
    public sealed class VipScreen : UIScreen
    {
        public override System.Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("vip.title");
            RectTransform list = UIFactory.ScrollList(body, 22, 32);
            UIFactory.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 170);

            ProfileDto profile = Game.Backend.Profile;
            GameBalance balance = Game.Backend.Balance;
            VipDto vip = profile?.Vip ?? new VipDto();
            BuildHeader(list, vip, balance);

            var system = new VipSystem(balance);
            for (int tier = 1; tier <= balance.Vip.MaxLevel; tier++)
            {
                TierCard(list, system.GetBenefit((VipTier)tier), tier, vip.Tier, balance);
            }

            Button shop = UIFactory.Button(body, Loc.T("vip.toShop"), () => UI.Show<ShopScreen>(1), Theme.Gold, Theme.BodySize + 4);
            UIFactory.Anchor(shop.GetComponent<RectTransform>(), 0.15f, 0.02f, 0.85f, 0.1f);
        }

        private void BuildHeader(Transform list, VipDto vip, GameBalance balance)
        {
            Image panel = UIFactory.Panel("Status", list, Theme.Panel);
            UIFactory.Height(panel, 420);
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

            string next = vip.NextThresholdCents.HasValue
                ? Loc.T("vip.next", vip.Tier + 1, Euros(vip.NextThresholdCents.Value - vip.LifetimeSpendCents))
                : Loc.T("shop.vipMax");
            Text nextText = UIFactory.Label(panel.transform, next, Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(nextText.rectTransform, 0.38f, 0.38f, 0.96f, 0.58f);

            UIFactory.ProgressBar(panel.transform, vip.NextThresholdCents.HasValue ? vip.Progress : 1f, Theme.Gold, out RectTransform bar);
            UIFactory.Anchor(bar, 0.06f, 0.2f, 0.94f, 0.33f);
            Text how = UIFactory.Label(panel.transform, Loc.T("vip.how"), Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(how.rectTransform, 0.06f, 0.04f, 0.94f, 0.19f);
        }

        private void TierCard(Transform list, VipBenefit benefit, int tier, int current, GameBalance balance)
        {
            var lines = new List<(string Icon, string Text)>();
            lines.Add(("item_coins", Loc.T("vip.coinBonus", benefit.CoinBonusPermille / 10)));
            if (benefit.OrbeBonusPermille > 0)
            {
                lines.Add(("item_orbs", Loc.T("vip.orbeBonus", benefit.OrbeBonusPermille / 10)));
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

            bool reached = tier <= current;
            bool isCurrent = tier == current;
            Image card = UIFactory.Panel("Tier", list, Theme.Panel);
            UIFactory.Height(card, 150 + lines.Count * 78);
            UiKit.CardFrame(card);
            if (!reached)
            {
                card.color = new Color(0.78f, 0.75f, 0.85f, 1f);
            }
            if (isCurrent)
            {
                Outline glow = card.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                glow.effectDistance = new Vector2(7, -7);
            }

            float headerShare = 130f / (150 + lines.Count * 78);
            RectTransform header = UIFactory.Anchor(UIFactory.Rect("Header", card.transform), 0.03f, 1f - headerShare, 0.97f, 0.99f);
            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Medal", header), 0f, 0.05f, 0.2f, 0.95f);
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            UiKit.RoundBadge(medal, crystal: !reached);
            Text number = UIFactory.Label(medal, tier.ToString(), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(number.rectTransform);
            Widgets.TitleOutline(number);

            Text name = UIFactory.Label(header, Loc.T("vip.badge", tier), Theme.HeaderSize - 4, reached ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.2f, 0.45f, 0.7f, 1f);
            Widgets.TitleOutline(name);
            Text threshold = UIFactory.Label(header, Loc.T("vip.threshold", Euros(benefit.ThresholdCents)), Theme.SmallSize - 2, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(threshold.rectTransform, 0.2f, 0f, 0.7f, 0.48f);
            Text state = UIFactory.Label(header, Loc.T(isCurrent ? "vip.current" : reached ? "vip.reached" : "vip.locked"), Theme.SmallSize, isCurrent ? Theme.Gold : reached ? Theme.Success : Theme.TextMuted, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Anchor(state.rectTransform, 0.62f, 0.2f, 1f, 0.8f);
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

        private void AddIf(List<(string, string)> lines, bool condition, string icon, string key)
        {
            if (condition)
            {
                lines.Add((icon, Loc.T(key)));
            }
        }

        private static string Euros(long cents) => (cents / 100m).ToString(cents % 100 == 0 ? "0" : "0.00") + " €";
    }
}
