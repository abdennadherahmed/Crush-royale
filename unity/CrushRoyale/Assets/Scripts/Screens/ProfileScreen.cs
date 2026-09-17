using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Economy;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Player profile (yours, or a friend's with the player id as argument): the hero dressed with the equipped outfit,
    /// avatar frame and title, stats, the stackable cosmetic bonus, equipped items and the cosmetic collection.
    /// Cosmetics are equipped from here; friends see exactly what you equipped.
    /// </summary>
    public sealed class ProfileScreen : UIScreen
    {
        private static readonly CosmeticKind[] Slots = { CosmeticKind.AvatarFrame, CosmeticKind.Title, CosmeticKind.HeroOutfit, CosmeticKind.BoardSkin, CosmeticKind.PieceSkin };

        private PlayerStatsDto _stats;
        private string _playerId;
        private bool _loading;

        private bool IsMine => string.IsNullOrEmpty(_playerId) || _playerId == Game.Backend.PlayerId;

        protected override void Build()
        {
            _playerId = Args as string;
            RectTransform body = Frame("profile.title");
            RectTransform list = UIFactory.ScrollList(body, 22, 30);
            if (_stats == null)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T(Game.Backend.IsOnline ? "common.loading" : "error.offline"), Theme.BodySize, Theme.TextMuted), 160);
                return;
            }

            BuildHero(list);
            BuildStats(list);
            BuildBonus(list);
            BuildEquipped(list);
            BuildCollection(list);
        }

        public override async Task OnShownAsync()
        {
            if (_loading || !Game.Backend.IsOnline)
            {
                return;
            }
            _loading = true;
            string id = string.IsNullOrEmpty(_playerId) ? Game.Backend.PlayerId : _playerId;
            PlayerStatsDto stats = await Api(api => api.GetPlayerStatsAsync(id), loading: false);
            _loading = false;
            if (stats != null && this != null)
            {
                _stats = stats;
                Rebuild();
            }
        }

        // ------------------------------------------------------------------ hero

        private void BuildHero(Transform list)
        {
            Image panel = UIFactory.Panel("Hero", list, Theme.Panel);
            UIFactory.Height(panel, 820);
            UiKit.FramePanel(panel);
            RectTransform rect = panel.rectTransform;

            string gender = _stats.HeroGender ?? "female";
            string id = gender == "male" ? "hero" : "heroine";
            CrushRoyale.Core.Story.Kingdom backdrop = CrushRoyale.Core.Story.Kingdom.Central;
            RectTransform scene = UIFactory.Stretch(UIFactory.Rect("Scene", rect), 26, 26, 26, 26);
            scene.gameObject.AddComponent<RectMask2D>();
            Widgets.Backdrop(scene, backdrop, 0.55f);

            Image aura = UIFactory.Icon(scene, ProceduralSprites.Glow(128), CosmeticLook.OutfitAura(_stats.Outfit), 0);
            UIFactory.Anchor(aura.rectTransform, 0.1f, 0.05f, 0.9f, 0.85f);
            aura.gameObject.AddComponent<Pulse>();
            Sprite full = CosmeticLook.HeroFull(gender, _stats.Outfit);
            if (full != null)
            {
                Image hero = UIFactory.Icon(scene, full, Color.white, 0);
                hero.rectTransform.pivot = new Vector2(0.5f, 0f);
                UIFactory.Anchor(hero.rectTransform, 0.2f, 0.02f, 0.8f, 0.8f);
                hero.gameObject.AddComponent<Breathe>();
            }
            if (!string.IsNullOrEmpty(_stats.Pet))
            {
                Sprite pet = PetsScreen.PetArt(_stats.Pet);
                if (pet != null)
                {
                    Image petImage = UIFactory.Icon(scene, pet, Color.white, 0);
                    UIFactory.Anchor(petImage.rectTransform, 0.66f, 0.02f, 0.94f, 0.28f);
                    Breathe hop = petImage.gameObject.AddComponent<Breathe>();
                    hop.Amount = 0.06f;
                    hop.Speed = 3f;
                    Text level = UIFactory.Label(scene, Loc.T("pets.level", _stats.PetLevel), Theme.SmallSize - 6, new Color(0.7f, 1f, 0.8f), TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Anchor(level.rectTransform, 0.66f, 0.26f, 0.94f, 0.32f);
                    Widgets.TitleOutline(level);
                }
            }

            // Top: avatar in its frame, name, title, league, VIP.
            RectTransform avatar = CosmeticLook.Avatar(scene, ArtLibrary.Character(id), _stats.Profile.Frame, 190);
            avatar.anchorMin = avatar.anchorMax = new Vector2(0.14f, 0.86f);
            Text name = UIFactory.Label(scene, _stats.Profile.DisplayName, Theme.HeaderSize + 4, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.27f, 0.88f, 0.98f, 0.98f);
            Widgets.TitleOutline(name);
            if (!string.IsNullOrEmpty(_stats.Profile.Title))
            {
                RectTransform plate = CosmeticLook.TitlePlate(scene, Loc, _stats.Profile.Title, Theme.SmallSize);
                UIFactory.Anchor(plate, 0.26f, 0.8f, 0.8f, 0.885f);
            }
            string league = Loc.T("league." + _stats.Profile.League) + "  ·  " + Loc.Number(_stats.Profile.Trophies);
            Text leagueText = UIFactory.Label(scene, league, Theme.SmallSize + 2, Theme.League(_stats.Profile.League), TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(leagueText.rectTransform, 0.27f, 0.73f, 0.8f, 0.8f);
            Widgets.TitleOutline(leagueText);
            if (_stats.VipTier > 0)
            {
                Image vip = UIFactory.Panel("Vip", scene, Theme.GoldDark);
                UIFactory.Anchor(vip.rectTransform, 0.8f, 0.73f, 0.98f, 0.8f);
                Text vipText = UIFactory.Label(vip.transform, Loc.T("vip.badge", _stats.VipTier), Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(vipText.rectTransform);
                Widgets.TitleOutline(vipText);
            }
        }

        // ------------------------------------------------------------------ stats

        private void BuildStats(Transform list)
        {
            Widgets.SectionTitle(list, Loc.T("profile.stats"));
            int games = _stats.PvpWins + _stats.PvpLosses;
            var stats = new List<(string Icon, string Label, string Value)>
            {
                ("item_trophy", Loc.T("profile.trophies"), Loc.Number(_stats.Profile.Trophies)),
                ("item_crown", Loc.T("profile.bestLeague"), Loc.T("league." + _stats.HighestLeague)),
                ("item_medal", Loc.T("profile.wins"), Loc.Number(_stats.PvpWins)),
                ("item_bolt", Loc.T("profile.winRate"), games > 0 ? Mathf.RoundToInt(_stats.PvpWins * 100f / games) + " %" : "-"),
                ("item_xp", Loc.T("profile.bestStreak"), Loc.Number(_stats.BestWinStreak)),
                ("item_stars", Loc.T("profile.stars"), Loc.Number(_stats.TotalStars)),
                ("item_ticket", Loc.T("profile.stage"), Loc.Number(_stats.Profile.HighestStage)),
                ("item_trophy", Loc.T("profile.achievements"), Loc.Number(_stats.AchievementsUnlocked)),
                ("item_fragment", Loc.T("profile.pets"), Loc.Number(_stats.PetsOwned)),
                ("item_gift", Loc.T("profile.cosmetics"), Loc.Number(_stats.CosmeticsCounted))
            };
            if (_stats.WeeklyRank.HasValue)
            {
                stats.Add(("item_medal", Loc.T("profile.weeklyRank"), "#" + _stats.WeeklyRank.Value));
            }

            RectTransform gridRect = UIFactory.Rect("Stats", list);
            int rows = (stats.Count + 1) / 2;
            UIFactory.Height(gridRect, rows * 130 + (rows - 1) * 14);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(480, 130);
            grid.spacing = new Vector2(14, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            GridFit.On(grid);
            foreach ((string icon, string label, string value) in stats)
            {
                Image cell = UIFactory.Panel("Stat", gridRect, Theme.Panel);
                UiKit.CardFrame(cell);
                Sprite art = UiKit.Art(icon);
                if (art != null)
                {
                    Image image = UIFactory.Icon(cell.transform, art, Color.white, 0);
                    UIFactory.Anchor(image.rectTransform, 0.04f, 0.15f, 0.24f, 0.85f);
                }
                Text labelText = UIFactory.Label(cell.transform, label, Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.LowerLeft, FontStyle.Bold);
                UIFactory.Anchor(labelText.rectTransform, 0.27f, 0.52f, 0.97f, 0.92f);
                Text valueText = UIFactory.Label(cell.transform, value, Theme.BodySize + 2, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                UIFactory.Anchor(valueText.rectTransform, 0.27f, 0.06f, 0.97f, 0.54f);
                Widgets.TitleOutline(valueText);
            }
        }

        // ------------------------------------------------------------------ bonus

        private void BuildBonus(Transform list)
        {
            Image panel = UIFactory.Panel("Bonus", list, Theme.Panel);
            UIFactory.Height(panel, 300);
            UiKit.FramePanel(panel);
            Sprite pouch = UiKit.Art("item_pouch");
            if (pouch != null)
            {
                Image icon = UIFactory.Icon(panel.transform, pouch, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.04f, 0.3f, 0.22f, 0.86f);
                icon.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }
            Text title = UIFactory.Label(panel.transform, Loc.T("profile.bonusTitle", _stats.CosmeticsCounted), Theme.BodySize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.25f, 0.68f, 0.97f, 0.9f);
            Widgets.TitleOutline(title);
            Text values = UIFactory.Label(panel.transform, Loc.T("profile.bonusValues", FormatPercent(_stats.CosmeticCoinBonusPermille), FormatPercent(_stats.CosmeticPassXpBonusPermille)),
                Theme.HeaderSize - 6, Theme.Success, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(values.rectTransform, 0.25f, 0.42f, 0.97f, 0.68f);
            Widgets.TitleOutline(values);
            Text rule = UIFactory.Label(panel.transform, Loc.T("profile.bonusRule", CosmeticBonuses.MaxCoinPermille / 10), Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.UpperLeft);
            UIFactory.Anchor(rule.rectTransform, 0.05f, 0.06f, 0.97f, 0.4f);
        }

        private static string FormatPercent(int permille) => (permille / 10f).ToString(permille % 10 == 0 ? "0" : "0.0");

        // ------------------------------------------------------------------ equipped

        private void BuildEquipped(Transform list)
        {
            Widgets.SectionTitle(list, Loc.T("profile.equipped"));
            RectTransform row = UIFactory.Rect("Equipped", list);
            UIFactory.Height(row, 250);
            GridLayoutGroup grid = row.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(190, 250);
            grid.spacing = new Vector2(10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Slots.Length;
            GridFit.On(grid);
            foreach (CosmeticKind kind in Slots)
            {
                string equipped = EquippedOf(kind);
                Image cell = UIFactory.Panel("Slot", row, Theme.Panel);
                UiKit.CardFrame(cell);
                SlotIcon(cell.transform, kind, equipped, 0.15f, 0.36f, 0.85f, 0.9f);
                Text kindText = UIFactory.Label(cell.transform, KindName(kind), Theme.SmallSize - 8, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(kindText.rectTransform, 0.04f, 0.2f, 0.96f, 0.36f);
                Text nameText = UIFactory.Label(cell.transform, string.IsNullOrEmpty(equipped) ? "-" : CosmeticLook.Name(Loc, equipped), Theme.SmallSize - 8,
                    string.IsNullOrEmpty(equipped) ? Theme.TextMuted : CosmeticLook.Rarity(equipped), TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(nameText.rectTransform, 0.04f, 0.03f, 0.96f, 0.22f);
                Widgets.TitleOutline(nameText);
            }
        }

        private string EquippedOf(CosmeticKind kind)
        {
            switch (kind)
            {
                case CosmeticKind.AvatarFrame: return _stats.Profile.Frame;
                case CosmeticKind.Title: return _stats.Profile.Title;
                case CosmeticKind.HeroOutfit: return _stats.Outfit;
                case CosmeticKind.BoardSkin: return _stats.BoardSkin;
                default: return _stats.PieceSkin;
            }
        }

        private string KindName(CosmeticKind kind)
        {
            switch (kind)
            {
                case CosmeticKind.AvatarFrame: return Loc.T("profile.kind.frame");
                case CosmeticKind.Title: return Loc.T("profile.kind.title");
                case CosmeticKind.HeroOutfit: return Loc.T("profile.kind.outfit");
                case CosmeticKind.BoardSkin: return Loc.T("profile.kind.board");
                case CosmeticKind.PieceSkin: return Loc.T("profile.kind.pieces");
                default: return Loc.T("profile.kind.emote");
            }
        }

        /// <summary>Small visual preview of a cosmetic: frame ring, title ribbon, outfit aura, board tiles, gems, emote.</summary>
        private void SlotIcon(Transform parent, CosmeticKind kind, string id, float minX, float minY, float maxX, float maxY)
        {
            RectTransform box = UIFactory.Anchor(UIFactory.Rect("Preview", parent), minX, minY, maxX, maxY);
            box.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            switch (kind)
            {
                case CosmeticKind.AvatarFrame:
                {
                    Sprite frameArt = CosmeticLook.FrameArt(id);
                    if (frameArt != null)
                    {
                        Image art = UIFactory.Icon(box, frameArt, Color.white, 0);
                        UIFactory.Stretch(art.rectTransform);
                        break;
                    }
                    (Color main, Color accent) = CosmeticLook.Frame(id);
                    Image ring = UIFactory.Icon(box, ProceduralSprites.Circle(), main, 0);
                    UIFactory.Stretch(ring.rectTransform);
                    Image hole = UIFactory.Icon(box, ProceduralSprites.Circle(), accent, 0);
                    UIFactory.Stretch(hole.rectTransform, 8, 8, 8, 8);
                    Image inner = UIFactory.Icon(box, ProceduralSprites.Circle(), new Color(0.1f, 0.06f, 0.2f, 1f), 0);
                    UIFactory.Stretch(inner.rectTransform, 16, 16, 16, 16);
                    break;
                }
                case CosmeticKind.Title:
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        UiKit.Ribbon(box);
                        break;
                    }
                    RectTransform plate = CosmeticLook.TitlePlate(box, Loc, id, Theme.SmallSize - 8);
                    UIFactory.Anchor(plate, -0.2f, 0.25f, 1.2f, 0.75f);
                    break;
                }
                case CosmeticKind.HeroOutfit:
                {
                    Image aura = UIFactory.Icon(box, ProceduralSprites.Glow(128), CosmeticLook.OutfitAura(id), 0);
                    UIFactory.Stretch(aura.rectTransform, -10, -10, -10, -10);
                    Sprite dressed = CosmeticLook.HeroFull(_stats.HeroGender ?? "female", id);
                    if (dressed != null)
                    {
                        Image body = UIFactory.Icon(box, dressed, Color.white, 0);
                        UIFactory.Stretch(body.rectTransform);
                    }
                    break;
                }
                case CosmeticKind.BoardSkin:
                {
                    (Color frame, Color a, Color b) = CosmeticLook.Board(id);
                    Image back = UIFactory.Panel("Board", box, new Color(frame.r, frame.g, frame.b, 1f));
                    UIFactory.Stretch(back.rectTransform);
                    for (int i = 0; i < 9; i++)
                    {
                        Image tile = UIFactory.Panel("Tile", back.transform, i % 2 == 0 ? new Color(a.r, a.g, a.b, 0.6f) : new Color(b.r, b.g, b.b, 0.35f));
                        float x = i % 3 / 3f;
                        float y = i / 3 / 3f;
                        UIFactory.Anchor(tile.rectTransform, x + 0.03f, y + 0.03f, x + 0.3f, y + 0.3f);
                    }
                    break;
                }
                case CosmeticKind.PieceSkin:
                {
                    // Three gems of the skin in a small cluster.
                    string folder = ArtLibrary.GemSkinFolder(id);
                    var colors = new[] { CrushRoyale.Core.Board.PieceColor.Red, CrushRoyale.Core.Board.PieceColor.Blue, CrushRoyale.Core.Board.PieceColor.Yellow };
                    Vector2[] spots = { new Vector2(0.05f, 0.35f), new Vector2(0.5f, 0.35f), new Vector2(0.27f, 0f) };
                    for (int g = 0; g < 3; g++)
                    {
                        Sprite gem = ArtLibrary.Gem(colors[g], folder);
                        if (gem != null)
                        {
                            Image gemImage = UIFactory.Icon(box, gem, Color.white, 0);
                            UIFactory.Anchor(gemImage.rectTransform, spots[g].x, spots[g].y, spots[g].x + 0.48f, spots[g].y + 0.62f);
                        }
                    }
                    break;
                }
                default:
                {
                    Sprite star = UiKit.Art("item_stars");
                    if (star != null)
                    {
                        Image s = UIFactory.Icon(box, star, Color.white, 0);
                        UIFactory.Stretch(s.rectTransform);
                    }
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ collection

        private void BuildCollection(Transform list)
        {
            var owned = new HashSet<string>(_stats.Cosmetics ?? new List<string>());
            List<CosmeticDefinition> all = CosmeticCatalog.All.Where(c => c.Source != CosmeticSource.Default).ToList();
            foreach (string id in owned)
            {
                CosmeticDefinition def = CosmeticCatalog.Get(id);
                if (def != null && def.Source != CosmeticSource.Default && all.All(c => c.Id != id))
                {
                    all.Add(def);
                }
            }
            List<CosmeticDefinition> shown = IsMine ? all : all.Where(c => owned.Contains(c.Id)).ToList();
            shown = shown.OrderByDescending(c => owned.Contains(c.Id)).ThenBy(c => c.Kind).ThenByDescending(c => c.Rarity).ToList();

            Widgets.SectionTitle(list, Loc.T("profile.collection", owned.Count(id => CosmeticCatalog.Get(id)?.Source != CosmeticSource.Default), all.Count));
            RectTransform gridRect = UIFactory.Rect("Collection", list);
            int rows = Mathf.Max(1, (shown.Count + 2) / 3);
            UIFactory.Height(gridRect, rows * 330 + (rows - 1) * 14);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(310, 330);
            grid.spacing = new Vector2(14, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            GridFit.On(grid);

            foreach (CosmeticDefinition def in shown)
            {
                bool has = owned.Contains(def.Id);
                bool equipped = has && EquippedOf(def.Kind) == def.Id;
                Image cell = UIFactory.Panel("Cosmetic", gridRect, Theme.Panel);
                UiKit.CardFrame(cell);
                Color rarity = CosmeticLook.Rarity(def.Rarity);
                if (equipped)
                {
                    Outline glow = cell.gameObject.AddComponent<Outline>();
                    glow.effectColor = new Color(rarity.r, rarity.g, rarity.b, 0.95f);
                    glow.effectDistance = new Vector2(6, -6);
                }
                if (!has)
                {
                    cell.color = new Color(0.6f, 0.58f, 0.68f, 1f);
                }

                Image stripe = UIFactory.Panel("Rarity", cell.transform, rarity);
                UIFactory.Anchor(stripe.rectTransform, 0.08f, 0.9f, 0.92f, 0.96f);
                SlotIcon(cell.transform, def.Kind, def.Id, 0.2f, 0.45f, 0.8f, 0.88f);
                Text name = UIFactory.Label(cell.transform, CosmeticLook.Name(Loc, def.Id), Theme.SmallSize - 6, has ? rarity : Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, 0.05f, 0.3f, 0.95f, 0.45f);
                Widgets.TitleOutline(name);
                string buff = "+" + FormatPercent(CosmeticBonuses.CoinPermille(def.Rarity)) + "% " + Loc.T("profile.coinsShort")
                    + (CosmeticBonuses.PassXpPermille(def.Rarity) > 0 ? "  +" + FormatPercent(CosmeticBonuses.PassXpPermille(def.Rarity)) + "% XP" : string.Empty);
                Text buffText = UIFactory.Label(cell.transform, buff, Theme.SmallSize - 10, has ? Theme.Success : Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(buffText.rectTransform, 0.05f, 0.2f, 0.95f, 0.31f);

                if (!IsMine)
                {
                    continue;
                }
                if (equipped)
                {
                    Text on = UIFactory.Label(cell.transform, "✔ " + Loc.T("pets.equipped"), Theme.SmallSize - 6, Theme.Success, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Anchor(on.rectTransform, 0.05f, 0.03f, 0.95f, 0.19f);
                }
                else if (has && def.Kind != CosmeticKind.Emote)
                {
                    string id = def.Id;
                    Button equip = UIFactory.Button(cell.transform, Loc.T("shop.equip"), () => _ = EquipAsync(id), Theme.Gold, Theme.SmallSize - 4);
                    UIFactory.Anchor(equip.GetComponent<RectTransform>(), 0.12f, 0.03f, 0.88f, 0.19f);
                }
                else if (!has)
                {
                    Text source = UIFactory.Label(cell.transform, SourceHint(def), Theme.SmallSize - 10, Theme.Warning, TextAnchor.MiddleCenter, FontStyle.Bold);
                    UIFactory.Anchor(source.rectTransform, 0.05f, 0.03f, 0.95f, 0.19f);
                }
            }
        }

        private string SourceHint(CosmeticDefinition def)
        {
            switch (def.Source)
            {
                case CosmeticSource.Shop: return Loc.T("profile.source.shop");
                case CosmeticSource.Vip: return Loc.T("vip.badge", def.RequiredVip);
                case CosmeticSource.Achievement: return Loc.T("profile.source.achievements");
                case CosmeticSource.BattlePass: return Loc.T("profile.source.pass");
                default: return Loc.T("profile.source.story");
            }
        }

        private async Task EquipAsync(string id)
        {
            InventoryDto inventory = await Api(api => api.EquipAsync(id));
            if (inventory == null || this == null)
            {
                return;
            }
            Game.Backend.ApplyInventory(inventory);
            Game.Audio.PlaySFX(SoundIds.Sparkle);
            _stats.Profile.Frame = inventory.EquippedFrame;
            _stats.Profile.Title = inventory.EquippedTitle;
            _stats.Outfit = inventory.EquippedOutfit;
            _stats.BoardSkin = inventory.EquippedBoardSkin;
            _stats.PieceSkin = inventory.EquippedPieceSkin;
            Rebuild();
        }
    }
}
