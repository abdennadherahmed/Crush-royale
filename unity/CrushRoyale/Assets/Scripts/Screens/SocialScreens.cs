using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>Friends: list, requests, search by id or name, friendly challenges (5-minute cooldown, no trophies).</summary>
    public sealed class FriendsScreen : UIScreen
    {
        private FriendsResponse _friends;
        private SearchPlayersResponse _search;
        private InputField _query;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("friends.title");
            RectTransform list = UIFactory.ScrollList(body, 14, 28);

            HorizontalLayoutGroup row = UIFactory.Row(list, 120, 16);
            _query = Widgets.Input(row.transform, Loc.T("friends.searchPlaceholder"), 40);
            UIFactory.Width(UIFactory.Button(row.transform, Loc.T("common.search"), () => _ = SearchAsync(), Theme.PanelLight, Theme.BodySize, Theme.Text), 260);

            if (_search != null)
            {
                Widgets.SectionTitle(list, Loc.T("friends.results"));
                foreach (PublicProfileDto player in _search.Players.Where(p => p.Id != Game.Backend.PlayerId))
                {
                    RectTransform card = PlayerCard(list, player);
                    string id = player.Id;
                    UIFactory.Button(card, Loc.T("friends.add"), () => _ = ActAsync("request", id), Theme.Gold, Theme.SmallSize);
                }
            }

            if (_friends == null)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted), 100);
                return;
            }

            if (_friends.Incoming.Count > 0)
            {
                Widgets.SectionTitle(list, Loc.T("friends.incoming"));
                foreach (PublicProfileDto player in _friends.Incoming)
                {
                    RectTransform card = PlayerCard(list, player);
                    HorizontalLayoutGroup actions = UIFactory.Row(card, 80, 12);
                    string id = player.Id;
                    UIFactory.Button(actions.transform, Loc.T("friends.accept"), () => _ = ActAsync("accept", id), Theme.Success, Theme.SmallSize, Theme.Text);
                    UIFactory.Button(actions.transform, Loc.T("friends.decline"), () => _ = ActAsync("decline", id), Theme.PanelLight, Theme.SmallSize, Theme.Text);
                }
            }

            Widgets.SectionTitle(list, Loc.T("friends.list", _friends.Friends.Count));
            if (_friends.Friends.Count == 0)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("friends.empty"), Theme.BodySize, Theme.TextMuted), 120);
            }
            foreach (PublicProfileDto friend in _friends.Friends)
            {
                RectTransform card = PlayerCard(list, friend);
                HorizontalLayoutGroup actions = UIFactory.Row(card, 80, 12);
                string id = friend.Id;
                long cooldown = _friends.ChallengeCooldownMs.TryGetValue(id, out long ms) ? ms : 0;
                Button challenge = UIFactory.Button(actions.transform, cooldown > 0 ? Loc.T("friends.cooldown", Mathf.CeilToInt(cooldown / 60000f)) : Loc.T("friends.challenge"), () => _ = ChallengeAsync(id), Theme.Gold, Theme.SmallSize);
                challenge.interactable = cooldown <= 0;
                UIFactory.Button(actions.transform, Loc.T("friends.remove"), () => _ = RemoveAsync(id), Theme.PanelLight, Theme.SmallSize, Theme.Text);
            }

            if (_friends.Outgoing.Count > 0)
            {
                Widgets.SectionTitle(list, Loc.T("friends.outgoing"));
                foreach (PublicProfileDto player in _friends.Outgoing)
                {
                    PlayerCard(list, player);
                }
            }

            UIFactory.Height(UIFactory.Label(list, Loc.T("friends.myId", Game.Backend.PlayerId), Theme.SmallSize, Theme.TextMuted), 80);
        }

        public override async Task OnShownAsync()
        {
            _friends = await Api(api => api.GetFriendsAsync(), loading: false);
            if (this != null)
            {
                Rebuild();
            }
        }

        private RectTransform PlayerCard(Transform parent, PublicProfileDto player)
        {
            RectTransform card = Widgets.Card(parent, 230);
            UIFactory.Label(card, player.DisplayName + (string.IsNullOrEmpty(player.Title) ? string.Empty : "  - " + Loc.T("cosmetic." + player.Title)), Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Label(card, Loc.T("friends.line", Loc.T("league." + player.League), player.Trophies, player.HighestStage), Theme.SmallSize, Theme.League(player.League), TextAnchor.MiddleLeft);
            return card;
        }

        private async Task SearchAsync()
        {
            SearchPlayersResponse search = await Api(api => api.SearchPlayersAsync(_query.text));
            if (search != null)
            {
                _search = search;
                Rebuild();
            }
        }

        private async Task ActAsync(string action, string playerId)
        {
            Task<FriendsResponse> Call(CrushRoyale.Client.CrushApi api)
            {
                switch (action)
                {
                    case "request": return api.SendFriendRequestAsync(playerId);
                    case "accept": return api.AcceptFriendAsync(playerId);
                    case "decline": return api.DeclineFriendAsync(playerId);
                    default: return api.RemoveFriendAsync(playerId);
                }
            }

            FriendsResponse friends = await Api(Call);
            if (friends != null)
            {
                _friends = friends;
                if (action == "request")
                {
                    UI.Toast(Loc.T("friends.requestSent"));
                    _search = null;
                }
                Rebuild();
            }
        }

        private async Task RemoveAsync(string playerId)
        {
            if (await UI.Confirm(Loc.T("friends.remove"), Loc.T("friends.removeConfirm")))
            {
                await ActAsync("remove", playerId);
            }
        }

        private async Task ChallengeAsync(string friendId)
        {
            MatchStartResponse ticket = await Api(api => api.ChallengeFriendAsync(friendId, new StartStageRequest()));
            if (ticket != null)
            {
                UI.Show<GameplayScreen>(MatchLaunch.Online(ticket), addToHistory: false);
            }
        }
    }

    /// <summary>Weekly leaderboards per league and for guilds, with the reset countdown.</summary>
    public sealed class LeaderboardScreen : UIScreen
    {
        private static readonly string[] Leagues = { null, "Bronze", "Silver", "Gold", "Platinum", "Diamond", "Master", "Guilds" };

        private int _tab;
        private LeaderboardResponse _players;
        private GuildLeaderboardResponse _guilds;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("leaderboard.title");
            RectTransform tabs = UIFactory.Anchor(UIFactory.Rect("Tabs", body), 0.01f, 0.84f, 0.99f, 1f);
            GridLayoutGroup grid = tabs.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(255, 100);
            grid.spacing = new Vector2(10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            for (int i = 0; i < Leagues.Length; i++)
            {
                int index = i;
                string key = Leagues[i] == null ? "leaderboard.all" : Leagues[i] == "Guilds" ? "leaderboard.guilds" : "league." + Leagues[i];
                UIFactory.Button(tabs, Loc.T(key), () =>
                {
                    _tab = index;
                    _ = LoadAsync();
                }, i == _tab ? Theme.GoldDark : Theme.PanelLight, Theme.SmallSize, Theme.Text);
            }

            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.83f);
            RectTransform list = UIFactory.ScrollList(holder, 8, 24);
            UIFactory.Stretch((RectTransform)list.parent.parent);

            if (Leagues[_tab] == "Guilds")
            {
                if (_guilds == null)
                {
                    return;
                }
                Reset(list, _guilds.ResetAtUnixMs);
                UIFactory.Height(UIFactory.Label(list, Loc.T("leaderboard.myGuildRank", _guilds.MyGuildRank?.ToString() ?? "-"), Theme.BodySize, Theme.Gold), 70);
                foreach (GuildRankDto g in _guilds.Entries)
                {
                    Line(list, g.Rank, g.Name, Loc.Number(g.TotalTrophies), Theme.Text);
                }
                return;
            }

            if (_players == null)
            {
                return;
            }
            Reset(list, _players.ResetAtUnixMs);
            UIFactory.Height(UIFactory.Label(list, Loc.T("leaderboard.myRank", _players.MyRank?.ToString() ?? "-"), Theme.BodySize, Theme.Gold), 70);
            foreach (LeaderboardEntryDto e in _players.Entries)
            {
                Line(list, e.Rank, e.DisplayName, e.Trophies.ToString(), e.PlayerId == Game.Backend.PlayerId ? Theme.Gold : Theme.League(e.League));
            }
        }

        public override Task OnShownAsync() => LoadAsync();

        private async Task LoadAsync()
        {
            if (Leagues[_tab] == "Guilds")
            {
                _guilds = await Api(api => api.GetGuildLeaderboardAsync());
            }
            else
            {
                _players = await Api(api => api.GetWeeklyLeaderboardAsync(Leagues[_tab]));
            }
            if (this != null)
            {
                Rebuild();
            }
        }

        private void Reset(Transform list, long resetAtUnixMs)
        {
            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(resetAtUnixMs) - DateTimeOffset.UtcNow;
            UIFactory.Height(UIFactory.Label(list, Loc.T("leaderboard.reset", left.Days, left.Hours), Theme.SmallSize, Theme.TextMuted), 60);
        }

        private static void Line(Transform list, int rank, string name, string value, Color color)
        {
            HorizontalLayoutGroup row = UIFactory.Row(list, 80, 12);
            UIFactory.Width(UIFactory.Label(row.transform, "#" + rank, Theme.BodySize, rank <= 3 ? Theme.Gold : Theme.TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold), 160);
            UIFactory.Label(row.transform, name, Theme.BodySize, color, TextAnchor.MiddleLeft);
            UIFactory.Width(UIFactory.Label(row.transform, value, Theme.BodySize, Theme.Text, TextAnchor.MiddleRight), 220);
        }
    }

    /// <summary>Collection book: 12 pages of achievements, claims, permanent coin bonus.</summary>
    public sealed class AchievementsScreen : UIScreen
    {
        private AchievementsResponse _data;
        private int _page = 1;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("achievements.title");
            if (_data == null)
            {
                UIFactory.Label(body, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted);
                return;
            }

            int pages = _data.Achievements.Max(a => a.Page);
            RectTransform header = UIFactory.Anchor(UIFactory.Rect("Header", body), 0.02f, 0.86f, 0.98f, 1f);
            HorizontalLayoutGroup row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandHeight = true;
            UIFactory.Width(UIFactory.Button(header, "<", () => { _page = Math.Max(1, _page - 1); Rebuild(); }, Theme.PanelLight, Theme.HeaderSize, Theme.Text), 140);
            UIFactory.Label(header, Loc.T("achievements.page", _page, pages, Mathf.RoundToInt(_data.CoinBonus * 100)), Theme.BodySize, Theme.Gold);
            UIFactory.Width(UIFactory.Button(header, ">", () => { _page = Math.Min(pages, _page + 1); Rebuild(); }, Theme.PanelLight, Theme.HeaderSize, Theme.Text), 140);

            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.85f);
            RectTransform list = UIFactory.ScrollList(holder, 14, 28);
            UIFactory.Stretch((RectTransform)list.parent.parent);

            bool pageDone = _data.PagesCompleted.Contains(_page);
            UIFactory.Height(UIFactory.Label(list, Loc.T(pageDone ? "achievements.pageDone" : "achievements.pageReward", Loc.T("cosmetic.frame.page" + _page)), Theme.SmallSize, pageDone ? Theme.Success : Theme.TextMuted), 70);

            foreach (AchievementDto a in _data.Achievements.Where(x => x.Page == _page))
            {
                RectTransform card = Widgets.Card(list, 290, a.Claimed ? Theme.BackgroundLight : Theme.Panel);
                UIFactory.Label(card, Loc.T("ach." + a.Id + ".title"), Theme.BodySize, a.Unlocked ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Label(card, Loc.T("ach." + a.Id + ".desc", Loc.Number(a.Target)), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft);
                UIFactory.ProgressBar(card, a.Progress / (float)Math.Max(1, a.Target), Theme.Crystal, out RectTransform _);
                if (a.Claimed)
                {
                    UIFactory.Label(card, Loc.T("achievements.claimed"), Theme.SmallSize, Theme.Success, TextAnchor.MiddleRight);
                }
                else if (a.Unlocked)
                {
                    string id = a.Id;
                    UIFactory.Button(card, Loc.T("achievements.claim") + "  " + MainMenuScreen.RewardText(Loc, a.Reward).Replace("\n", ", "), () => _ = ClaimAsync(id), Theme.Gold, Theme.SmallSize);
                }
                else
                {
                    UIFactory.Label(card, Loc.Number(a.Progress) + " / " + Loc.Number(a.Target) + "   " + MainMenuScreen.RewardText(Loc, a.Reward).Replace("\n", ", "), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleRight);
                }
            }
        }

        public override async Task OnShownAsync()
        {
            _data = await Api(api => api.GetAchievementsAsync(), loading: false);
            if (_data != null && this != null)
            {
                AchievementDto firstClaimable = _data.Achievements.FirstOrDefault(a => a.Unlocked && !a.Claimed);
                if (firstClaimable != null)
                {
                    _page = firstClaimable.Page;
                }
                Rebuild();
            }
        }

        private async Task ClaimAsync(string id)
        {
            ClaimResponse claim = await Api(api => api.ClaimAchievementAsync(id));
            if (claim == null)
            {
                return;
            }
            Game.Backend.ApplyWallet(claim.Wallet);
            Game.Audio.PlaySFX(claim.PageCompleted ? Audio.SoundIds.WinFanfare : Audio.SoundIds.Coins);
            UI.Toast(MainMenuScreen.RewardText(Loc, claim.Reward));
            await OnShownAsync();
        }
    }

    /// <summary>Seasonal battle pass (50 tiers, free and premium tracks).</summary>
    public sealed class BattlePassScreen : UIScreen
    {
        private BattlePassResponse _pass;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("battlepass.title");
            if (_pass == null)
            {
                UIFactory.Label(body, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted);
                return;
            }

            RectTransform list = UIFactory.ScrollList(body, 12, 28);
            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(_pass.SeasonEndUnixMs) - DateTimeOffset.UtcNow;
            Widgets.SectionTitle(list, Loc.T("battlepass.season", _pass.Season, _pass.Tier, left.Days));
            long intoTier = _pass.Xp - _pass.Tier * _pass.XpPerTier;
            UIFactory.ProgressBar(list, _pass.Tier >= _pass.Tiers.Count ? 1f : intoTier / (float)_pass.XpPerTier, Theme.Crystal, out RectTransform bar);
            UIFactory.Height(bar, 50);

            if (!_pass.Premium)
            {
                UIFactory.Height(UIFactory.Button(list, Loc.T("battlepass.unlockPremium"), () => UI.Show<ShopScreen>(), Theme.Gold), 120);
            }

            foreach (BattlePassTierDto tier in _pass.Tiers)
            {
                bool reached = _pass.Tier >= tier.Tier;
                RectTransform card = Widgets.Card(list, 250, reached ? Theme.Panel : Theme.BackgroundLight);
                UIFactory.Label(card, Loc.T("battlepass.tier", tier.Tier), Theme.BodySize, reached ? Theme.Gold : Theme.TextMuted, TextAnchor.MiddleLeft, FontStyle.Bold);
                HorizontalLayoutGroup row = UIFactory.Row(card, 150, 16);
                Track(row.transform, tier, false, reached);
                Track(row.transform, tier, true, reached && _pass.Premium);
            }
        }

        public override async Task OnShownAsync()
        {
            _pass = await Api(api => api.GetBattlePassAsync(), loading: false);
            if (this != null)
            {
                Rebuild();
            }
        }

        private void Track(Transform parent, BattlePassTierDto tier, bool premium, bool claimable)
        {
            RewardDto reward = premium ? tier.Premium : tier.Free;
            bool claimed = premium ? tier.PremiumClaimed : tier.FreeClaimed;
            string label = Loc.T(premium ? "battlepass.premium" : "battlepass.free") + "\n" + MainMenuScreen.RewardText(Loc, reward).Replace("\n", ", ");
            if (claimed)
            {
                label += "\n" + Loc.T("achievements.claimed");
            }
            int t = tier.Tier;
            Button button = UIFactory.Button(parent, label, () => _ = ClaimAsync(t, premium), premium ? Theme.Orbe : Theme.PanelLight, Theme.SmallSize - 2, premium ? Theme.Background : Theme.Text);
            button.interactable = claimable && !claimed;
        }

        private async Task ClaimAsync(int tier, bool premium)
        {
            ClaimResponse claim = await Api(api => api.ClaimBattlePassAsync(tier, premium));
            if (claim != null)
            {
                Game.Backend.ApplyWallet(claim.Wallet);
                UI.Toast(MainMenuScreen.RewardText(Loc, claim.Reward));
                await OnShownAsync();
            }
        }
    }

    /// <summary>Daily quests and the 7-day login calendar.</summary>
    public sealed class QuestsScreen : UIScreen
    {
        private QuestsResponse _quests;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("quests.title");
            RectTransform list = UIFactory.ScrollList(body, 16, 32);
            if (_quests == null)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted), 100);
                return;
            }

            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(_quests.ResetAtUnixMs) - DateTimeOffset.UtcNow;
            UIFactory.Height(UIFactory.Label(list, Loc.T("quests.reset", (int)left.TotalHours, left.Minutes), Theme.SmallSize, Theme.TextMuted), 60);

            foreach (QuestDto quest in _quests.Quests)
            {
                RectTransform card = Widgets.Card(list, 260, quest.Claimed ? Theme.BackgroundLight : Theme.Panel);
                UIFactory.Label(card, Loc.T("quest." + quest.Type, quest.Target), Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.ProgressBar(card, quest.Progress / (float)Math.Max(1, quest.Target), Theme.Success, out RectTransform _);
                if (quest.Claimed)
                {
                    UIFactory.Label(card, Loc.T("achievements.claimed"), Theme.SmallSize, Theme.Success, TextAnchor.MiddleRight);
                }
                else
                {
                    string id = quest.Id;
                    Button claim = UIFactory.Button(card, Loc.T("quests.claim", quest.Progress, quest.Target), () => _ = ClaimAsync(id), Theme.Gold, Theme.SmallSize);
                    claim.interactable = quest.Progress >= quest.Target;
                }
            }

            Widgets.SectionTitle(list, Loc.T("login.calendar"));
            ProfileDto profile = Game.Backend.Profile;
            int[] calendar = Game.Backend.Balance.LiveOps.LoginCalendar;
            for (int i = 0; i < calendar.Length; i++)
            {
                string reward = calendar[i] >= 0 ? Loc.T("currency.coins", calendar[i]) : Loc.T("currency.orbes", -calendar[i]);
                bool next = profile != null && profile.LoginCalendarSlot == i;
                UIFactory.Height(UIFactory.Label(list, Loc.T("login.day", i + 1) + "  " + reward, Theme.BodySize, next ? Theme.Gold : Theme.TextMuted, TextAnchor.MiddleLeft), 64);
            }
        }

        public override async Task OnShownAsync()
        {
            _quests = await Api(api => api.GetQuestsAsync(), loading: false);
            if (this != null)
            {
                Rebuild();
            }
        }

        private async Task ClaimAsync(string questId)
        {
            ClaimResponse claim = await Api(api => api.ClaimQuestAsync(questId));
            if (claim != null)
            {
                Game.Backend.ApplyWallet(claim.Wallet);
                Game.Audio.PlaySFX(Audio.SoundIds.Coins);
                UI.Toast(MainMenuScreen.RewardText(Loc, claim.Reward));
                await OnShownAsync();
            }
        }
    }
}
