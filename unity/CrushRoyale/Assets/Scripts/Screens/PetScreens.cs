using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Pets: summon with orbes (odds and pity shown before paying, as Google Play requires for random items), an egg
    /// reveal with suspense, the collection with fragment progress toward unlocking, levels, XP, equip and awakening.
    /// </summary>
    public sealed class PetsScreen : UIScreen
    {
        private RectTransform _list;

        /// <summary>
        /// A pet whose conversion popup should open as soon as this screen is built.
        ///
        /// The bag lists the fragments and offers "Convert" next to each one, but the trade window lives here. The
        /// bag used to merely open this screen, which left the player on a list with nothing happening -- they had
        /// pressed a button and the game answered with a different page. The bag now names the pet and the popup
        /// opens on arrival.
        /// </summary>
        public static string PendingConvert;

        protected override string BackdropScene => "pets";

        protected override CrushRoyale.Core.Story.Kingdom BackdropKingdom => CrushRoyale.Core.Story.Kingdom.West;

        public override Type BackTarget => typeof(MainMenuScreen);

        /// <summary>Sprite of a pet ("FrostFox" -> Art/Pets/frostfox), or the summon egg for null.</summary>
        public static Sprite PetArt(string type) => string.IsNullOrEmpty(type)
            ? UiKit.Art("egg_closed") ?? ArtLibrary.Load("Art/Pets/egg")
            : ArtLibrary.Load("Art/Pets/" + type.ToLowerInvariant());

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
            UIFactory.Anchor((RectTransform)bar.transform, 0.02f, 0.935f, 0.98f, 1f);

            PetsDto pets = Game.Backend.Profile?.Pets;
            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.925f);
            _list = UIFactory.ScrollList(holder, 20, 26);
            UIFactory.Stretch((RectTransform)_list.parent.parent);
            if (pets == null)
            {
                UIFactory.Height(UIFactory.Label(_list, Loc.T("error.offline"), Theme.BodySize, Theme.TextMuted), 160);
                return;
            }
            BuildSummon(_list, pets);
            Widgets.SectionTitle(_list, Loc.T("pets.collection"));
            foreach (PetDto pet in pets.Pets)
            {
                PetCard(pet, pets);
            }

            string pending = PendingConvert;
            PendingConvert = null;
            if (string.IsNullOrEmpty(pending))
            {
                return;
            }
            PetDto asked = pets.Pets.Find(p => p.Type == pending);
            if (asked != null && CanConvert(asked, pets))
            {
                ShowConvert(asked, pets);
            }
        }

        /// <summary>Surplus fragments of an owned pet can be traded, but only while some pet is still locked.</summary>
        private bool CanConvert(PetDto pet, PetsDto pets) =>
            pet.Owned && pet.Fragments >= ConvertRatio && pets.Pets.Exists(p => !p.Owned);

        private void BuildSummon(Transform list, PetsDto pets)
        {
            Image panel = UIFactory.Panel("Summon", list, Theme.Panel);
            UIFactory.Height(panel, 760);
            UiKit.FramePanel(panel);
            RectTransform rect = panel.rectTransform;

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", rect), 0.1f, 0.86f, 0.9f, 0.99f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, Loc.T("pets.summonTitle"), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
            Widgets.TitleOutline(title);

            Image glow = UIFactory.Icon(rect, ProceduralSprites.Glow(128), new Color(Theme.Orbe.r, Theme.Orbe.g, Theme.Orbe.b, 0.6f), 0);
            UIFactory.Anchor(glow.rectTransform, -0.02f, 0.36f, 0.42f, 0.9f);
            glow.gameObject.AddComponent<Pulse>();
            Sprite egg = PetArt(null);
            if (egg != null)
            {
                Image image = UIFactory.Icon(rect, egg, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.05f, 0.42f, 0.36f, 0.84f);
                image.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }

            // Odds, stated plainly: whole pet chance, what else drops, the unlock rule.
            float wholePercent = 100f / Mathf.Max(1, pets.WholePetOneIn);
            AddOddsLine(rect, "item_crown", Loc.T("pets.oddsWhole", wholePercent.ToString("0.0#"), pets.WholePetOneIn), 0.72f);
            AddOddsLine(rect, "item_fragment", Loc.T("pets.oddsFragments", Game.Backend.Balance.Pets.FragmentsMin, Game.Backend.Balance.Pets.FragmentsMax, pets.Pets.Count), 0.6f);
            AddOddsLine(rect, "item_lock", Loc.T("pets.oddsUnlock", Math.Max(1, pets.UnlockFragments)), 0.48f);

            // Pity counter.
            int left = Math.Max(1, pets.PityPulls - pets.PullsSinceWholePet);
            Text pity = UIFactory.Label(rect, Loc.T("pets.pity", left), Theme.SmallSize + 2, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(pity.rectTransform, 0.06f, 0.33f, 0.94f, 0.41f);
            Widgets.TitleOutline(pity);
            UIFactory.ProgressBar(rect, pets.PullsSinceWholePet / (float)Math.Max(1, pets.PityPulls), Theme.Gold, out RectTransform pityBar);
            UIFactory.Anchor(pityBar, 0.08f, 0.26f, 0.92f, 0.32f);

            Button one = UIFactory.Button(rect, Loc.T("pets.summon1", pets.SummonCostOrbes), () => _ = SummonAsync(1), Theme.PanelLight, Theme.BodySize);
            UIFactory.Anchor(one.GetComponent<RectTransform>(), 0.06f, 0.06f, 0.48f, 0.22f);
            Button ten = UIFactory.Button(rect, Loc.T("pets.summon10", pets.Summon10CostOrbes), () => _ = SummonAsync(10), Theme.Success, Theme.BodySize);
            UIFactory.Anchor(ten.GetComponent<RectTransform>(), 0.52f, 0.06f, 0.94f, 0.22f);
            ten.gameObject.AddComponent<Breathe>().Amount = 0.02f;
            AddOrbeIcon(one);
            AddOrbeIcon(ten);

            int saving = Mathf.RoundToInt(100f - pets.Summon10CostOrbes * 100f / Mathf.Max(1, pets.SummonCostOrbes * 10));
            if (saving > 0)
            {
                Image tag = UIFactory.Panel("Deal", ten.transform, Theme.Danger);
                UIFactory.Anchor(tag.rectTransform, 0.62f, 0.78f, 1.04f, 1.18f);
                Text tagText = UIFactory.Label(tag.transform, "-" + saving + "%", Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(tagText.rectTransform);
                tag.gameObject.AddComponent<Pulse>().Scale = true;
            }
        }

        private static void AddOddsLine(RectTransform rect, string icon, string text, float y)
        {
            Sprite art = UiKit.Art(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(rect, art, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.4f, y + 0.005f, 0.48f, y + 0.105f);
            }
            Text label = UIFactory.Label(rect, text, Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Anchor(label.rectTransform, 0.5f, y, 0.95f, y + 0.11f);
        }

        private static void AddOrbeIcon(Button button)
        {
            Sprite orb = UiKit.Art("item_orbs") ?? ArtLibrary.Icon("orb");
            if (orb == null)
            {
                return;
            }
            Image icon = UIFactory.Icon(button.transform, orb, Color.white, 0);
            UIFactory.Anchor(icon.rectTransform, -0.04f, 0.5f, 0.2f, 1.12f);
        }

        private void PetCard(PetDto pet, PetsDto pets)
        {
            bool equipped = pet.Type == pets.Equipped;
            Image panel = UIFactory.Panel("Pet", _list, Theme.Panel);
            UIFactory.Height(panel, 380);
            UiKit.CardFrame(panel);
            RectTransform card = panel.rectTransform;
            if (equipped)
            {
                Outline glow = panel.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                glow.effectDistance = new Vector2(6, -6);
            }

            Image halo = UIFactory.Icon(card, ProceduralSprites.Glow(128), pet.Owned ? new Color(Theme.Crystal.r, Theme.Crystal.g, Theme.Crystal.b, 0.45f) : new Color(0, 0, 0, 0), 0);
            UIFactory.Anchor(halo.rectTransform, -0.02f, 0f, 0.34f, 1f);
            Sprite art = PetArt(pet.Type);
            if (art != null)
            {
                Image image = UIFactory.Icon(card, art, pet.Owned ? Color.white : new Color(0.12f, 0.1f, 0.2f, 1f), 0);
                UIFactory.Anchor(image.rectTransform, 0.02f, 0.08f, 0.31f, 0.92f);
                if (pet.Owned)
                {
                    image.gameObject.AddComponent<Breathe>().Amount = 0.03f;
                }
            }

            Text name = UIFactory.Label(card, PetName(Loc, pet.Type), Theme.BodySize + 4, pet.Owned ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.33f, 0.8f, 0.8f, 0.96f);
            Widgets.TitleOutline(name);

            if (pet.Owned)
            {
                RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Level", card), 0.82f, 0.72f, 0.98f, 0.98f);
                medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                UiKit.RoundBadge(medal, crystal: true);
                Text level = UIFactory.Label(medal, pet.Level.ToString(), Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(level.rectTransform);
                Widgets.TitleOutline(level);

                Text detail = UIFactory.Label(card, Loc.T("pets.chance", pet.AutoMovePercent) + "  ·  " + Loc.T("pets.fragments", pet.Fragments), Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft);
                UIFactory.Anchor(detail.rectTransform, 0.33f, 0.66f, 0.98f, 0.8f);

                bool max = pet.Level >= pets.MaxLevel;
                float progress = pet.XpForNext > pet.XpForCurrent ? Mathf.Clamp01((pet.Xp - pet.XpForCurrent) / (float)(pet.XpForNext - pet.XpForCurrent)) : 1f;
                UIFactory.ProgressBar(card, max ? 1f : progress, Theme.Crystal, out RectTransform xpBar);
                UIFactory.Anchor(xpBar, 0.33f, 0.53f, 0.97f, 0.64f);
                Text xp = UIFactory.Label(xpBar, max ? Loc.T("pets.maxLevel") : Loc.T("pets.xp", pet.Xp - pet.XpForCurrent, pet.XpForNext - pet.XpForCurrent), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(xp.rectTransform);
                Widgets.TitleOutline(xp);
            }
            else
            {
                // Locked: how close the fragments are to unlocking it.
                int need = Math.Max(1, pets.UnlockFragments);
                Text detail = UIFactory.Label(card, Loc.T("pets.lockedHint", need), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                UIFactory.Anchor(detail.rectTransform, 0.33f, 0.66f, 0.98f, 0.8f);
                UIFactory.ProgressBar(card, pet.Fragments / (float)need, Theme.Orbe, out RectTransform fragBar);
                UIFactory.Anchor(fragBar, 0.33f, 0.53f, 0.97f, 0.64f);
                Text count = UIFactory.Label(fragBar, Loc.T("pets.fragmentProgress", Math.Min(pet.Fragments, need), need), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(count.rectTransform);
                Widgets.TitleOutline(count);
                Sprite fragment = UiKit.Art("item_fragment");
                if (fragment != null)
                {
                    Image fragIcon = UIFactory.Icon(fragBar, fragment, Color.white, 0);
                    UIFactory.Anchor(fragIcon.rectTransform, -0.06f, -0.3f, 0.06f, 1.3f);
                }
            }

            Sprite giftArt = Enum.TryParse(pet.PowerUp, out CrushRoyale.Core.Config.PowerUpType gift) ? ArtLibrary.PowerUp(gift) : null;
            if (giftArt != null)
            {
                Image giftIcon = UIFactory.Icon(card, giftArt, Color.white, 0);
                UIFactory.Anchor(giftIcon.rectTransform, 0.33f, 0.34f, 0.41f, 0.5f);
            }
            Text giftLabel = UIFactory.Label(card, Loc.T("pets.gift", pets.MaxLevel, Loc.T("powerup." + pet.PowerUp)), Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(giftLabel.rectTransform, 0.42f, 0.34f, 0.98f, 0.5f);

            if (!pet.Owned)
            {
                bool ready = pet.Fragments >= pets.UnlockFragments;
                Button unlock = UIFactory.Button(card, Loc.T(ready ? "pets.unlock" : "pets.getFragments"), () =>
                {
                    if (ready)
                    {
                        _ = UnlockAsync(pet.Type);
                    }
                    else
                    {
                        _list.parent.parent.GetComponent<ScrollRect>().verticalNormalizedPosition = 1f;
                        UI.Toast(Loc.T("pets.getFragmentsToast"));
                    }
                }, ready ? Theme.Success : Theme.PanelLight, Theme.SmallSize);
                UIFactory.Anchor(unlock.GetComponent<RectTransform>(), 0.33f, 0.05f, 0.97f, 0.3f);
                if (ready)
                {
                    unlock.gameObject.AddComponent<Breathe>().Amount = 0.03f;
                }
                return;
            }
            // Surplus fragments of an owned pet can be traded for fragments of a pet still locked.
            bool canConvert = CanConvert(pet, pets);
            float convertMax = pet.CanAwaken ? 0.54f : 0.64f;
            if (pet.CanAwaken)
            {
                Button awaken = UIFactory.Button(card, Loc.T("pets.awaken", pet.AwakenFragments, pet.AwakenCoins), () => _ = AwakenAsync(pet.Type),
                    pet.Fragments >= pet.AwakenFragments ? Theme.Orbe : Theme.PanelLight, Theme.SmallSize - 8);
                UIFactory.Anchor(awaken.GetComponent<RectTransform>(), canConvert ? 0.555f : 0.33f, 0.05f, canConvert ? 0.765f : 0.64f, 0.3f);
            }
            if (canConvert)
            {
                PetDto source = pet;
                Button convert = UIFactory.Button(card, Loc.T("pets.convert"), () => ShowConvert(source, pets), Theme.Crystal, Theme.SmallSize - 6);
                UIFactory.Anchor(convert.GetComponent<RectTransform>(), 0.33f, 0.05f, convertMax, 0.3f);
                Pulse pulse = convert.gameObject.AddComponent<Pulse>();
                pulse.Scale = true;
            }
            Button equip = UIFactory.Button(card, Loc.T(equipped ? "pets.equipped" : "pets.equip"), () =>
            {
                if (!equipped)
                {
                    _ = EquipAsync(pet.Type);
                }
            }, equipped ? Theme.Success : Theme.Gold, Theme.SmallSize);
            UIFactory.Anchor(equip.GetComponent<RectTransform>(), pet.CanAwaken && canConvert ? 0.78f : 0.67f, 0.05f, 0.97f, 0.3f);
        }

        private int ConvertRatio => Math.Max(1, Game.Backend.Balance?.Pets?.ConvertRatio ?? 3);

        /// <summary>Trade popup: pick a locked pet and how many of its fragments to receive (ConvertRatio surplus fragments each).</summary>
        private void ShowConvert(PetDto source, PetsDto pets)
        {
            List<PetDto> locked = pets.Pets.FindAll(p => !p.Owned);
            if (locked.Count == 0)
            {
                return;
            }
            int ratio = ConvertRatio;
            int max = source.Fragments / ratio;
            PetDto target = locked.OrderByDescending(p => p.Fragments).First();
            int count = 1;

            RectTransform box = UI.Popup(0.14f, 0.86f);
            void Close()
            {
                if (box != null)
                {
                    Destroy(box.parent.gameObject);
                }
            }

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", box), -0.03f, 0.9f, 1.03f, 1.03f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, Loc.T("pets.convertTitle"), Theme.HeaderSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
            Widgets.TitleOutline(title);

            // From -> to strip.
            Image fromArt = UIFactory.Icon(box, PetArt(source.Type), Color.white, 0);
            UIFactory.Anchor(fromArt.rectTransform, 0.06f, 0.7f, 0.3f, 0.88f);
            fromArt.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            Text fromText = UIFactory.Label(box, Loc.T("pets.fragments", source.Fragments), Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(fromText.rectTransform, 0.02f, 0.655f, 0.34f, 0.705f);
            Widgets.TitleOutline(fromText);
            Text arrow = UIFactory.Label(box, Loc.T("pets.convertRate", ratio), Theme.BodySize, Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(arrow.rectTransform, 0.32f, 0.72f, 0.68f, 0.86f);
            Widgets.TitleOutline(arrow);
            arrow.gameObject.AddComponent<Pulse>();
            Image toArt = UIFactory.Icon(box, PetArt(target.Type), new Color(0.2f, 0.18f, 0.32f, 1f), 0);
            UIFactory.Anchor(toArt.rectTransform, 0.7f, 0.7f, 0.94f, 0.88f);
            Text toText = UIFactory.Label(box, string.Empty, Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(toText.rectTransform, 0.66f, 0.655f, 0.98f, 0.705f);
            Widgets.TitleOutline(toText);

            // Locked pets to choose from.
            Text pick = UIFactory.Label(box, Loc.T("pets.convertPick"), Theme.SmallSize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(pick.rectTransform, 0.07f, 0.6f, 0.93f, 0.65f);
            RectTransform choices = UIFactory.Anchor(UIFactory.Rect("Choices", box), 0.06f, 0.4f, 0.94f, 0.595f);
            HorizontalLayoutGroup row = choices.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = true;
            var tiles = new List<(PetDto Pet, Image Tile)>();

            Text amount = UIFactory.Label(box, string.Empty, Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(amount.rectTransform, 0.28f, 0.25f, 0.72f, 0.36f);
            Widgets.TitleOutline(amount);
            Button go = null;

            void Refresh()
            {
                count = Mathf.Clamp(count, 1, Math.Max(1, max));
                int after = target.Fragments + count;
                toArt.sprite = PetArt(target.Type);
                toText.text = PetName(Loc, target.Type) + "  " + Loc.T("pets.fragmentProgress", Math.Min(after, pets.UnlockFragments), pets.UnlockFragments);
                amount.text = Loc.T("pets.convertAmount", count * ratio, count);
                foreach ((PetDto p, Image tile) in tiles)
                {
                    tile.color = p == target ? Theme.Crystal : Theme.PanelLight;
                }
                if (go != null)
                {
                    go.interactable = max >= 1;
                }
            }

            foreach (PetDto candidate in locked)
            {
                PetDto choice = candidate;
                Image tile = UIFactory.Panel("Tile", choices, Theme.PanelLight);
                UiKit.CardFrame(tile);
                Button tap = tile.gameObject.AddComponent<Button>();
                tap.targetGraphic = tile;
                tap.onClick.AddListener(() =>
                {
                    Game.Audio.PlaySFX(SoundIds.Click);
                    target = choice;
                    Refresh();
                });
                Image art = UIFactory.Icon(tile.transform, PetArt(choice.Type), new Color(0.35f, 0.32f, 0.5f, 1f), 0);
                UIFactory.Anchor(art.rectTransform, 0.1f, 0.3f, 0.9f, 0.95f);
                art.raycastTarget = false;
                Text frag = UIFactory.Label(tile.transform, choice.Fragments + "/" + pets.UnlockFragments, Theme.SmallSize - 8, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(frag.rectTransform, 0f, 0.02f, 1f, 0.3f);
                Widgets.TitleOutline(frag);
                tiles.Add((choice, tile));
            }

            // Amount: - / + / max.
            Button minus = UIFactory.Button(box, "-", () => { count--; Refresh(); }, Theme.PanelLight, Theme.HeaderSize);
            UIFactory.Anchor(minus.GetComponent<RectTransform>(), 0.07f, 0.25f, 0.26f, 0.36f);
            Button plus = UIFactory.Button(box, "+", () => { count++; Refresh(); }, Theme.PanelLight, Theme.HeaderSize);
            UIFactory.Anchor(plus.GetComponent<RectTransform>(), 0.74f, 0.25f, 0.93f, 0.36f);
            Button all = UIFactory.Button(box, Loc.T("pets.convertMax", max), () => { count = max; Refresh(); }, Theme.Orbe, Theme.SmallSize - 4);
            UIFactory.Anchor(all.GetComponent<RectTransform>(), 0.3f, 0.155f, 0.7f, 0.235f);

            Button cancel = UIFactory.Button(box, Loc.T("common.cancel"), Close, Theme.PanelLight, Theme.BodySize);
            UIFactory.Anchor(cancel.GetComponent<RectTransform>(), 0.06f, 0.03f, 0.46f, 0.13f);
            go = UIFactory.Button(box, Loc.T("pets.convertGo"), () =>
            {
                string to = target.Type;
                int n = count;
                Close();
                _ = ConvertAsync(source.Type, to, n);
            }, Theme.Success, Theme.BodySize);
            UIFactory.Anchor(go.GetComponent<RectTransform>(), 0.54f, 0.03f, 0.94f, 0.13f);
            go.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            Refresh();
        }

        private async Task ConvertAsync(string from, string to, int count)
        {
            PetActionResponse response = await Api(api => api.ConvertPetFragmentsAsync(from, to, count));
            if (response == null || this == null)
            {
                return;
            }
            Game.Audio.PlaySFX(SoundIds.Sparkle);
            Apply(response.Pets, response.Wallet);
            Game.Telemetry.Track("pet_convert", ("from", from), ("to", to), ("count", count));
            UI.Toast(Loc.T("pets.converted", count, PetName(Loc, to)), 2.5f);
            Rebuild();
        }

        private async Task SummonAsync(int count)
        {
            PetSummonResponse response = await Api(api => api.SummonPetsAsync(count));
            if (response == null || this == null)
            {
                return;
            }
            Apply(response.Pets, response.Wallet);
            Game.Telemetry.Track("pet_summon", ("count", count), ("whole", response.Pulls.FindAll(p => p.WholePet && !p.Duplicate).Count));

            var items = new List<RevealItem>();
            foreach (PetPullDto pull in response.Pulls)
            {
                items.Add(new RevealItem
                {
                    Art = PetArt(pull.Pet),
                    Rare = pull.WholePet,
                    Caption = pull.WholePet && !pull.Duplicate ? Loc.T("pets.new") + "\n" + PetName(Loc, pull.Pet)
                        : pull.Duplicate ? Loc.T("pets.duplicate", pull.Fragments)
                        : Loc.T("pets.fragmentsGain", pull.Fragments)
                });
            }
            await RevealOverlay.PlaySummonAsync(items, UiKit.Art("egg_closed") ?? PetArt(null), UiKit.Art("egg_cracked"), UiKit.Art("egg_open"), Loc.T("pets.summonTitle"));
            if (this != null)
            {
                Rebuild();
            }
        }

        private async Task UnlockAsync(string pet)
        {
            PetActionResponse response = await Api(api => api.UnlockPetAsync(pet));
            if (response == null || this == null)
            {
                return;
            }
            Apply(response.Pets, response.Wallet);
            Game.Telemetry.Track("pet_unlock", ("pet", pet));
            var items = new List<RevealItem> { new RevealItem { Art = PetArt(pet), Rare = true, Caption = Loc.T("pets.new") + "\n" + PetName(Loc, pet) } };
            await RevealOverlay.PlaySummonAsync(items, UiKit.Art("egg_closed") ?? PetArt(null), UiKit.Art("egg_cracked"), UiKit.Art("egg_open"), Loc.T("pets.unlockTitle"));
            if (this != null)
            {
                Rebuild();
            }
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
    }
}
