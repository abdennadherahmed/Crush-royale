using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Social;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>
    /// Guild hall: an illustrated banner (crest, level, members, trophies, rank, level progress), then four tabs:
    /// members (role badges, contributions, management), donations and tech tree (daily coin allowance + unlimited orbes),
    /// the weekly boss (illustrated, HP bar, damage podium) and a bubble chat. Without a guild: create or join one.
    /// </summary>
    public sealed class GuildScreen : UIScreen
    {
        private static readonly string[] MemberTabs = { "guild.tab.members", "guild.tab.upgrades", "guild.tab.boss", "guild.tab.chat" };
        private static readonly string[] Techs = { "CoinBonus", "BossDamage", "LifeRecharge", "MaxLives", "PowerUpDiscount", "BattlePassXp", "ChestSpeed", "PetXp" };

        private GuildDto _guild;
        private GuildSearchResponse _search;
        private int _tab;
        private bool _loaded;
        private RectTransform _content;
        private RectTransform _chatList;
        private InputField _chatInput;
        private InputField _searchInput;

        protected override string BackdropScene => "guild";

        protected override CrushRoyale.Core.Story.Kingdom BackdropKingdom => CrushRoyale.Core.Story.Kingdom.South;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("guild.title");
            if (!_loaded)
            {
                Text loading = Widgets.Loading(body, Loc);
                UIFactory.Stretch(loading.rectTransform);
                return;
            }
            if (_guild == null)
            {
                BuildNoGuild(body);
            }
            else
            {
                BuildGuild(body);
            }
        }

        public override async Task OnShownAsync()
        {
            await LoadAsync();
        }

        public override void OnHidden()
        {
            if (Game.Backend.Client != null)
            {
                Game.Backend.ChatMessageReceived -= OnChatMessage;
                Game.Backend.Client.Chat.Disconnect();
            }
        }

        private async Task LoadAsync()
        {
            ProfileDto profile = Game.Backend.Profile;
            if (profile?.GuildId != null)
            {
                _guild = await Api(api => api.GetMyGuildAsync());
                await Game.Backend.RefreshProfileAsync();
            }
            else
            {
                _guild = null;
                _search = await Api(api => api.SearchGuildsAsync(string.Empty), loading: false);
            }
            if (this == null)
            {
                return;
            }
            _loaded = true;

            if (_guild != null && Game.Backend.Client != null)
            {
                Game.Backend.ChatMessageReceived -= OnChatMessage;
                Game.Backend.ChatMessageReceived += OnChatMessage;
                Game.Backend.Client.Chat.Connect(_guild.Id);
            }
            Rebuild();
        }

        // ------------------------------------------------------------------ shared pieces

        /// <summary>Guild crest: a gold medallion with the guild emblem and the level on a ribbon underneath.</summary>
        private static RectTransform Crest(Transform parent, int level, Localization loc)
        {
            RectTransform crest = UIFactory.Rect("Crest", parent);
            Image halo = UIFactory.Icon(crest, ProceduralSprites.Glow(128), new Color(1f, 0.8f, 0.3f, 0.55f), 0);
            UIFactory.Stretch(halo.rectTransform, -40, -40, -40, -40);
            halo.raycastTarget = false;
            halo.gameObject.AddComponent<Pulse>();
            RectTransform medal = UIFactory.Stretch(UIFactory.Rect("Medal", crest));
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            UiKit.RoundBadge(medal, crystal: false);
            Sprite emblem = ArtLibrary.Icon("guild");
            if (emblem != null)
            {
                Image icon = UIFactory.Icon(medal, emblem, Color.white, 0);
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                UIFactory.Anchor(icon.rectTransform, 0.2f, 0.22f, 0.8f, 0.82f);
            }
            Image plate = UIFactory.Panel("Level", crest, Theme.GoldDark);
            UIFactory.Anchor(plate.rectTransform, 0.12f, -0.08f, 0.88f, 0.16f);
            Text text = UIFactory.Label(plate.transform, loc.T("guild.levelShort", level), Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(text.rectTransform);
            Widgets.TitleOutline(text);
            return crest;
        }

        private static void Chip(Transform parent, string art, string value, float x0, float x1, float y0, float y1)
        {
            Image chip = UIFactory.Panel("Chip", parent, new Color(0.05f, 0.03f, 0.12f, 0.7f));
            UIFactory.Anchor(chip.rectTransform, x0, y0, x1, y1);
            Sprite sprite = UiKit.Art(art);
            if (sprite != null)
            {
                Image icon = UIFactory.Icon(chip.transform, sprite, Color.white, 0);
                icon.preserveAspect = true;
                UIFactory.Anchor(icon.rectTransform, 0.02f, 0.05f, 0.3f, 0.95f);
            }
            Text text = UIFactory.Label(chip.transform, value, Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Theme.SmallSize - 10;
            text.resizeTextMaxSize = Theme.SmallSize + 2;
            UIFactory.Anchor(text.rectTransform, 0.33f, 0f, 0.98f, 1f);
            Widgets.TitleOutline(text);
        }

        private static Text Outlined(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Text label = UIFactory.Label(parent, text, size, color, align, FontStyle.Bold);
            Widgets.TitleOutline(label);
            return label;
        }

        /// <summary>Round portrait with the member's initial (gold for the leader, silver for officers).</summary>
        private static void Initial(Transform parent, string name, string role, float x0, float y0, float x1, float y1)
        {
            // The aspect fitter works inside its own area (on the card itself it filled the whole card).
            RectTransform area = UIFactory.Anchor(UIFactory.Rect("AvatarArea", parent), x0, y0, x1, y1);
            RectTransform holder = UIFactory.Stretch(UIFactory.Rect("Avatar", area));
            holder.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            UiKit.RoundBadge(holder, crystal: role != "Leader");
            string letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
            Text text = UIFactory.Label(holder, letter, Theme.HeaderSize, role == "Leader" ? Theme.Gold : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(text.rectTransform);
            Widgets.TitleOutline(text);
        }

        // ------------------------------------------------------------------ without a guild

        private void BuildNoGuild(RectTransform body)
        {
            RectTransform list = UIFactory.ScrollList(body, 22, 30);

            // Invitation banner.
            Image hero = UIFactory.Panel("Invite", list, Theme.Panel);
            UIFactory.Height(hero, 420);
            UiKit.FramePanel(hero);
            RectTransform scene = UIFactory.Stretch(UIFactory.Rect("Scene", hero.rectTransform), 24, 24, 24, 24);
            scene.gameObject.AddComponent<RectMask2D>();
            Widgets.Backdrop(scene, CrushRoyale.Core.Story.Kingdom.Central, 0.5f);
            RectTransform crest = UIFactory.Anchor(Crest(scene, 1, Loc), 0.05f, 0.25f, 0.36f, 0.85f);
            crest.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            Text pitch = Outlined(scene, Loc.T("guild.pitchTitle"), Theme.HeaderSize, Theme.Gold);
            UIFactory.Anchor(pitch.rectTransform, 0.4f, 0.58f, 0.97f, 0.9f);
            Text why = UIFactory.Label(scene, Loc.T("guild.pitchBody"), Theme.SmallSize, Theme.Text, TextAnchor.UpperLeft);
            UIFactory.Anchor(why.rectTransform, 0.4f, 0.08f, 0.97f, 0.56f);

            // Create.
            Image create = UIFactory.Panel("Create", list, Theme.Panel);
            UIFactory.Height(create, 470);
            UiKit.CardFrame(create);
            Text createTitle = Outlined(create.transform, Loc.T("guild.create"), Theme.BodySize + 2, Theme.Gold);
            UIFactory.Anchor(createTitle.rectTransform, 0.05f, 0.8f, 0.95f, 0.96f);
            InputField name = Widgets.Input(create.transform, Loc.T("guild.namePlaceholder"), 20);
            UIFactory.Anchor(name.GetComponent<RectTransform>(), 0.05f, 0.56f, 0.95f, 0.77f);
            InputField description = Widgets.Input(create.transform, Loc.T("guild.descriptionPlaceholder"), 200);
            UIFactory.Anchor(description.GetComponent<RectTransform>(), 0.05f, 0.31f, 0.95f, 0.52f);
            Button createButton = UIFactory.Button(create.transform, Loc.T("guild.createButton", Game.Backend.Balance.Guild.CreateCostCoins), () => _ = CreateAsync(name.text, description.text), Theme.Gold);
            UIFactory.Anchor(createButton.GetComponent<RectTransform>(), 0.2f, 0.05f, 0.8f, 0.26f);

            // Suggestions first: a new player lands on guilds that are actually recruiting, without typing anything.
            Widgets.SectionTitle(list, Loc.T(string.IsNullOrEmpty(_searchInput?.text) ? "guild.suggested" : "guild.search"));
            if (string.IsNullOrEmpty(_searchInput?.text))
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("guild.suggestedHelp"), Theme.SmallSize, Theme.TextMuted), 60);
            }
            HorizontalLayoutGroup row = UIFactory.Row(list, 120, 16);
            _searchInput = Widgets.Input(row.transform, Loc.T("guild.searchPlaceholder"), 20);
            UIFactory.Width(UIFactory.Button(row.transform, Loc.T("common.search"), () => _ = SearchAsync(_searchInput.text), Theme.PanelLight, Theme.BodySize, Theme.Text), 260);

            int index = 0;
            foreach (GuildSummaryDto g in _search?.Guilds ?? new List<GuildSummaryDto>())
            {
                Image card = UIFactory.Panel("Guild", list, Theme.Panel);
                UIFactory.Height(card, 240);
                UiKit.CardFrame(card);
                card.gameObject.AddComponent<PopIn>().Delay = Mathf.Min(0.5f, index++ * 0.05f);
                UIFactory.Anchor(Crest(card.transform, g.Level, Loc), 0.03f, 0.2f, 0.2f, 0.85f);
                Text title = Outlined(card.transform, g.Name, Theme.BodySize + 2, Theme.Text);
                UIFactory.Anchor(title.rectTransform, 0.24f, 0.6f, 0.7f, 0.92f);
                Chip(card.transform, "item_crown", g.Members + "/" + Game.Backend.Balance.Guild.MaxMembers, 0.24f, 0.44f, 0.36f, 0.56f);
                Chip(card.transform, "item_trophy", Loc.Number(g.TotalTrophies), 0.46f, 0.7f, 0.36f, 0.56f);
                Text min = UIFactory.Label(card.transform, Loc.T("guild.minTrophies", g.MinTrophies), Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.MiddleLeft);
                UIFactory.Anchor(min.rectTransform, 0.24f, 0.1f, 0.7f, 0.32f);
                long id = g.Id;
                Button join = UIFactory.Button(card.transform, Loc.T(g.IsOpen ? "guild.join" : "guild.closed"), () => _ = JoinAsync(id), g.IsOpen ? Theme.Success : Theme.PanelLight, Theme.SmallSize);
                UIFactory.Anchor(join.GetComponent<RectTransform>(), 0.72f, 0.25f, 0.97f, 0.75f);
                join.interactable = g.IsOpen;
            }
        }

        private async Task CreateAsync(string name, string description)
        {
            GuildDto guild = await Api(api => api.CreateGuildAsync(new CreateGuildRequest { Name = name, Description = description, IsOpen = true }));
            if (guild != null)
            {
                await Game.Backend.RefreshProfileAsync();
                await LoadAsync();
            }
        }

        private async Task SearchAsync(string query)
        {
            GuildSearchResponse search = await Api(api => api.SearchGuildsAsync(query));
            if (search != null)
            {
                _search = search;
                Rebuild();
            }
        }

        private async Task JoinAsync(long guildId)
        {
            GuildDto guild = await Api(api => api.JoinGuildAsync(guildId));
            if (guild != null)
            {
                await Game.Backend.RefreshProfileAsync();
                await LoadAsync();
            }
        }

        // ------------------------------------------------------------------ member view

        private void BuildGuild(RectTransform body)
        {
            BuildBanner(body);

            RectTransform tabs = UIFactory.Anchor(UIFactory.Rect("Tabs", body), 0.02f, 0.715f, 0.98f, 0.78f);
            // No layout group on the holder. It has exactly one child -- the tab row -- and a VerticalLayoutGroup
            // that does not control width overwrites that row's stretch anchors with a point and never gives it a
            // size, so the row collapsed to nothing and every tab label was zero pixels wide. The row stretches
            // itself; the holder only has to stay out of its way.
            Action<int> setTab = Widgets.Tabs(tabs, MemberTabs.Select(k => Loc.T(k)).ToList(), index =>
            {
                Game.Audio.PlaySFX(SoundIds.Click);
                _tab = index;
                Rebuild();
            });
            setTab(_tab);

            _content = UIFactory.Anchor(UIFactory.Rect("Content", body), 0, 0, 1, 0.71f);
            switch (_tab)
            {
                case 0: BuildMembers(); break;
                case 1: BuildUpgrades(); break;
                case 2: BuildBoss(); break;
                default: BuildChat(); break;
            }
        }

        /// <summary>
        /// The weekly race against the other guilds. A guild under the member floor still scores and still sees its
        /// score, but is not ranked: showing the points it is already piling up is what makes recruiting feel urgent
        /// rather than a chore.
        /// </summary>
        private void BuildTournament(Transform list)
        {
            GuildTournamentDto race = _guild?.Tournament;
            if (race == null)
            {
                return;
            }
            Widgets.SectionTitle(list, Loc.T("guild.race"));

            Image card = UIFactory.Panel("Race", list, Theme.Panel);
            UIFactory.Height(card, race.Ranked ? 250 : 300);
            UiKit.CardFrame(card);

            Text score = Outlined(card.transform, Loc.Number(race.Points), Theme.TitleSize, Theme.Gold, TextAnchor.MiddleLeft);
            UIFactory.Anchor(score.rectTransform, 0.05f, race.Ranked ? 0.52f : 0.6f, 0.55f, 0.92f);
            Text caption = Outlined(card.transform, Loc.T("guild.racePoints"), Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(caption.rectTransform, 0.05f, race.Ranked ? 0.34f : 0.46f, 0.55f, 0.52f);

            Chip(card.transform, "item_trophy", Loc.T("guild.racePrize", Loc.Number(race.FirstPrizeCoins), Loc.Number(race.FirstPrizeOrbes)),
                0.56f, 0.97f, race.Ranked ? 0.62f : 0.7f, 0.9f);

            // What the player personally brought, against the cap: a member who has hit it should go help elsewhere.
            float mine = race.MemberCap > 0 ? Mathf.Clamp01(race.MyPoints / (float)race.MemberCap) : 0f;
            UIFactory.ProgressBar(card.transform, mine, Theme.Crystal, out RectTransform bar);
            UIFactory.Anchor(bar, 0.05f, race.Ranked ? 0.12f : 0.3f, 0.95f, race.Ranked ? 0.3f : 0.46f);
            Text mineText = Outlined(bar, Loc.T("guild.raceMine", Loc.Number(race.MyPoints), Loc.Number(race.MemberCap)),
                Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Stretch(mineText.rectTransform);

            if (race.Ranked)
            {
                string standing = race.Rank > 0 ? Loc.T("guild.raceRank", race.Rank) : Loc.T("guild.raceEntered");
                Text ok = Outlined(card.transform, standing, Theme.SmallSize - 4, Theme.Success, TextAnchor.MiddleCenter);
                UIFactory.Anchor(ok.rectTransform, 0.05f, 0.02f, 0.95f, 0.11f);
                BuildRaceBoard(list, race);
                return;
            }

            Image warn = UIFactory.Panel("Missing", card.transform, new Color(0.35f, 0.06f, 0.12f, 0.85f));
            UIFactory.Anchor(warn.rectTransform, 0.04f, 0.04f, 0.96f, 0.28f);
            Text need = Outlined(warn.transform, Loc.T("guild.raceNeedMembers", race.MembersMissing, race.MinMembers),
                Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Stretch(need.rectTransform, 12, 6, 12, 6);
            need.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        /// <summary>The guilds ahead of us, so the race has faces and not just a number.</summary>
        private void BuildRaceBoard(Transform list, GuildTournamentDto race)
        {
            if (race.Board == null || race.Board.Count == 0)
            {
                return;
            }
            Color[] medals = { Theme.Gold, new Color(0.78f, 0.82f, 0.9f), new Color(0.8f, 0.52f, 0.28f) };
            int shown = Mathf.Min(race.Board.Count, 10);
            for (int i = 0; i < shown; i++)
            {
                GuildRaceEntryDto entry = race.Board[i];
                Image row = UIFactory.Panel("Race" + entry.Rank, list, entry.Mine ? Theme.PanelLight : Theme.Panel);
                UIFactory.Height(row, 96);
                if (entry.Mine)
                {
                    UiKit.CardFrame(row);
                }
                Text rank = Outlined(row.transform, "#" + entry.Rank, Theme.SmallSize, i < 3 ? medals[i] : Theme.TextMuted, TextAnchor.MiddleCenter);
                UIFactory.Anchor(rank.rectTransform, 0.02f, 0.1f, 0.14f, 0.9f);
                Text name = Outlined(row.transform, entry.Name, Theme.SmallSize - 2, entry.Mine ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft);
                UIFactory.Anchor(name.rectTransform, 0.16f, 0.1f, 0.62f, 0.9f);
                Text points = Outlined(row.transform, Loc.Number(entry.Points), Theme.SmallSize - 2, Theme.Crystal, TextAnchor.MiddleRight);
                UIFactory.Anchor(points.rectTransform, 0.64f, 0.1f, 0.98f, 0.9f);
            }
        }

        private void BuildBanner(RectTransform body)
        {
            Image banner = UIFactory.Panel("Banner", body, Theme.Panel);
            UIFactory.Anchor(banner.rectTransform, 0.02f, 0.79f, 0.98f, 1f);
            UiKit.FramePanel(banner);
            RectTransform scene = UIFactory.Stretch(UIFactory.Rect("Scene", banner.rectTransform), 22, 22, 22, 22);
            scene.gameObject.AddComponent<RectMask2D>();
            Widgets.Backdrop(scene, CrushRoyale.Core.Story.Kingdom.Central, 0.42f);

            UIFactory.Anchor(Crest(scene, _guild.Level, Loc), 0.02f, 0.2f, 0.24f, 0.92f);

            Text name = Outlined(scene, _guild.Name, Theme.HeaderSize, Theme.Gold);
            UIFactory.Anchor(name.rectTransform, 0.27f, 0.7f, 0.98f, 0.97f);
            if (!string.IsNullOrEmpty(_guild.Description))
            {
                Text description = UIFactory.Label(scene, _guild.Description, Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.MiddleLeft);
                UIFactory.Anchor(description.rectTransform, 0.27f, 0.56f, 0.98f, 0.71f);
            }
            int maxMembers = Game.Backend.Balance.Guild.MaxMembers;
            Chip(scene, "item_crown", _guild.Members.Count + "/" + maxMembers, 0.27f, 0.48f, 0.34f, 0.54f);
            Chip(scene, "item_trophy", Loc.Number(_guild.TotalTrophies), 0.5f, 0.75f, 0.34f, 0.54f);
            Chip(scene, "item_medal", _guild.Rank > 0 ? "#" + _guild.Rank : "-", 0.77f, 0.98f, 0.34f, 0.54f);

            // Level progress (guild points).
            bool max = _guild.NextLevelCurrency == null;
            float progress = max ? 1f : Mathf.Clamp01(_guild.DonationProgress / (float)Math.Max(1, _guild.NextLevelCost));
            UiKit.Bar(scene, progress, Theme.Gold, out RectTransform bar);
            UIFactory.Anchor(bar, 0.27f, 0.05f, 0.98f, 0.28f);
            string label = max ? Loc.T("guild.maxLevel") : Loc.T("guild.levelProgress", _guild.Level + 1, Loc.Number(_guild.DonationProgress), Loc.Number(_guild.NextLevelCost));
            Text barText = Outlined(bar, label, Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Stretch(barText.rectTransform);
        }

        private GuildMemberDto Me => _guild.Members.FirstOrDefault(m => m.PlayerId == Game.Backend.PlayerId);

        private void BuildMembers()
        {
            RectTransform list = UIFactory.ScrollList(_content, 14, 24);
            BuildTournament(list);
            string myRole = Me?.Role ?? "Member";
            int index = 0;
            foreach (GuildMemberDto member in _guild.Members)
            {
                bool mine = member.PlayerId == Game.Backend.PlayerId;
                bool canManage = !mine && (myRole == "Leader" || (myRole == "Officer" && member.Role == "Member"));
                Image card = UIFactory.Panel("Member", list, Theme.Panel);
                UIFactory.Height(card, canManage ? 380 : 290);
                UiKit.CardFrame(card);
                card.gameObject.AddComponent<PopIn>().Delay = Mathf.Min(0.6f, index++ * 0.04f);
                if (mine)
                {
                    Outline glow = card.gameObject.AddComponent<Outline>();
                    glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.9f);
                    glow.effectDistance = new Vector2(5, -5);
                }
                RectTransform rect = card.rectTransform;
                float top = canManage ? 0.3f : 0f;
                float span = 1f - top;

                Initial(rect, member.DisplayName, member.Role, 0.03f, top + span * 0.18f, 0.2f, top + span * 0.9f);
                Text name = Outlined(rect, member.DisplayName, Theme.BodySize, mine ? Theme.Gold : Theme.Text);
                UIFactory.Anchor(name.rectTransform, 0.22f, top + span * 0.56f, 0.66f, top + span * 0.92f);

                // Role badge.
                Color roleColor = member.Role == "Leader" ? Theme.GoldDark : member.Role == "Officer" ? Theme.Hex("5B6FB8") : Theme.PanelLight;
                Image badge = UIFactory.Panel("Role", rect, roleColor);
                UIFactory.Anchor(badge.rectTransform, 0.68f, top + span * 0.62f, 0.97f, top + span * 0.9f);
                Sprite roleArt = UiKit.Art(member.Role == "Leader" ? "item_crown" : member.Role == "Officer" ? "item_medal" : "item_stars");
                if (roleArt != null)
                {
                    Image roleIcon = UIFactory.Icon(badge.transform, roleArt, Color.white, 0);
                    roleIcon.preserveAspect = true;
                    UIFactory.Anchor(roleIcon.rectTransform, 0.02f, 0.05f, 0.3f, 0.95f);
                }
                Text roleText = Outlined(badge.transform, Loc.T("guild.role." + member.Role), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter);
                UIFactory.Anchor(roleText.rectTransform, 0.28f, 0f, 1f, 1f);

                Chip(rect, "item_trophy", Loc.Number(member.Trophies), 0.22f, 0.42f, top + span * 0.3f, top + span * 0.52f);
                Chip(rect, "item_coins", Loc.Number(member.DonatedCoins), 0.435f, 0.63f, top + span * 0.3f, top + span * 0.52f);
                Chip(rect, "item_orbs", Loc.Number(member.DonatedOrbes), 0.645f, 0.815f, top + span * 0.3f, top + span * 0.52f);
                Chip(rect, "item_bolt", Loc.Number(member.BossDamage), 0.83f, 0.97f, top + span * 0.3f, top + span * 0.52f);
                Text today = UIFactory.Label(rect, Loc.T("guild.memberToday", Loc.Number(member.CoinsDonatedToday), Math.Max(0, member.BossAttacksLeft)), Theme.SmallSize - 8, Theme.TextMuted, TextAnchor.MiddleLeft);
                UIFactory.Anchor(today.rectTransform, 0.22f, top + span * 0.06f, 0.97f, top + span * 0.28f);

                if (!canManage)
                {
                    continue;
                }
                RectTransform actions = UIFactory.Anchor(UIFactory.Rect("Actions", rect), 0.03f, 0.04f, 0.97f, 0.28f);
                HorizontalLayoutGroup row = actions.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.spacing = 12;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = row.childForceExpandHeight = true;
                string id = member.PlayerId;
                if (myRole == "Leader")
                {
                    string promote = member.Role == "Member" ? "Officer" : member.Role == "Officer" ? "Member" : null;
                    if (promote != null)
                    {
                        UIFactory.Button(actions, Loc.T(promote == "Officer" ? "guild.promote" : "guild.demote"), () => _ = RoleAsync(id, promote), Theme.PanelLight, Theme.SmallSize - 6, Theme.Text);
                    }
                    UIFactory.Button(actions, Loc.T("guild.makeLeader"), () => _ = RoleAsync(id, "Leader"), Theme.PanelLight, Theme.SmallSize - 6, Theme.Text);
                }
                if ((myRole == "Leader" && member.Role != "Leader") || (myRole == "Officer" && member.Role == "Member"))
                {
                    UIFactory.Button(actions, Loc.T("guild.kick"), () => _ = KickAsync(id), Theme.Danger, Theme.SmallSize - 6, Theme.Text);
                }
            }
            UIFactory.Height(UIFactory.Button(list, Loc.T("guild.leave"), () => _ = LeaveAsync(), Theme.Danger), 120);
        }

        // ------------------------------------------------------------------ donations and tech

        private void BuildUpgrades()
        {
            RectTransform list = UIFactory.ScrollList(_content, 18, 26);
            if (_guild.NextLevelCurrency == null)
            {
                Image done = UIFactory.Panel("Max", list, Theme.Panel);
                UIFactory.Height(done, 160);
                UiKit.CardFrame(done);
                Text text = Outlined(done.transform, Loc.T("guild.maxLevel"), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter);
                UIFactory.Stretch(text.rectTransform);
            }
            else
            {
                BuildDonations(list);
            }
            BuildTech(list);
        }

        private void BuildDonations(Transform list)
        {
            int coinsPerPoint = Math.Max(1, _guild.CoinsPerPoint);
            long cap = Math.Max(0, _guild.DailyCoinCap);
            long usedToday = Me?.CoinsDonatedToday ?? 0;
            long coinsLeft = Math.Max(0, cap - usedToday);
            long toNext = Math.Max(1, _guild.NextLevelCost - _guild.DonationProgress);

            Image panel = UIFactory.Panel("Donate", list, Theme.Panel);
            UIFactory.Height(panel, 1040);
            UiKit.FramePanel(panel);
            RectTransform rect = panel.rectTransform;
            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", rect), 0.08f, 0.9f, 0.92f, 1.02f);
            UiKit.Ribbon(ribbon);
            Text title = Outlined(ribbon, Loc.T("guild.donateTitle"), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Anchor(title.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
            Text rule = UIFactory.Label(rect, Loc.T("guild.donateRule", coinsPerPoint), Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleCenter);
            UIFactory.Anchor(rule.rectTransform, 0.06f, 0.83f, 0.94f, 0.89f);

            // Coins: daily allowance for everyone.
            Image coins = UIFactory.Panel("Coins", rect, new Color(0.08f, 0.05f, 0.16f, 0.75f));
            UIFactory.Anchor(coins.rectTransform, 0.05f, 0.46f, 0.95f, 0.81f);
            UiKit.CardFrame(coins);
            Sprite coinArt = UiKit.Art("item_coins");
            if (coinArt != null)
            {
                Image icon = UIFactory.Icon(coins.transform, coinArt, Color.white, 0);
                icon.preserveAspect = true;
                UIFactory.Anchor(icon.rectTransform, 0.03f, 0.5f, 0.19f, 0.95f);
                icon.gameObject.AddComponent<Breathe>().Amount = 0.04f;
            }
            Text coinTitle = Outlined(coins.transform, Loc.T("guild.coinsDaily"), Theme.BodySize - 2, Theme.Gold);
            UIFactory.Anchor(coinTitle.rectTransform, 0.21f, 0.74f, 0.97f, 0.95f);
            UiKit.Bar(coins.transform, cap > 0 ? usedToday / (float)cap : 1f, Theme.Gold, out RectTransform coinBar);
            UIFactory.Anchor(coinBar, 0.21f, 0.5f, 0.97f, 0.7f);
            Text coinBarText = Outlined(coinBar, Loc.T("guild.coinsToday", Loc.Number(usedToday), Loc.Number(cap)), Theme.SmallSize - 8, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Stretch(coinBarText.rectTransform);
            RectTransform coinRow = UIFactory.Anchor(UIFactory.Rect("CoinButtons", coins.transform), 0.04f, 0.07f, 0.96f, 0.42f);
            HorizontalLayoutGroup coinLayout = coinRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            coinLayout.spacing = 14;
            coinLayout.childControlWidth = coinLayout.childControlHeight = true;
            coinLayout.childForceExpandWidth = coinLayout.childForceExpandHeight = true;
            if (coinsLeft < coinsPerPoint)
            {
                Text done = Outlined(coinRow, Loc.T("guild.coinsDone"), Theme.SmallSize - 2, Theme.Success, TextAnchor.MiddleCenter);
                done.gameObject.AddComponent<LayoutElement>();
            }
            else
            {
                foreach (long amount in new[] { 1000L, 2500L })
                {
                    long value = Math.Min(amount, coinsLeft);
                    UIFactory.Button(coinRow, "+" + Loc.Number(value), () => _ = DonateAsync(value, "Coins"), Theme.PanelLight, Theme.SmallSize - 2, Theme.Text);
                }
                long all = coinsLeft / coinsPerPoint * coinsPerPoint;
                Button maxButton = UIFactory.Button(coinRow, Loc.T("guild.donateMax", Loc.Number(all)), () => _ = DonateAsync(all, "Coins"), Theme.Gold, Theme.SmallSize - 4);
                maxButton.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }

            // Orbes: unlimited, a big gift climbs several levels at once.
            Image orbes = UIFactory.Panel("Orbes", rect, new Color(0.08f, 0.05f, 0.16f, 0.75f));
            UIFactory.Anchor(orbes.rectTransform, 0.05f, 0.05f, 0.95f, 0.43f);
            UiKit.CardFrame(orbes);
            Sprite orbArt = UiKit.Art("item_orbs");
            if (orbArt != null)
            {
                Image icon = UIFactory.Icon(orbes.transform, orbArt, Color.white, 0);
                icon.preserveAspect = true;
                UIFactory.Anchor(icon.rectTransform, 0.03f, 0.55f, 0.19f, 0.95f);
                icon.gameObject.AddComponent<Breathe>().Amount = 0.04f;
            }
            Text orbTitle = Outlined(orbes.transform, Loc.T("guild.orbesUnlimited"), Theme.BodySize - 2, Theme.Orbe);
            UIFactory.Anchor(orbTitle.rectTransform, 0.21f, 0.76f, 0.97f, 0.95f);
            Text orbHint = UIFactory.Label(orbes.transform, Loc.T("guild.orbesHint", Loc.Number(toNext)), Theme.SmallSize - 6, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(orbHint.rectTransform, 0.21f, 0.58f, 0.97f, 0.76f);
            RectTransform orbRow = UIFactory.Anchor(UIFactory.Rect("OrbButtons", orbes.transform), 0.04f, 0.32f, 0.96f, 0.54f);
            HorizontalLayoutGroup orbLayout = orbRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            orbLayout.spacing = 14;
            orbLayout.childControlWidth = orbLayout.childControlHeight = true;
            orbLayout.childForceExpandWidth = orbLayout.childForceExpandHeight = true;
            foreach (long amount in new[] { 10L, 100L })
            {
                long value = amount;
                UIFactory.Button(orbRow, "+" + Loc.Number(value), () => _ = DonateAsync(value, "Orbes"), Theme.PanelLight, Theme.SmallSize - 2, Theme.Text);
            }
            Button nextLevel = UIFactory.Button(orbRow, Loc.T("guild.donateNextLevel", Loc.Number(toNext)), () => _ = DonateAsync(toNext, "Orbes"), Theme.Orbe, Theme.SmallSize - 6);
            nextLevel.gameObject.AddComponent<Breathe>().Amount = 0.03f;

            RectTransform customRow = UIFactory.Anchor(UIFactory.Rect("Custom", orbes.transform), 0.04f, 0.06f, 0.96f, 0.27f);
            HorizontalLayoutGroup customLayout = customRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            customLayout.spacing = 14;
            customLayout.childControlWidth = customLayout.childControlHeight = true;
            customLayout.childForceExpandHeight = true;
            InputField amountInput = Widgets.Input(customRow, Loc.T("guild.amount"), 7, InputField.ContentType.IntegerNumber);
            amountInput.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            UIFactory.Width(UIFactory.Button(customRow, Loc.T("guild.donate"), () =>
            {
                if (long.TryParse(amountInput.text, out long value) && value > 0)
                {
                    _ = DonateAsync(value, "Orbes");
                }
            }, Theme.Success, Theme.SmallSize), 260);
        }

        private static string TechArt(string tech)
        {
            switch (tech)
            {
                case "CoinBonus": return "item_coins";
                case "BossDamage": return "item_bolt";
                case "LifeRecharge": return "item_hourglass";
                case "MaxLives": return "item_heart";
                case "PowerUpDiscount": return "item_ticket";
                case "ChestSpeed": return "item_gift";
                case "PetXp": return "item_fragment";
                default: return "item_xp";
            }
        }

        private void BuildTech(Transform list)
        {
            Image header = UIFactory.Panel("TechHeader", list, new Color(0, 0, 0, 0));
            UIFactory.Height(header, 110);
            Text title = Outlined(header.transform, Loc.T("guild.techTitle"), Theme.BodySize + 2, Theme.Gold);
            UIFactory.Anchor(title.rectTransform, 0.02f, 0f, 0.62f, 1f);
            Chip(header.transform, "item_stars", Loc.T("guild.techPoints", _guild.TechPoints), 0.6f, 0.98f, 0.12f, 0.88f);
            if (_guild.TechPoints > 0)
            {
                header.transform.GetChild(header.transform.childCount - 1).gameObject.AddComponent<Pulse>().Scale = true;
            }

            RectTransform gridRect = UIFactory.Rect("Tech", list);
            int rows = (Techs.Length + 1) / 2;
            UIFactory.Height(gridRect, rows * 330 + (rows - 1) * 16);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(480, 330);
            grid.spacing = new Vector2(16, 16);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            GridFit.On(grid);

            bool canSpend = Me?.Role == "Leader" || Me?.Role == "Officer";
            foreach (string tech in Techs)
            {
                int rank = _guild.Tech.TryGetValue(tech, out int r) ? r : 0;
                int maxRank = Enum.TryParse(tech, out GuildTech parsed) ? GuildTechTree.Get(parsed).MaxRank : 5;
                bool maxed = rank >= maxRank;
                Image cell = UIFactory.Panel("Tech", gridRect, Theme.Panel);
                UiKit.CardFrame(cell);
                if (rank > 0)
                {
                    Image glow = UIFactory.Icon(cell.transform, ProceduralSprites.Glow(128), new Color(Theme.Crystal.r, Theme.Crystal.g, Theme.Crystal.b, 0.35f), 0);
                    UIFactory.Anchor(glow.rectTransform, 0.02f, 0.45f, 0.4f, 1f);
                    glow.raycastTarget = false;
                }
                Sprite art = UiKit.Art(TechArt(tech));
                if (art != null)
                {
                    Image icon = UIFactory.Icon(cell.transform, art, rank > 0 ? Color.white : new Color(0.6f, 0.6f, 0.7f, 1f), 0);
                    icon.preserveAspect = true;
                    UIFactory.Anchor(icon.rectTransform, 0.05f, 0.56f, 0.34f, 0.94f);
                }
                Text name = Outlined(cell.transform, Loc.T("guild.tech." + tech), Theme.SmallSize - 2, rank > 0 ? Theme.Gold : Theme.Text);
                UIFactory.Anchor(name.rectTransform, 0.37f, 0.72f, 0.97f, 0.95f);

                // Rank pips.
                RectTransform pips = UIFactory.Anchor(UIFactory.Rect("Pips", cell.transform), 0.37f, 0.58f, 0.97f, 0.7f);
                HorizontalLayoutGroup pipRow = pips.gameObject.AddComponent<HorizontalLayoutGroup>();
                pipRow.spacing = 8;
                pipRow.childControlWidth = pipRow.childControlHeight = true;
                pipRow.childForceExpandWidth = false;
                pipRow.childForceExpandHeight = true;
                for (int i = 0; i < maxRank; i++)
                {
                    Image pip = UIFactory.Icon(pips, ProceduralSprites.Circle(), i < rank ? Theme.Crystal : new Color(1f, 1f, 1f, 0.18f), 0);
                    UIFactory.Width(pip, 34);
                }

                Text desc = UIFactory.Label(cell.transform, Loc.T("guild.tech." + tech + ".desc"), Theme.SmallSize - 8, Theme.TextMuted, TextAnchor.UpperLeft);
                UIFactory.Anchor(desc.rectTransform, 0.05f, 0.28f, 0.95f, 0.54f);

                string t = tech;
                if (maxed)
                {
                    Text done = Outlined(cell.transform, Loc.T("guild.techMax"), Theme.SmallSize - 4, Theme.Success, TextAnchor.MiddleCenter);
                    UIFactory.Anchor(done.rectTransform, 0.05f, 0.05f, 0.95f, 0.25f);
                }
                else if (canSpend)
                {
                    Button upgrade = UIFactory.Button(cell.transform, Loc.T("guild.upgrade"), () => _ = TechAsync(t), _guild.TechPoints > 0 ? Theme.Success : Theme.PanelLight, Theme.SmallSize - 4);
                    UIFactory.Anchor(upgrade.GetComponent<RectTransform>(), 0.08f, 0.05f, 0.92f, 0.25f);
                    upgrade.interactable = _guild.TechPoints > 0;
                }
                else
                {
                    Text officers = UIFactory.Label(cell.transform, Loc.T("guild.techOfficers"), Theme.SmallSize - 10, Theme.TextMuted, TextAnchor.MiddleCenter);
                    UIFactory.Anchor(officers.rectTransform, 0.05f, 0.05f, 0.95f, 0.25f);
                }
            }
        }

        // ------------------------------------------------------------------ boss

        /// <summary>The weekly boss changes every week: its portrait and name follow the boss index.</summary>
        /// <summary>Name of the boss on duty this week (a proper noun, the same in every language).</summary>
        public static string GuildBossName(Localization loc, int index) => CrushRoyale.Core.Social.GuildBossRoster.For(index).Name;

        /// <summary>Epithet shown under the name. Literal keys: the localization validator only sees complete keys.</summary>
        public static string GuildBossTitle(Localization loc, int index)
        {
            switch (CrushRoyale.Core.Social.GuildBossRoster.For(index).Id)
            {
                case "7kou": return loc.T("guildboss.7kou.title");
                case "escobaros": return loc.T("guildboss.escobaros.title");
                default: return loc.T("guildboss.majors_blue.title");
            }
        }

        /// <summary>
        /// What this boss does to the board, in one line.
        ///
        /// Each of the three fights differently and each has a way to end an attack outright. A player who learns
        /// that by losing a life to it learns the wrong lesson, so the rule is written next to the portrait.
        /// </summary>
        public static string GuildBossRule(Localization loc, int index)
        {
            switch (CrushRoyale.Core.Social.GuildBossRoster.For(index).Id)
            {
                case "7kou": return loc.T("guildboss.7kou.rule");
                case "escobaros": return loc.T("guildboss.escobaros.rule");
                default: return loc.T("guildboss.majors_blue.rule");
            }
        }

        private static CrushRoyale.Core.Story.Kingdom BossKingdom(int index) => CrushRoyale.Core.Social.GuildBossRoster.For(index).Kingdom;

        private void BuildBoss()
        {
            RectTransform list = UIFactory.ScrollList(_content, 18, 26);
            GuildBossDto boss = _guild.Boss;
            if (boss == null)
            {
                Widgets.Loading(list, Loc, 80);
                return;
            }
            long hp = Math.Max(0, boss.MaxHp - boss.Damage);

            Image panel = UIFactory.Panel("Boss", list, Theme.Panel);
            UIFactory.Height(panel, 1040);
            UiKit.FramePanel(panel);
            RectTransform scene = UIFactory.Stretch(UIFactory.Rect("Scene", panel.rectTransform), 26, 26, 26, 26);
            scene.gameObject.AddComponent<RectMask2D>();
            Widgets.Backdrop(scene, BossKingdom(boss.BossIndex), 0.5f);
            Widgets.Fade(scene, top: false, 0.35f, 0.9f);

            Image aura = UIFactory.Icon(scene, ProceduralSprites.Glow(128), boss.Defeated ? new Color(0.5f, 0.5f, 0.5f, 0.3f) : new Color(1f, 0.2f, 0.25f, 0.6f), 0);
            UIFactory.Anchor(aura.rectTransform, 0.05f, 0.18f, 0.95f, 0.8f);
            aura.raycastTarget = false;
            if (!boss.Defeated)
            {
                aura.gameObject.AddComponent<Pulse>();
            }
            Sprite art = ArtLibrary.GuildBoss(boss.BossIndex);
            if (art != null)
            {
                // The art is far taller than the card is wide, so a plain anchored box letterboxed it and the boss
                // looked like a photo pasted in the middle. The window takes the aspect of the art itself, and a
                // fade at its foot dissolves the hard bottom edge into the scene.
                RectTransform stage = UIFactory.Anchor(UIFactory.Rect("BossStage", scene), 0.04f, 0.2f, 0.96f, 0.75f);
                RectTransform window = UIFactory.Rect("Portrait", stage);
                window.anchorMin = new Vector2(0.5f, 0.5f);
                window.anchorMax = new Vector2(0.5f, 0.5f);
                window.pivot = new Vector2(0.5f, 0.5f);
                window.anchoredPosition = Vector2.zero;
                AspectRatioFitter fitter = window.gameObject.AddComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = art.rect.height > 0 ? art.rect.width / art.rect.height : 0.5f;

                Image portrait = UIFactory.Icon(window, art, boss.Defeated ? new Color(0.4f, 0.4f, 0.45f, 1f) : Color.white, 0);
                UIFactory.Stretch(portrait.rectTransform);
                Widgets.Fade(window, top: false, 0.22f, 0.85f);
                if (!boss.Defeated)
                {
                    Breathe breathe = portrait.gameObject.AddComponent<Breathe>();
                    breathe.Amount = 0.025f;
                    breathe.Speed = 1.2f;
                }
            }

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", scene), 0.04f, 0.885f, 0.96f, 1f);
            UiKit.Ribbon(ribbon);
            Text title = Outlined(ribbon, GuildBossName(Loc, boss.BossIndex), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Anchor(title.rectTransform, 0.18f, 0.34f, 0.82f, 0.92f);
            Text epithet = Outlined(scene, GuildBossTitle(Loc, boss.BossIndex), Theme.SmallSize, Theme.Crystal, TextAnchor.MiddleCenter);
            UIFactory.Anchor(epithet.rectTransform, 0.06f, 0.833f, 0.94f, 0.879f);

            // What this one does to the board. Each boss has a way to end an attack outright, and finding that out
            // by losing a life to it is the wrong way to learn it.
            Image ruleBar = UIFactory.Panel("BossRule", scene, new Color(0.06f, 0.02f, 0.13f, 0.88f));
            UIFactory.Anchor(ruleBar.rectTransform, 0.04f, 0.755f, 0.96f, 0.828f);
            ruleBar.raycastTarget = false;
            Text rule = Outlined(ruleBar.transform, GuildBossRule(Loc, boss.BossIndex), Theme.SmallSize - 6, Theme.Warning, TextAnchor.MiddleCenter);
            UIFactory.Stretch(rule.rectTransform, 12, 12, 4, 4);
            Chip(scene, "item_medal", Loc.T("guild.bossWeek", boss.BossIndex), 0.03f, 0.34f, 0.755f, 0.825f);
            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(boss.ResetAtUnixMs) - DateTimeOffset.UtcNow;
            if (left < TimeSpan.Zero)
            {
                left = TimeSpan.Zero;
            }
            Chip(scene, "item_hourglass", Loc.T("guild.bossReset", left.Days, left.Hours), 0.5f, 0.97f, 0.755f, 0.825f);

            if (boss.Defeated)
            {
                Text stamp = Outlined(scene, Loc.T("guild.bossStamp"), Theme.HeaderSize + 20, Theme.Danger, TextAnchor.MiddleCenter);
                UIFactory.Anchor(stamp.rectTransform, 0.05f, 0.45f, 0.95f, 0.65f);
                stamp.rectTransform.localRotation = Quaternion.Euler(0, 0, 12f);
                stamp.gameObject.AddComponent<PopIn>();
            }

            // HP bar.
            UiKit.Bar(scene, boss.MaxHp > 0 ? hp / (float)boss.MaxHp : 0f, Theme.Danger, out RectTransform bar);
            UIFactory.Anchor(bar, 0.05f, 0.125f, 0.95f, 0.19f);
            Text hpText = Outlined(bar, Loc.T("guild.bossHp", Loc.Number(hp), Loc.Number(boss.MaxHp)), Theme.SmallSize - 2, Theme.Text, TextAnchor.MiddleCenter);
            UIFactory.Stretch(hpText.rectTransform);
            Sprite heart = UiKit.Art("item_heart");
            if (heart != null)
            {
                Image heartIcon = UIFactory.Icon(bar, heart, Color.white, 0);
                heartIcon.preserveAspect = true;
                UIFactory.Anchor(heartIcon.rectTransform, -0.04f, -0.35f, 0.08f, 1.35f);
            }

            int attacks = Math.Max(0, Me?.BossAttacksLeft ?? 0);
            if (boss.Defeated)
            {
                Text won = Outlined(scene, Loc.T("guild.bossDefeated"), Theme.BodySize, Theme.Success, TextAnchor.MiddleCenter);
                UIFactory.Anchor(won.rectTransform, 0.04f, 0.015f, 0.96f, 0.115f);
            }
            else
            {
                Button attack = UIFactory.Button(scene, Loc.T("guild.attack", attacks), () => _ = AttackAsync(), attacks > 0 ? Theme.Danger : Theme.PanelLight, Theme.BodySize);
                UIFactory.Anchor(attack.GetComponent<RectTransform>(), 0.15f, 0.01f, 0.85f, 0.07f);
                Text daily = Outlined(scene, Loc.T("guild.bossDaily"), Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleCenter);
                UIFactory.Anchor(daily.rectTransform, 0.04f, 0.075f, 0.96f, 0.12f);
                attack.interactable = attacks > 0;
                if (attacks > 0)
                {
                    attack.gameObject.AddComponent<Breathe>().Amount = 0.035f;
                }
            }

            // Damage podium.
            Widgets.SectionTitle(list, Loc.T("guild.damageBoard"));
            List<GuildMemberDto> ranking = _guild.Members.OrderByDescending(m => m.BossDamage).ToList();
            long best = Math.Max(1, ranking.Count > 0 ? ranking[0].BossDamage : 1);
            Color[] medals = { Theme.Gold, Theme.Hex("C9D3E0"), Theme.Hex("CD7F32") };
            for (int i = 0; i < ranking.Count; i++)
            {
                GuildMemberDto member = ranking[i];
                bool mine = member.PlayerId == Game.Backend.PlayerId;
                Image row = UIFactory.Panel("Damage", list, mine ? Theme.GoldDark : Theme.Panel);
                UIFactory.Height(row, 120);
                UiKit.CardFrame(row);
                row.gameObject.AddComponent<PopIn>().Delay = Mathf.Min(0.6f, i * 0.05f);
                Text rank = Outlined(row.transform, "#" + (i + 1), Theme.BodySize, i < 3 ? medals[i] : Theme.TextMuted, TextAnchor.MiddleCenter);
                UIFactory.Anchor(rank.rectTransform, 0.01f, 0f, 0.13f, 1f);
                Text name = Outlined(row.transform, member.DisplayName, Theme.SmallSize, Theme.Text);
                UIFactory.Anchor(name.rectTransform, 0.14f, 0.5f, 0.6f, 0.95f);
                UiKit.Bar(row.transform, member.BossDamage / (float)best, i < 3 ? medals[i] : Theme.Crystal, out RectTransform damageBar);
                UIFactory.Anchor(damageBar, 0.14f, 0.1f, 0.72f, 0.48f);
                Text value = Outlined(row.transform, Loc.Number(member.BossDamage), Theme.SmallSize, Theme.Danger, TextAnchor.MiddleRight);
                UIFactory.Anchor(value.rectTransform, 0.73f, 0f, 0.97f, 1f);
            }
        }

        // ------------------------------------------------------------------ chat

        private void BuildChat()
        {
            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("ChatHolder", _content), 0, 0.12f, 1, 1);
            _chatList = UIFactory.ScrollList(holder, 10, 20);
            UIFactory.Stretch((RectTransform)_chatList.parent.parent);

            RectTransform inputRow = UIFactory.Anchor(UIFactory.Rect("Input", _content), 0.02f, 0.01f, 0.98f, 0.11f);
            HorizontalLayoutGroup row = inputRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 12;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandHeight = true;
            _chatInput = Widgets.Input(inputRow, Loc.T("guild.chatPlaceholder"), Game.Backend.Balance.Guild.ChatMaxLength);
            UIFactory.Width(UIFactory.Button(inputRow, Loc.T("guild.send"), () => _ = SendChatAsync(), Theme.Gold, Theme.BodySize), 240);

            _ = LoadChatAsync();
        }

        private async Task LoadChatAsync()
        {
            ChatHistoryResponse history = await Api(api => api.GetChatHistoryAsync(), loading: false);
            if (history == null || _chatList == null)
            {
                return;
            }
            foreach (ChatMessageDto message in history.Messages.OrderBy(m => m.Id))
            {
                AddChatLine(message);
            }
            ScrollChatToBottom();
        }

        private void AddChatLine(ChatMessageDto message)
        {
            if (_chatList == null)
            {
                return;
            }
            bool mine = message.PlayerId == Game.Backend.PlayerId;
            int lines = Mathf.Clamp(1 + (message.Body ?? string.Empty).Length / 34, 1, 6);
            RectTransform line = UIFactory.Rect("Line", _chatList);
            UIFactory.Height(line, 70 + lines * 44);

            Image bubble = UIFactory.Panel("Bubble", line, mine ? Theme.GoldDark : Theme.Panel);
            UIFactory.Anchor(bubble.rectTransform, mine ? 0.22f : 0.02f, 0f, mine ? 0.98f : 0.78f, 1f);
            UiKit.CardFrame(bubble);
            Text author = Outlined(bubble.transform, mine ? Loc.T("guild.you") : message.DisplayName, Theme.SmallSize - 8, mine ? Theme.Text : Theme.Crystal);
            UIFactory.Anchor(author.rectTransform, 0.05f, 1f - 60f / (70 + lines * 44), 0.95f, 0.96f);
            Text body = UIFactory.Label(bubble.transform, message.Body, Theme.SmallSize - 2, Theme.Text, TextAnchor.UpperLeft);
            body.resizeTextForBestFit = false;
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.Anchor(body.rectTransform, 0.05f, 0.06f, 0.95f, 1f - 58f / (70 + lines * 44));
        }

        private void ScrollChatToBottom()
        {
            ScrollRect scroll = _chatList != null ? _chatList.GetComponentInParent<ScrollRect>() : null;
            if (scroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scroll.verticalNormalizedPosition = 0f;
            }
        }

        private void OnChatMessage(ChatMessageDto message)
        {
            if (_tab == 3 && _chatList != null)
            {
                AddChatLine(message);
                ScrollChatToBottom();
            }
            if (message.PlayerId != Game.Backend.PlayerId)
            {
                Game.Audio.PlaySFX(SoundIds.Chat);
            }
        }

        private async Task SendChatAsync()
        {
            string text = _chatInput.text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }
            _chatInput.text = string.Empty;
            ChatMessageDto sent = await Api(api => api.SendChatAsync(text), loading: false);
            if (sent != null && Game.Backend.Client.Chat.Status != CrushRoyale.Client.RealtimeStatus.Joined)
            {
                AddChatLine(sent);
                ScrollChatToBottom();
            }
        }

        // ------------------------------------------------------------------ actions

        private async Task RoleAsync(string playerId, string role)
        {
            GuildDto guild = await Api(api => api.SetRoleAsync(playerId, role));
            if (guild != null)
            {
                _guild = guild;
                Rebuild();
            }
        }

        private async Task KickAsync(string playerId)
        {
            if (!await UI.Confirm(Loc.T("guild.kick"), Loc.T("guild.kickConfirm")))
            {
                return;
            }
            GuildDto guild = await Api(api => api.KickAsync(playerId));
            if (guild != null)
            {
                _guild = guild;
                Rebuild();
            }
        }

        private async Task LeaveAsync()
        {
            if (!await UI.Confirm(Loc.T("guild.leave"), Loc.T("guild.leaveConfirm")))
            {
                return;
            }
            if (await Api(api => api.LeaveGuildAsync()) != null)
            {
                Game.Backend.Client.Chat.Disconnect();
                await Game.Backend.RefreshProfileAsync();
                await LoadAsync();
            }
        }

        private async Task DonateAsync(long amount, string currency)
        {
            // Big orbe gifts are confirmed first (they cannot be undone).
            if (currency == "Orbes" && amount >= 100
                && !await UI.Confirm(Loc.T("guild.donateTitle"), Loc.T("guild.donateConfirm", Loc.Number(amount))))
            {
                return;
            }
            int before = _guild?.Level ?? 0;
            DonateResponse response = await Api(api => api.DonateAsync(amount, currency));
            if (response == null || this == null)
            {
                return;
            }
            Game.Backend.ApplyWallet(response.Wallet);
            _guild = response.Guild;
            if (response.LevelsGained > 0)
            {
                Game.Audio.PlaySFX(SoundIds.WinFanfare);
                UI.Toast(Loc.T("guild.levelsUp", response.LevelsGained, response.Guild?.Level ?? before + response.LevelsGained), 3.5f);
            }
            else
            {
                Game.Audio.PlaySFX(SoundIds.Coins);
                UI.Toast(Loc.T("guild.donated", Loc.Number(response.Points)), 2f);
            }
            Rebuild();
        }

        private async Task TechAsync(string tech)
        {
            GuildDto guild = await Api(api => api.SpendTechAsync(tech));
            if (guild != null)
            {
                Game.Audio.PlaySFX(SoundIds.Sparkle);
                _guild = guild;
                Rebuild();
            }
        }

        private async Task AttackAsync()
        {
            MatchStartResponse ticket = await Api(api => api.StartGuildBossAsync(new StartStageRequest()));
            if (ticket != null)
            {
                UI.Show<GameplayScreen>(MatchLaunch.Online(ticket), addToHistory: false);
            }
        }
    }
}
