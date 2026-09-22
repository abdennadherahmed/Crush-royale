using System;
using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// The bag: everything the player owns in one place (coins, orbs, lives, boosts, pet fragments, cosmetics) with the
    /// conversions right there. Fragments used to be buried inside the pet cards, so players could not find them.
    /// </summary>
    public sealed class BagScreen : UIScreen
    {
        private RectTransform _list;

        protected override string BackdropScene => "bag";

        protected override CrushRoyale.Core.Story.Kingdom BackdropKingdom => CrushRoyale.Core.Story.Kingdom.East;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("bag.title");
            CurrencyBar bar = CurrencyBar.Create(body);
            UIFactory.Anchor((RectTransform)bar.transform, 0.02f, 0.935f, 0.98f, 1f);

            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.925f);
            _list = UIFactory.ScrollList(holder, 20, 26);
            UIFactory.Stretch((RectTransform)_list.parent.parent);

            ProfileDto profile = Game.Backend.Profile;
            if (profile == null)
            {
                Widgets.EmptyState(_list, "item_pouch", Loc.T("error.offline"));
                return;
            }

            BuildWallet(profile);
            BuildBoosts(profile);
            BuildFragments(profile);
            BuildCosmetics(profile);
        }

        // ------------------------------------------------------------------ wallet and lives

        private void BuildWallet(ProfileDto profile)
        {
            Widgets.SectionTitle(_list, Loc.T("bag.wallet"));
            RectTransform grid = Grid(2, 190);
            Tile(grid, "item_coins", Loc.Number(profile.Wallet?.Coins ?? 0), Loc.T("currency.coinsName"), Theme.Gold, () => UI.Show<ShopScreen>(ShopScreen.CoinsTab));
            Tile(grid, "item_orbs", Loc.Number(profile.Wallet?.Orbes ?? 0), Loc.T("currency.orbesName"), Theme.Orbe, () => UI.Show<ShopScreen>(ShopScreen.OrbesTab));

            LivesDto lives = profile.Lives;
            int max = Math.Max(1, Game.Backend.Balance?.Stamina?.MaxRegenLives ?? 5);
            string livesValue = (lives?.Lives ?? 0) + " / " + max;
            Tile(grid, "item_heart", livesValue, LivesCaption(lives, max), Theme.Danger, () => UI.Show<ShopScreen>(ShopScreen.CoinsTab));
            Tile(grid, "item_ticket", (lives?.FreeContinues ?? 0).ToString(), Loc.T("bag.continues"), Theme.Crystal, null);
        }

        private string LivesCaption(LivesDto lives, int max)
        {
            if (lives == null || lives.Lives >= max)
            {
                return Loc.T("bag.livesFull");
            }
            int seconds = Mathf.CeilToInt(lives.RechargeSeconds);
            return Loc.T("bag.livesIn", seconds / 60, seconds % 60);
        }

        // ------------------------------------------------------------------ boosts

        private void BuildBoosts(ProfileDto profile)
        {
            Widgets.SectionTitle(_list, Loc.T("bag.boosts"));
            GameBalance balance = Game.Backend.Balance;
            List<PowerUpDefinition> defs = balance.PowerUps.Definitions.Where(d => !d.PvpOnly).ToList();
            RectTransform grid = Grid(3, 240);
            foreach (PowerUpDefinition def in defs)
            {
                string type = def.Type.ToString();
                int count = profile.Inventory != null && profile.Inventory.PowerUps.TryGetValue(type, out int n) ? n : 0;
                RectTransform cell = UIFactory.Rect("Boost", grid);
                Image card = UIFactory.Panel("Card", cell, Theme.Panel);
                UIFactory.Stretch(card.rectTransform);
                UiKit.CardFrame(card);
                Button button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = card;
                card.gameObject.AddComponent<ButtonFeedback>();
                button.onClick.AddListener(() => _ = GetItemPopup.ShowAsync(def.Type, Rebuild));

                Sprite art = ArtLibrary.PowerUp(def.Type);
                if (art != null)
                {
                    Image icon = UIFactory.Icon(card.transform, art, count > 0 ? Color.white : new Color(0.5f, 0.5f, 0.55f, 1f), 0);
                    icon.preserveAspect = true;
                    UIFactory.Anchor(icon.rectTransform, 0.2f, 0.42f, 0.8f, 0.95f);
                }
                Text name = UIFactory.Label(card.transform, Loc.T("powerup." + type), Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, 0.04f, 0.24f, 0.96f, 0.42f);
                Widgets.TitleOutline(name);
                Text owned = UIFactory.Label(card.transform, "x" + count, Theme.BodySize, count > 0 ? Theme.Gold : Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(owned.rectTransform, 0.04f, 0.04f, 0.96f, 0.24f);
                Widgets.TitleOutline(owned);
            }
        }

        // ------------------------------------------------------------------ pet fragments

        private void BuildFragments(ProfileDto profile)
        {
            PetsDto pets = profile.Pets;
            if (pets == null || pets.Pets.Count == 0)
            {
                return;
            }
            Widgets.SectionTitle(_list, Loc.T("bag.fragments"));
            int ratio = Math.Max(1, Game.Backend.Balance?.Pets?.ConvertRatio ?? 3);
            UIFactory.Height(UIFactory.Label(_list, Loc.T("bag.fragmentsHelp", ratio), Theme.SmallSize, Theme.TextMuted), 60);

            List<PetDto> withFragments = pets.Pets.Where(p => p.Fragments > 0).OrderByDescending(p => p.Fragments).ToList();
            if (withFragments.Count == 0)
            {
                Widgets.EmptyState(_list, "item_fragment", Loc.T("bag.noFragments"), Loc.T("bag.goSummon"), () => UI.Show<PetsScreen>());
                return;
            }

            foreach (PetDto pet in withFragments)
            {
                Image row = UIFactory.Panel("Fragment", _list, Theme.Panel);
                UIFactory.Height(row, 170);
                UiKit.CardFrame(row);
                Sprite art = PetsScreen.PetArt(pet.Type);
                if (art != null)
                {
                    Image icon = UIFactory.Icon(row.transform, art, Color.white, 0);
                    icon.preserveAspect = true;
                    UIFactory.Anchor(icon.rectTransform, 0.03f, 0.08f, 0.2f, 0.92f);
                }
                Text name = UIFactory.Label(row.transform, PetsScreen.PetName(Loc, pet.Type), Theme.BodySize - 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, 0.23f, 0.5f, 0.66f, 0.92f);
                Text amount = UIFactory.Label(row.transform, Loc.T("bag.fragmentCount", pet.Fragments, pet.Owned ? pet.AwakenFragments : pets.UnlockFragments),
                    Theme.SmallSize, pet.Owned ? Theme.Crystal : Theme.Gold, TextAnchor.MiddleLeft);
                UIFactory.Anchor(amount.rectTransform, 0.23f, 0.12f, 0.66f, 0.5f);

                // The pet screen owns the conversion popup: the bag just points at the pet that has the fragments.
                Button convert = UIFactory.Button(row.transform, Loc.T("pets.convert"), () => UI.Show<PetsScreen>(), Theme.Crystal, Theme.SmallSize);
                UIFactory.Anchor(convert.GetComponent<RectTransform>(), 0.68f, 0.2f, 0.96f, 0.8f);
                convert.interactable = pet.Fragments >= Math.Max(1, Game.Backend.Balance?.Pets?.ConvertRatio ?? 3);
            }
        }

        // ------------------------------------------------------------------ cosmetics

        private void BuildCosmetics(ProfileDto profile)
        {
            int owned = profile.Inventory?.Cosmetics?.Count ?? 0;
            Widgets.SectionTitle(_list, Loc.T("bag.cosmetics"));
            Image row = UIFactory.Panel("Cosmetics", _list, Theme.Panel);
            UIFactory.Height(row, 150);
            UiKit.CardFrame(row);
            Text label = UIFactory.Label(row.transform, Loc.T("bag.cosmeticsOwned", owned), Theme.BodySize - 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.05f, 0.1f, 0.62f, 0.9f);
            Button open = UIFactory.Button(row.transform, Loc.T("bag.openCollection"), () => UI.Show<ProfileScreen>(), Theme.Gold, Theme.SmallSize);
            UIFactory.Anchor(open.GetComponent<RectTransform>(), 0.64f, 0.2f, 0.96f, 0.8f);
        }

        // ------------------------------------------------------------------ helpers

        private RectTransform Grid(int columns, float cellHeight)
        {
            RectTransform gridRect = UIFactory.Rect("Grid", _list);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            float width = (1080f - 64f - (columns - 1) * 20f) / columns;
            grid.cellSize = new Vector2(width, cellHeight);
            grid.spacing = new Vector2(20, 20);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.childAlignment = TextAnchor.UpperCenter;
            GridFit.On(grid);
            return gridRect;
        }

        private void Tile(Transform grid, string icon, string value, string caption, Color color, Action onTap)
        {
            Image card = UIFactory.Panel("Tile", grid, Theme.Panel);
            UiKit.CardFrame(card);
            if (onTap != null)
            {
                Button button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = card;
                card.gameObject.AddComponent<ButtonFeedback>();
                button.onClick.AddListener(() => onTap());
            }
            Sprite art = UiKit.Art(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(card.transform, art, Color.white, 0);
                image.preserveAspect = true;
                UIFactory.Anchor(image.rectTransform, 0.06f, 0.24f, 0.36f, 0.92f);
            }
            Text amount = UIFactory.Label(card.transform, value, Theme.HeaderSize - 6, color, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(amount.rectTransform, 0.4f, 0.46f, 0.96f, 0.92f);
            Widgets.TitleOutline(amount);
            Text sub = UIFactory.Label(card.transform, caption, Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(sub.rectTransform, 0.4f, 0.1f, 0.96f, 0.44f);
        }
    }
}
