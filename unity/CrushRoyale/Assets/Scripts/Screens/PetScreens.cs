using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Pets: summon with orbes (odds and pity shown before paying, as Google Play requires for random items),
    /// the collection with levels, XP and fragments, equip and awakening.
    /// </summary>
    public sealed class PetsScreen : UIScreen
    {
        private RectTransform _list;

        public override Type BackTarget => typeof(MainMenuScreen);

        /// <summary>Sprite of a pet ("FrostFox" -> Art/Pets/frostfox), or the summon egg for null.</summary>
        public static Sprite PetArt(string type) => ArtLibrary.Load("Art/Pets/" + (string.IsNullOrEmpty(type) ? "egg" : type.ToLowerInvariant()));

        /// <summary>Localized pet name (literal keys keep the localization validator aware of them).</summary>
        public static string PetName(Localization loc, string type)
        {
            switch (type)
            {
                case "FrostFox": return loc.T("pet.FrostFox");
                case "SunFennec": return loc.T("pet.SunFennec");
                case "ForestOwl": return loc.T("pet.ForestOwl");
                case "EmberSalamander": return loc.T("pet.EmberSalamander");
                case "CrystalDrake": return loc.T("pet.CrystalDrake");
                default: return type ?? string.Empty;
            }
        }

        protected override void Build()
        {
            RectTransform body = Frame("pets.title");
            CurrencyBar bar = CurrencyBar.Create(body);
            UIFactory.Anchor((RectTransform)bar.transform, 0.02f, 0.93f, 0.98f, 1f);

            PetsDto pets = Game.Backend.Profile?.Pets;
            BuildSummon(body, pets);

            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.6f);
            _list = UIFactory.ScrollList(holder, 18, 28);
            UIFactory.Stretch((RectTransform)_list.parent.parent);
            if (pets == null)
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("error.offline"), Theme.BodySize, Theme.TextMuted), 160);
                return;
            }
            foreach (PetDto pet in pets.Pets)
            {
                PetCard(pet, pets);
            }
        }

        private void BuildSummon(RectTransform body, PetsDto pets)
        {
            Image panel = UIFactory.Panel("Summon", body, new Color(0.12f, 0.07f, 0.26f, 0.92f));
            RectTransform rect = UIFactory.Anchor(panel.rectTransform, 0.03f, 0.615f, 0.97f, 0.92f);

            Image glow = UIFactory.Icon(rect, ProceduralSprites.Glow(128), new Color(Theme.Orbe.r, Theme.Orbe.g, Theme.Orbe.b, 0.55f), 0);
            UIFactory.Anchor(glow.rectTransform, -0.05f, -0.05f, 0.45f, 1.05f);
            glow.gameObject.AddComponent<Pulse>();
            Sprite egg = PetArt(null);
            if (egg != null)
            {
                Image image = UIFactory.Icon(rect, egg, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.02f, 0.08f, 0.38f, 0.92f);
                image.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }

            Text title = UIFactory.Label(rect, Loc.T("pets.summonTitle"), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.4f, 0.74f, 0.98f, 0.96f);
            string odds = pets == null ? string.Empty : Loc.T("pets.odds", pets.WholePetOneIn, Math.Max(1, pets.PityPulls - pets.PullsSinceWholePet));
            Text oddsLabel = UIFactory.Label(rect, odds, Theme.SmallSize - 2, Theme.TextMuted, TextAnchor.UpperLeft);
            UIFactory.Anchor(oddsLabel.rectTransform, 0.4f, 0.46f, 0.98f, 0.74f);

            int cost1 = pets?.SummonCostOrbes ?? 30;
            int cost10 = pets?.Summon10CostOrbes ?? 270;
            Button one = UIFactory.Button(rect, Loc.T("pets.summon1", cost1), () => _ = SummonAsync(1), Theme.PanelLight, Theme.SmallSize, Theme.Text);
            UIFactory.Anchor(one.GetComponent<RectTransform>(), 0.4f, 0.06f, 0.67f, 0.42f);
            Button ten = UIFactory.Button(rect, Loc.T("pets.summon10", cost10), () => _ = SummonAsync(10), Theme.GoldDark, Theme.SmallSize, Theme.Text);
            UIFactory.Anchor(ten.GetComponent<RectTransform>(), 0.7f, 0.06f, 0.97f, 0.42f);
            AddOrbeIcon(one);
            AddOrbeIcon(ten);
        }

        private static void AddOrbeIcon(Button button)
        {
            Sprite orb = ArtLibrary.Icon("orb");
            if (orb == null)
            {
                return;
            }
            Image icon = UIFactory.Icon(button.transform, orb, Color.white, 0);
            UIFactory.Anchor(icon.rectTransform, 0.78f, 0.55f, 0.98f, 0.98f);
        }

        private void PetCard(PetDto pet, PetsDto pets)
        {
            // Plain panel (not Widgets.Card): children are placed with anchors, not a layout group.
            Image panel = UIFactory.Panel("Pet", _list, pet.Type == pets.Equipped ? new Color(0.3f, 0.22f, 0.08f, 0.95f) : Theme.Panel);
            UIFactory.Height(panel, 300);
            RectTransform card = panel.rectTransform;

            Sprite art = PetArt(pet.Type);
            if (art != null)
            {
                Image image = UIFactory.Icon(card, art, pet.Owned ? Color.white : new Color(0.15f, 0.12f, 0.25f, 1f), 0);
                UIFactory.Anchor(image.rectTransform, 0.01f, 0.05f, 0.3f, 0.95f);
            }

            Text name = UIFactory.Label(card, PetName(Loc, pet.Type), Theme.BodySize, pet.Owned ? Theme.Gold : Theme.TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.32f, 0.74f, 0.98f, 0.96f);

            string line = pet.Owned
                ? Loc.T("pets.level", pet.Level) + "  ·  " + Loc.T("pets.chance", pet.AutoMovePercent)
                : Loc.T("pets.notOwned");
            Text detail = UIFactory.Label(card, line, Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Anchor(detail.rectTransform, 0.32f, 0.56f, 0.98f, 0.76f);

            if (pet.Owned)
            {
                float progress = pet.XpForNext > pet.XpForCurrent ? Mathf.Clamp01((pet.Xp - pet.XpForCurrent) / (float)(pet.XpForNext - pet.XpForCurrent)) : 1f;
                UIFactory.ProgressBar(card, progress, Theme.Crystal, out RectTransform xpBar);
                UIFactory.Anchor(xpBar, 0.32f, 0.47f, 0.7f, 0.54f);
            }

            string info = Loc.T("pets.fragments", pet.Fragments) + "   " + Loc.T("pets.gift", pets.MaxLevel, Loc.T("powerup." + pet.PowerUp));
            Text infoLabel = UIFactory.Label(card, info, Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.UpperLeft);
            UIFactory.Anchor(infoLabel.rectTransform, 0.32f, 0.26f, 0.98f, 0.46f);

            if (!pet.Owned)
            {
                return;
            }
            if (pet.CanAwaken)
            {
                Button awaken = UIFactory.Button(card, Loc.T("pets.awaken", pet.AwakenFragments, pet.AwakenCoins), () => _ = AwakenAsync(pet.Type),
                    pet.Fragments >= pet.AwakenFragments ? Theme.Orbe : Theme.BackgroundLight, Theme.SmallSize - 6, Theme.Text);
                UIFactory.Anchor(awaken.GetComponent<RectTransform>(), 0.32f, 0.03f, 0.64f, 0.24f);
            }
            bool equipped = pet.Type == pets.Equipped;
            Button equip = UIFactory.Button(card, Loc.T(equipped ? "pets.equipped" : "pets.equip"), () =>
            {
                if (!equipped)
                {
                    _ = EquipAsync(pet.Type);
                }
            }, equipped ? Theme.Success : Theme.PanelLight, Theme.SmallSize - 4, Theme.Text);
            UIFactory.Anchor(equip.GetComponent<RectTransform>(), 0.67f, 0.03f, 0.97f, 0.24f);
        }

        private async Task SummonAsync(int count)
        {
            PetSummonResponse response = await Api(api => api.SummonPetsAsync(count));
            if (response == null || this == null)
            {
                return;
            }
            Apply(response.Pets, response.Wallet);
            await ShowPullsAsync(response.Pulls);
            Rebuild();
        }

        private async Task EquipAsync(string pet)
        {
            PetActionResponse response = await Api(api => api.EquipPetAsync(pet));
            if (response != null && this != null)
            {
                Game.Audio.PlaySFX(SoundIds.Click);
                Apply(response.Pets, response.Wallet);
                Rebuild();
            }
        }

        private async Task AwakenAsync(string pet)
        {
            PetActionResponse response = await Api(api => api.AwakenPetAsync(pet));
            if (response != null && this != null)
            {
                Game.Audio.PlaySFX(SoundIds.WinFanfare);
                Apply(response.Pets, response.Wallet);
                UI.Toast(Loc.T("pets.awakened", PetName(Loc, pet)), 2.5f);
                Rebuild();
            }
        }

        private void Apply(PetsDto pets, WalletDto wallet)
        {
            ProfileDto profile = Game.Backend.Profile;
            if (profile != null && pets != null)
            {
                profile.Pets = pets;
            }
            Game.Backend.ApplyWallet(wallet);
        }

        /// <summary>Reveal of the pulls: whole pets get a golden burst, fragments a small tile.</summary>
        private async Task ShowPullsAsync(List<PetPullDto> pulls)
        {
            RectTransform box = UI.Popup(0.12f, 0.88f);
            Text title = UIFactory.Label(box, Loc.T("pets.summonTitle"), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.05f, 0.88f, 0.95f, 0.98f);

            RectTransform grid = UIFactory.Anchor(UIFactory.Rect("Pulls", box), 0.04f, 0.16f, 0.96f, 0.87f);
            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = pulls.Count > 1 ? new Vector2(170, 230) : new Vector2(420, 520);
            layout.spacing = new Vector2(14, 14);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = pulls.Count > 1 ? 5 : 1;

            bool anyWhole = false;
            foreach (PetPullDto pull in pulls)
            {
                Image tile = UIFactory.Panel("Pull", grid, pull.WholePet && !pull.Duplicate ? Theme.GoldDark : Theme.PanelLight);
                if (pull.WholePet && !pull.Duplicate)
                {
                    anyWhole = true;
                    Image burst = UIFactory.Icon(tile.transform, ProceduralSprites.Glow(128), new Color(1f, 0.9f, 0.5f, 0.8f), 0);
                    UIFactory.Stretch(burst.rectTransform, -30, -30, -30, -30);
                    burst.gameObject.AddComponent<Pulse>();
                }
                Sprite art = PetArt(pull.Pet);
                if (art != null)
                {
                    Image image = UIFactory.Icon(tile.transform, art, Color.white, 0);
                    UIFactory.Anchor(image.rectTransform, 0.06f, 0.3f, 0.94f, 0.96f);
                }
                string text = pull.WholePet && !pull.Duplicate ? Loc.T("pets.new")
                    : pull.Duplicate ? Loc.T("pets.duplicate", pull.Fragments)
                    : Loc.T("pets.fragmentsGain", pull.Fragments);
                Text label = UIFactory.Label(tile.transform, text, pulls.Count > 1 ? Theme.SmallSize - 8 : Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(label.rectTransform, 0.02f, 0.02f, 0.98f, 0.3f);
            }

            Game.Audio.PlaySFX(anyWhole ? SoundIds.WinFanfare : SoundIds.Coins);
            var closed = new TaskCompletionSource<bool>();
            Button ok = UIFactory.Button(box, Loc.T("common.ok"), () => closed.TrySetResult(true));
            UIFactory.Anchor(ok.GetComponent<RectTransform>(), 0.3f, 0.03f, 0.7f, 0.13f);
            await closed.Task;
            if (box != null)
            {
                Destroy(box.parent.gameObject);
            }
        }
    }
}
