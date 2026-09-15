using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>Guild hub: search/create, members and roles, donations and tech, weekly boss, realtime chat.</summary>
    public sealed class GuildScreen : UIScreen
    {
        private static readonly string[] MemberTabs = { "guild.tab.members", "guild.tab.upgrades", "guild.tab.boss", "guild.tab.chat" };
        private static readonly string[] Techs = { "CoinBonus", "BossDamage", "LifeRecharge", "MaxLives", "PowerUpDiscount", "BattlePassXp" };

        private GuildDto _guild;
        private GuildSearchResponse _search;
        private int _tab;
        private bool _loaded;
        private RectTransform _content;
        private RectTransform _chatList;
        private InputField _chatInput;
        private InputField _searchInput;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("guild.title");
            if (!_loaded)
            {
                UIFactory.Label(body, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted);
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

        // ------------------------------------------------------------------ without a guild

        private void BuildNoGuild(RectTransform body)
        {
            RectTransform list = UIFactory.ScrollList(body, 18, 32);
            Widgets.SectionTitle(list, Loc.T("guild.create"));
            InputField name = Widgets.Input(list, Loc.T("guild.namePlaceholder"), 20);
            UIFactory.Height(name, 120);
            InputField description = Widgets.Input(list, Loc.T("guild.descriptionPlaceholder"), 200);
            UIFactory.Height(description, 120);
            UIFactory.Height(UIFactory.Button(list, Loc.T("guild.createButton", Game.Backend.Balance.Guild.CreateCostCoins), () => _ = CreateAsync(name.text, description.text)), 120);

            Widgets.SectionTitle(list, Loc.T("guild.search"));
            HorizontalLayoutGroup row = UIFactory.Row(list, 120, 16);
            _searchInput = Widgets.Input(row.transform, Loc.T("guild.searchPlaceholder"), 20);
            UIFactory.Width(UIFactory.Button(row.transform, Loc.T("common.search"), () => _ = SearchAsync(_searchInput.text), Theme.PanelLight, Theme.BodySize, Theme.Text), 260);

            foreach (GuildSummaryDto g in _search?.Guilds ?? new List<GuildSummaryDto>())
            {
                RectTransform card = Widgets.Card(list, 220);
                UIFactory.Label(card, g.Name, Theme.HeaderSize - 6, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("guild.summary", g.Level, g.Members, Game.Backend.Balance.Guild.MaxMembers, Loc.Number(g.TotalTrophies), g.MinTrophies), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                long id = g.Id;
                Button join = UIFactory.Button(card, Loc.T(g.IsOpen ? "guild.join" : "guild.closed"), () => _ = JoinAsync(id), g.IsOpen ? Theme.Gold : Theme.PanelLight, Theme.BodySize);
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
            Image header = UIFactory.Panel("Header", body, Theme.Panel);
            UIFactory.Anchor(header.rectTransform, 0.02f, 0.86f, 0.98f, 1f);
            Text name = UIFactory.Label(header.transform, _guild.Name, Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(name.rectTransform, 0.04f, 0.5f, 0.96f, 0.95f);
            Text info = UIFactory.Label(header.transform, Loc.T("guild.header", _guild.Level, _guild.Members.Count, Loc.Number(_guild.TotalTrophies), _guild.Rank > 0 ? _guild.Rank.ToString() : "-"), Theme.SmallSize + 2, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(info.rectTransform, 0.04f, 0.05f, 0.96f, 0.5f);

            RectTransform tabs = UIFactory.Anchor(UIFactory.Rect("Tabs", body), 0.02f, 0.78f, 0.98f, 0.855f);
            tabs.gameObject.AddComponent<VerticalLayoutGroup>().childForceExpandWidth = true;
            Action<int> setTab = Widgets.Tabs(tabs, MemberTabs.Select(k => Loc.T(k)).ToList(), index =>
            {
                _tab = index;
                Rebuild();
            });
            setTab(_tab);

            _content = UIFactory.Anchor(UIFactory.Rect("Content", body), 0, 0, 1, 0.775f);
            switch (_tab)
            {
                case 0: BuildMembers(); break;
                case 1: BuildUpgrades(); break;
                case 2: BuildBoss(); break;
                default: BuildChat(); break;
            }
        }

        private GuildMemberDto Me => _guild.Members.FirstOrDefault(m => m.PlayerId == Game.Backend.PlayerId);

        private void BuildMembers()
        {
            RectTransform list = UIFactory.ScrollList(_content, 12, 24);
            string myRole = Me?.Role ?? "Member";
            foreach (GuildMemberDto member in _guild.Members)
            {
                RectTransform card = Widgets.Card(list, 220);
                UIFactory.Label(card, member.DisplayName + "  (" + Loc.T("guild.role." + member.Role) + ")", Theme.BodySize, member.PlayerId == Game.Backend.PlayerId ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("guild.memberLine", member.Trophies, Loc.Number(member.DonatedCoins), Loc.Number(member.DonatedOrbes), Loc.Number(member.BossDamage)), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);

                if (member.PlayerId == Game.Backend.PlayerId)
                {
                    continue;
                }
                HorizontalLayoutGroup row = UIFactory.Row(card, 80, 12);
                string id = member.PlayerId;
                if (myRole == "Leader")
                {
                    string promote = member.Role == "Member" ? "Officer" : member.Role == "Officer" ? "Member" : null;
                    if (promote != null)
                    {
                        UIFactory.Button(row.transform, Loc.T(promote == "Officer" ? "guild.promote" : "guild.demote"), () => _ = RoleAsync(id, promote), Theme.PanelLight, Theme.SmallSize, Theme.Text);
                    }
                    UIFactory.Button(row.transform, Loc.T("guild.makeLeader"), () => _ = RoleAsync(id, "Leader"), Theme.PanelLight, Theme.SmallSize, Theme.Text);
                }
                if ((myRole == "Leader" && member.Role != "Leader") || (myRole == "Officer" && member.Role == "Member"))
                {
                    UIFactory.Button(row.transform, Loc.T("guild.kick"), () => _ = KickAsync(id), Theme.Danger, Theme.SmallSize, Theme.Text);
                }
            }
            UIFactory.Height(UIFactory.Button(list, Loc.T("guild.leave"), () => _ = LeaveAsync(), Theme.Danger), 120);
        }

        private void BuildUpgrades()
        {
            RectTransform list = UIFactory.ScrollList(_content, 16, 32);
            Widgets.SectionTitle(list, Loc.T("guild.level", _guild.Level));
            if (_guild.NextLevelCurrency != null)
            {
                UIFactory.ProgressBar(list, _guild.DonationProgress / (float)Math.Max(1, _guild.NextLevelCost), Theme.Gold, out RectTransform bar);
                UIFactory.Height(bar, 50);
                string currency = _guild.NextLevelCurrency == "Coins" ? "currency.coins" : "currency.orbes";
                UIFactory.Height(UIFactory.Label(list, Loc.T("guild.nextLevel", Loc.Number(_guild.DonationProgress), Loc.T(currency, Loc.Number(_guild.NextLevelCost))), Theme.BodySize), 70);

                HorizontalLayoutGroup row = UIFactory.Row(list, 120, 16);
                InputField amount = Widgets.Input(row.transform, Loc.T("guild.amount"), 7, InputField.ContentType.IntegerNumber);
                UIFactory.Width(UIFactory.Button(row.transform, Loc.T("guild.donate"), () =>
                {
                    if (long.TryParse(amount.text, out long value) && value > 0)
                    {
                        _ = DonateAsync(value);
                    }
                }), 300);
            }
            else
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("guild.maxLevel"), Theme.BodySize, Theme.Gold), 80);
            }

            Widgets.SectionTitle(list, Loc.T("guild.tech", _guild.TechPoints));
            bool canSpend = Me?.Role == "Leader" || Me?.Role == "Officer";
            foreach (string tech in Techs)
            {
                int rank = _guild.Tech.TryGetValue(tech, out int r) ? r : 0;
                RectTransform card = Widgets.Card(list, 200);
                UIFactory.Label(card, Loc.T("guild.tech." + tech) + "  " + rank, Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("guild.tech." + tech + ".desc"), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                if (canSpend && _guild.TechPoints > 0)
                {
                    string t = tech;
                    UIFactory.Button(card, Loc.T("guild.upgrade"), () => _ = TechAsync(t), Theme.PanelLight, Theme.SmallSize, Theme.Text);
                }
            }
        }

        private void BuildBoss()
        {
            RectTransform list = UIFactory.ScrollList(_content, 16, 32);
            GuildBossDto boss = _guild.Boss;
            if (boss == null)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("common.loading"), Theme.BodySize), 80);
                return;
            }
            Widgets.SectionTitle(list, Loc.T("guild.bossTitle", boss.BossIndex));
            UIFactory.ProgressBar(list, 1f - Mathf.Clamp01(boss.Damage / (float)Math.Max(1, boss.MaxHp)), Theme.Danger, out RectTransform bar);
            UIFactory.Height(bar, 70);
            UIFactory.Height(UIFactory.Label(list, Loc.T("guild.bossHp", Loc.Number(Math.Max(0, boss.MaxHp - boss.Damage)), Loc.Number(boss.MaxHp)), Theme.BodySize), 70);

            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(boss.ResetAtUnixMs) - DateTimeOffset.UtcNow;
            UIFactory.Height(UIFactory.Label(list, Loc.T("guild.bossReset", left.Days, left.Hours), Theme.SmallSize, Theme.TextMuted), 60);

            int attacks = Me?.BossAttacksLeft ?? 0;
            if (boss.Defeated)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("guild.bossDefeated"), Theme.HeaderSize, Theme.Success), 100);
            }
            else
            {
                Button attack = UIFactory.Button(list, Loc.T("guild.attack", attacks), () => _ = AttackAsync());
                UIFactory.Height(attack, 140);
                attack.interactable = attacks > 0;
            }

            Widgets.SectionTitle(list, Loc.T("guild.damageBoard"));
            foreach (GuildMemberDto member in _guild.Members.OrderByDescending(m => m.BossDamage))
            {
                UIFactory.Height(UIFactory.Label(list, member.DisplayName + "  " + Loc.Number(member.BossDamage), Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft), 60);
            }
        }

        private void BuildChat()
        {
            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("ChatHolder", _content), 0, 0.12f, 1, 1);
            _chatList = UIFactory.ScrollList(holder, 8, 20);
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
        }

        private void AddChatLine(ChatMessageDto message)
        {
            if (_chatList == null)
            {
                return;
            }
            bool mine = message.PlayerId == Game.Backend.PlayerId;
            Text line = UIFactory.Label(_chatList, message.DisplayName + ": " + message.Body, Theme.SmallSize + 4, mine ? Theme.Gold : Theme.Text, mine ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft);
            line.resizeTextForBestFit = false;
            UIFactory.Height(line, 64);
        }

        private void OnChatMessage(ChatMessageDto message)
        {
            if (_tab == 3 && _chatList != null)
            {
                AddChatLine(message);
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
            }
        }

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

        private async Task DonateAsync(long amount)
        {
            DonateResponse response = await Api(api => api.DonateAsync(amount));
            if (response != null)
            {
                Game.Backend.ApplyWallet(response.Wallet);
                _guild = response.Guild;
                Game.Audio.PlaySFX(response.LeveledUp ? SoundIds.WinFanfare : SoundIds.Coins);
                Rebuild();
            }
        }

        private async Task TechAsync(string tech)
        {
            GuildDto guild = await Api(api => api.SpendTechAsync(tech));
            if (guild != null)
            {
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
