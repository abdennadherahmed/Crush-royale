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
                UIFactory.Button(actions.transform, Loc.T("profile.view"), () => UI.Show<ProfileScreen>(id), Theme.Hex("5BA8FF"), Theme.SmallSize);
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

    /// <summary>Weekly leaderboards per league and for guilds: podium for the top 3, then framed rows, reset countdown.</summary>
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
            RectTransform tabs = UIFactory.Anchor(UIFactory.Rect("Tabs", body), 0.01f, 0.875f, 0.99f, 1f);
            GridLayoutGroup grid = tabs.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(255, 88);
            grid.spacing = new Vector2(10, 10);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            GridFit.On(grid);
            for (int i = 0; i < Leagues.Length; i++)
            {
                int index = i;
                string key = Leagues[i] == null ? "leaderboard.all" : Leagues[i] == "Guilds" ? "leaderboard.guilds" : "league." + Leagues[i];
                UIFactory.Button(tabs, Loc.T(key), () =>
                {
                    _tab = index;
                    _ = LoadAsync();
                }, i == _tab ? Theme.GoldDark : Theme.PanelLight, Theme.SmallSize - 2, Theme.Text);
            }

            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.865f);
            RectTransform list = UIFactory.ScrollList(holder, 12, 24);
            UIFactory.Stretch((RectTransform)list.parent.parent);

            var rows = new List<(int Rank, string Name, long Value, Color Color, bool Me)>();
            long resetAt;
            string mine;
            if (Leagues[_tab] == "Guilds")
            {
                if (_guilds == null)
                {
                    return;
                }
                resetAt = _guilds.ResetAtUnixMs;
                mine = Loc.T("leaderboard.myGuildRank", _guilds.MyGuildRank?.ToString() ?? "-");
                rows.AddRange(_guilds.Entries.Select(g => (g.Rank, g.Name, (long)g.TotalTrophies, Theme.Text, false)));
                _ids.Clear();
            }
            else
            {
                if (_players == null)
                {
                    return;
                }
                resetAt = _players.ResetAtUnixMs;
                mine = Loc.T("leaderboard.myRank", _players.MyRank?.ToString() ?? "-");
                rows.AddRange(_players.Entries.Select(e => (e.Rank, e.DisplayName, (long)e.Trophies, Theme.League(e.League), e.PlayerId == Game.Backend.PlayerId)));
                _ids = _players.Entries.GroupBy(e => e.Rank).ToDictionary(g => g.Key, g => g.First().PlayerId);
            }

            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(resetAt) - DateTimeOffset.UtcNow;
            Image info = UIFactory.Panel("Info", list, Theme.Panel);
            UIFactory.Height(info, 120);
            UiKit.CardFrame(info);
            Text mineText = UIFactory.Label(info.transform, mine, Theme.BodySize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(mineText.rectTransform, 0.05f, 0.1f, 0.5f, 0.9f);
            Widgets.TitleOutline(mineText);
            Sprite hourglass = UiKit.Art("item_hourglass");
            if (hourglass != null)
            {
                Image icon = UIFactory.Icon(info.transform, hourglass, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.5f, 0.12f, 0.58f, 0.88f);
            }
            Text reset = UIFactory.Label(info.transform, Loc.T("leaderboard.reset", left.Days, left.Hours), Theme.SmallSize - 2, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(reset.rectTransform, 0.59f, 0.1f, 0.97f, 0.9f);

            Podium(list, rows.Where(r => r.Rank <= 3).ToList());
            foreach (var row in rows.Where(r => r.Rank > 3))
            {
                RectTransform line = Line(list, row.Rank, row.Name, Loc.Number(row.Value), row.Color, row.Me);
                if (_ids.TryGetValue(row.Rank, out string playerId))
                {
                    Button open = line.gameObject.AddComponent<Button>();
                    open.targetGraphic = line.GetComponent<Image>();
                    open.onClick.AddListener(() => UI.Show<ProfileScreen>(playerId));
                    line.gameObject.AddComponent<ButtonFeedback>();
                }
            }
            if (rows.Count == 0)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("leaderboard.empty"), Theme.BodySize, Theme.TextMuted), 160);
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

        /// <summary>Top 3 on steps of different heights (2nd, 1st, 3rd) with a crown on the winner.</summary>
        private void Podium(Transform list, List<(int Rank, string Name, long Value, Color Color, bool Me)> top)
        {
            if (top.Count == 0)
            {
                return;
            }
            RectTransform podium = UIFactory.Rect("Podium", list);
            UIFactory.Height(podium, 560);
            int[] order = { 2, 1, 3 };
            float[] heights = { 0.42f, 0.56f, 0.32f };
            Color[] medals = { Theme.Hex("C0C7D6"), Theme.Gold, Theme.Hex("CD7F32") };
            for (int i = 0; i < 3; i++)
            {
                int rank = order[i];
                var entry = top.FirstOrDefault(r => r.Rank == rank);
                if (entry.Name == null)
                {
                    continue;
                }
                float x = i / 3f;
                Image step = UIFactory.Panel("Step", podium, Theme.Panel);
                UIFactory.Anchor(step.rectTransform, x + 0.015f, 0f, x + 0.318f, heights[i]);
                UiKit.CardFrame(step);
                step.gameObject.AddComponent<PopIn>().Delay = 0.1f * i;
                Text number = UIFactory.Label(step.transform, rank.ToString(), 96, medals[i], TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(number.rectTransform, 0.05f, 0.35f, 0.95f, 0.95f);
                Widgets.TitleOutline(number);
                Text score = UIFactory.Label(step.transform, Loc.Number(entry.Value), Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(score.rectTransform, 0.05f, 0.08f, 0.95f, 0.35f);

                float top0 = heights[i];
                Sprite art = rank == 1 ? UiKit.Art("item_crown") : UiKit.Art("item_medal");
                if (art != null)
                {
                    Image icon = UIFactory.Icon(podium, art, rank == 1 ? Color.white : medals[i], 0);
                    UIFactory.Anchor(icon.rectTransform, x + 0.08f, top0 + 0.14f, x + 0.25f, top0 + 0.36f);
                    icon.gameObject.AddComponent<Breathe>().Amount = 0.03f;
                    if (rank == 1)
                    {
                        Image glow = UIFactory.Icon(podium, ProceduralSprites.Glow(128), new Color(1f, 0.85f, 0.4f, 0.55f), 0);
                        UIFactory.Anchor(glow.rectTransform, x - 0.02f, top0 + 0.02f, x + 0.35f, top0 + 0.46f);
                        glow.transform.SetSiblingIndex(icon.transform.GetSiblingIndex());
                        glow.gameObject.AddComponent<Pulse>();
                    }
                }
                Text name = UIFactory.Label(podium, entry.Name, Theme.SmallSize, entry.Me ? Theme.Gold : entry.Color, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(name.rectTransform, x + 0.01f, top0 + 0.01f, x + 0.32f, top0 + 0.13f);
                Widgets.TitleOutline(name);
            }
        }

        private Dictionary<int, string> _ids = new Dictionary<int, string>();

        private static RectTransform Line(Transform list, int rank, string name, string value, Color color, bool me)
        {
            Image row = UIFactory.Panel("Row", list, Theme.Panel);
            UIFactory.Height(row, 110);
            UiKit.CardFrame(row);
            if (me)
            {
                Outline glow = row.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                glow.effectDistance = new Vector2(5, -5);
            }
            Text rankText = UIFactory.Label(row.transform, "#" + rank, Theme.BodySize, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(rankText.rectTransform, 0.02f, 0.1f, 0.16f, 0.9f);
            Text nameText = UIFactory.Label(row.transform, name, Theme.BodySize, me ? Theme.Gold : color, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(nameText.rectTransform, 0.18f, 0.1f, 0.68f, 0.9f);
            Sprite trophy = UiKit.Art("item_trophy") ?? ArtLibrary.Icon("trophy");
            if (trophy != null)
            {
                Image icon = UIFactory.Icon(row.transform, trophy, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.7f, 0.15f, 0.78f, 0.85f);
            }
            Text valueText = UIFactory.Label(row.transform, value, Theme.BodySize, Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Anchor(valueText.rectTransform, 0.78f, 0.1f, 0.96f, 0.9f);
            return row.rectTransform;
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
            int done = _data.Achievements.Count(a => a.Claimed);
            Image header = UIFactory.Panel("Header", body, Theme.Panel);
            UIFactory.Anchor(header.rectTransform, 0.02f, 0.85f, 0.98f, 1f);
            UiKit.FramePanel(header);
            Sprite trophy = UiKit.Art("item_trophy");
            if (trophy != null)
            {
                Image icon = UIFactory.Icon(header.transform, trophy, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.03f, 0.12f, 0.17f, 0.88f);
            }
            Text summary = UIFactory.Label(header.transform, Loc.T("achievements.summary", done, _data.Achievements.Count), Theme.BodySize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(summary.rectTransform, 0.19f, 0.5f, 0.97f, 0.88f);
            Widgets.TitleOutline(summary);
            Text bonus = UIFactory.Label(header.transform, Loc.T("achievements.bonus", Mathf.RoundToInt(_data.CoinBonus * 100)), Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft);
            UIFactory.Anchor(bonus.rectTransform, 0.19f, 0.12f, 0.97f, 0.5f);

            // Page selector: small round arrows around "Page n / N".
            RectTransform pager = UIFactory.Anchor(UIFactory.Rect("Pager", body), 0.15f, 0.775f, 0.85f, 0.84f);
            PageArrow(pager, "<", 0f, () => { _page = Math.Max(1, _page - 1); Rebuild(); }, _page > 1);
            PageArrow(pager, ">", 0.85f, () => { _page = Math.Min(pages, _page + 1); Rebuild(); }, _page < pages);
            Text pageText = UIFactory.Label(pager, Loc.T("achievements.pageShort", _page, pages), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(pageText.rectTransform, 0.16f, 0f, 0.84f, 1f);
            Widgets.TitleOutline(pageText);

            RectTransform holder = UIFactory.Anchor(UIFactory.Rect("Holder", body), 0, 0, 1, 0.77f);
            RectTransform list = UIFactory.ScrollList(holder, 16, 26);
            UIFactory.Stretch((RectTransform)list.parent.parent);

            bool pageDone = _data.PagesCompleted.Contains(_page);
            Image pageReward = UIFactory.Panel("PageReward", list, Theme.Panel);
            UIFactory.Height(pageReward, 110);
            UiKit.CardFrame(pageReward);
            Sprite gift = UiKit.Art(pageDone ? "item_stars" : "item_gift");
            if (gift != null)
            {
                Image icon = UIFactory.Icon(pageReward.transform, gift, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.03f, 0.1f, 0.13f, 0.9f);
            }
            Text rewardText = UIFactory.Label(pageReward.transform, Loc.T(pageDone ? "achievements.pageDone" : "achievements.pageReward", Loc.T("cosmetic.frame.page" + _page)),
                Theme.SmallSize, pageDone ? Theme.Success : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(rewardText.rectTransform, 0.15f, 0.05f, 0.97f, 0.95f);

            foreach (AchievementDto a in _data.Achievements.Where(x => x.Page == _page).OrderBy(x => x.Claimed).ThenByDescending(x => x.Unlocked))
            {
                AchievementCard(list, a);
            }
        }

        private static void PageArrow(RectTransform pager, string glyph, float x, Action onClick, bool enabled)
        {
            Button arrow = UIFactory.Button(pager, glyph, onClick, enabled ? Theme.Gold : Theme.PanelLight, Theme.BodySize);
            RectTransform rect = UIFactory.Anchor(arrow.GetComponent<RectTransform>(), x, 0f, x + 0.15f, 1f);
            rect.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            arrow.interactable = enabled;
        }

        private void AchievementCard(Transform list, AchievementDto a)
        {
            Image card = UIFactory.Panel("Achievement", list, Theme.Panel);
            UIFactory.Height(card, 300);
            UiKit.CardFrame(card);
            if (a.Claimed)
            {
                card.color = new Color(0.72f, 0.7f, 0.78f, 1f);
            }
            else if (a.Unlocked)
            {
                Outline glow = card.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                glow.effectDistance = new Vector2(6, -6);
            }

            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Medal", card.transform), 0.02f, 0.3f, 0.2f, 0.92f);
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            UiKit.RoundBadge(medal, crystal: !a.Unlocked);
            Sprite art = UiKit.Art(a.Unlocked ? "item_medal" : "item_lock");
            if (art != null)
            {
                Image icon = UIFactory.Icon(medal, art, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.2f, 0.15f, 0.8f, 0.85f);
            }

            Text title = UIFactory.Label(card.transform, Loc.T("ach." + a.Id + ".title"), Theme.BodySize, a.Unlocked ? Theme.Gold : Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.23f, 0.72f, 0.97f, 0.94f);
            Widgets.TitleOutline(title);
            Text desc = UIFactory.Label(card.transform, Loc.T("ach." + a.Id + ".desc", Loc.Number(a.Target)), Theme.SmallSize - 2, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(desc.rectTransform, 0.23f, 0.54f, 0.97f, 0.72f);

            UIFactory.ProgressBar(card.transform, a.Progress / (float)Math.Max(1, a.Target), a.Unlocked ? Theme.Success : Theme.Crystal, out RectTransform bar);
            UIFactory.Anchor(bar, 0.23f, 0.37f, 0.97f, 0.52f);
            Text count = UIFactory.Label(bar, Loc.Number(Math.Min(a.Progress, a.Target)) + " / " + Loc.Number(a.Target), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(count.rectTransform);
            Widgets.TitleOutline(count);

            RectTransform rewards = UIFactory.Anchor(UIFactory.Rect("Rewards", card.transform), 0.04f, 0.05f, 0.6f, 0.32f);
            Widgets.RewardChips(rewards, a.Reward);

            if (a.Claimed)
            {
                Text claimed = UIFactory.Label(card.transform, "✔ " + Loc.T("achievements.claimed"), Theme.SmallSize, Theme.Success, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.Anchor(claimed.rectTransform, 0.6f, 0.05f, 0.97f, 0.32f);
            }
            else if (a.Unlocked)
            {
                string id = a.Id;
                Button claim = UIFactory.Button(card.transform, Loc.T("achievements.claim"), () => _ = ClaimAsync(id), Theme.Success, Theme.SmallSize + 2);
                UIFactory.Anchor(claim.GetComponent<RectTransform>(), 0.62f, 0.04f, 0.97f, 0.33f);
                claim.gameObject.AddComponent<Breathe>().Amount = 0.03f;
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
            await RevealOverlay.PlayChestAsync(RevealOverlay.FromReward(claim.Reward), UiKit.Art("item_gift"), UiKit.Art("item_gift"), Loc.T("achievements.claim"));
            await OnShownAsync();
        }
    }

    /// <summary>
    /// Seasonal battle pass: XP to the next tier, how to earn pass XP (missions with "Go" buttons), and the tier track
    /// with free rewards on the left, premium on the right, the current tier highlighted.
    /// </summary>
    public sealed class BattlePassScreen : UIScreen
    {
        private BattlePassResponse _pass;
        private QuestsResponse _quests;

        public override Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("battlepass.title");
            if (_pass == null)
            {
                UIFactory.Label(body, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted);
                return;
            }

            RectTransform list = UIFactory.ScrollList(body, 16, 26);
            BuildStatus(list);
            BuildMissions(list);
            Widgets.SectionTitle(list, Loc.T("battlepass.rewards"));
            BuildTrackHeader(list);
            foreach (BattlePassTierDto tier in _pass.Tiers)
            {
                TierRow(list, tier);
            }
        }

        private void BuildStatus(Transform list)
        {
            LiveOpsBalanceView live = new LiveOpsBalanceView(_pass);
            Image panel = UIFactory.Panel("Status", list, Theme.Panel);
            UIFactory.Height(panel, 380);
            UiKit.FramePanel(panel);

            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(_pass.SeasonEndUnixMs) - DateTimeOffset.UtcNow;
            Text season = UIFactory.Label(panel.transform, Loc.T("battlepass.seasonShort", _pass.Season, Math.Max(0, left.Days)), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(season.rectTransform, 0.05f, 0.8f, 0.95f, 0.94f);

            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Tier", panel.transform), 0.04f, 0.3f, 0.3f, 0.82f);
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            UiKit.RoundBadge(medal, crystal: false);
            Text tier = UIFactory.Label(medal, _pass.Tier.ToString(), Theme.TitleSize + 10, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(tier.rectTransform);
            Widgets.TitleOutline(tier);

            bool maxed = _pass.Tier >= _pass.Tiers.Count;
            Text title = UIFactory.Label(panel.transform, maxed ? Loc.T("battlepass.maxed") : Loc.T("battlepass.nextTier", _pass.Tier + 1), Theme.BodySize + 4, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.33f, 0.6f, 0.97f, 0.8f);
            Widgets.TitleOutline(title);
            UIFactory.ProgressBar(panel.transform, maxed ? 1f : live.IntoTier / (float)Math.Max(1, _pass.XpPerTier), Theme.Crystal, out RectTransform bar);
            UIFactory.Anchor(bar, 0.33f, 0.42f, 0.96f, 0.58f);
            Text xp = UIFactory.Label(bar, maxed ? "MAX" : Loc.T("battlepass.xpProgress", live.IntoTier, _pass.XpPerTier), Theme.SmallSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(xp.rectTransform);
            Widgets.TitleOutline(xp);
            if (!maxed)
            {
                Text missing = UIFactory.Label(panel.transform, Loc.T("battlepass.xpMissing", _pass.XpPerTier - live.IntoTier), Theme.SmallSize - 2, Theme.Text, TextAnchor.MiddleLeft);
                UIFactory.Anchor(missing.rectTransform, 0.33f, 0.3f, 0.97f, 0.42f);
            }

            if (!_pass.Premium)
            {
                Button premium = UIFactory.Button(panel.transform, Loc.T("battlepass.unlockPremium"), () => UI.Show<ShopScreen>(3), Theme.Gold, Theme.BodySize);
                UIFactory.Anchor(premium.GetComponent<RectTransform>(), 0.12f, 0.05f, 0.88f, 0.26f);
                premium.gameObject.AddComponent<Breathe>().Amount = 0.02f;
                Sprite crown = UiKit.Art("item_ticket");
                if (crown != null)
                {
                    Image icon = UIFactory.Icon(premium.transform, crown, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, -0.06f, -0.1f, 0.14f, 1.1f);
                }
            }
            else
            {
                Text premium = UIFactory.Label(panel.transform, "✔ " + Loc.T("battlepass.premiumActive"), Theme.BodySize, Theme.Success, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(premium.rectTransform, 0.05f, 0.05f, 0.95f, 0.26f);
            }
        }

        /// <summary>How to earn pass XP: daily quests (with live progress) and every match mode, each with a "Go" button.</summary>
        private void BuildMissions(Transform list)
        {
            Widgets.SectionTitle(list, Loc.T("battlepass.missions"));
            var live = Game.Backend.Balance.LiveOps;
            if (_quests != null)
            {
                foreach (QuestDto quest in _quests.Quests)
                {
                    Mission(list, "item_medal", Loc.T("quest." + quest.Type, quest.Target), live.XpQuest, quest.Claimed ? 1f : quest.Progress / (float)Math.Max(1, quest.Target),
                        quest.Claimed ? "✔" : quest.Progress + "/" + quest.Target, () => UI.Show<QuestsScreen>());
                }
            }
            Mission(list, "item_stars", Loc.T("battlepass.missionStory"), live.XpStoryWin, -1f, null, () => UI.Show<WorldMapScreen>());
            Mission(list, "item_trophy", Loc.T("battlepass.missionPvp"), live.XpPvpWin, -1f, null, () => UI.Show<PvpScreen>());
            Mission(list, "item_bolt", Loc.T("battlepass.missionBoss"), live.XpGuildBossAttack, -1f, null, () => UI.Show<GuildScreen>());
        }

        private void Mission(Transform list, string icon, string text, int xp, float progress, string progressText, Action go)
        {
            Image row = UIFactory.Panel("Mission", list, Theme.Panel);
            UIFactory.Height(row, 150);
            UiKit.CardFrame(row);
            Sprite art = UiKit.Art(icon);
            if (art != null)
            {
                Image image = UIFactory.Icon(row.transform, art, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.02f, 0.12f, 0.13f, 0.88f);
            }
            Text label = UIFactory.Label(row.transform, text, Theme.SmallSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.15f, progress >= 0 ? 0.48f : 0.1f, 0.62f, 0.92f);
            if (progress >= 0)
            {
                UIFactory.ProgressBar(row.transform, progress, Theme.Success, out RectTransform bar);
                UIFactory.Anchor(bar, 0.15f, 0.14f, 0.62f, 0.44f);
                Text count = UIFactory.Label(bar, progressText, Theme.SmallSize - 8, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(count.rectTransform);
            }
            Sprite xpArt = UiKit.Art("item_xp");
            if (xpArt != null)
            {
                Image xpIcon = UIFactory.Icon(row.transform, xpArt, Color.white, 0);
                UIFactory.Anchor(xpIcon.rectTransform, 0.63f, 0.2f, 0.71f, 0.8f);
            }
            Text xpText = UIFactory.Label(row.transform, "+" + xp, Theme.BodySize, Theme.Crystal, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(xpText.rectTransform, 0.71f, 0.1f, 0.8f, 0.9f);
            Widgets.TitleOutline(xpText);
            Button button = UIFactory.Button(row.transform, Loc.T("get.go"), go, Theme.Success, Theme.SmallSize);
            UIFactory.Anchor(button.GetComponent<RectTransform>(), 0.81f, 0.2f, 0.98f, 0.8f);
        }

        private void BuildTrackHeader(Transform list)
        {
            RectTransform header = UIFactory.Rect("TrackHeader", list);
            UIFactory.Height(header, 70);
            Text free = UIFactory.Label(header, Loc.T("battlepass.free"), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(free.rectTransform, 0f, 0f, 0.42f, 1f);
            Text premium = UIFactory.Label(header, Loc.T("battlepass.premium"), Theme.BodySize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(premium.rectTransform, 0.58f, 0f, 1f, 1f);
            Widgets.TitleOutline(free);
            Widgets.TitleOutline(premium);
        }

        private RectTransform TierRow(Transform list, BattlePassTierDto tier)
        {
            bool reached = _pass.Tier >= tier.Tier;
            RectTransform row = UIFactory.Rect("Tier" + tier.Tier, list);
            UIFactory.Height(row, 230);

            // Path in the middle: a line filled up to the current tier, a medal per tier.
            Image line = UIFactory.Panel("Line", row, reached ? Theme.Gold : Theme.BackgroundLight, rounded: false);
            UIFactory.Anchor(line.rectTransform, 0.485f, -0.05f, 0.515f, 1.05f);
            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Medal", row), 0.43f, 0.3f, 0.57f, 0.7f);
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.WidthControlsHeight;
            UiKit.RoundBadge(medal, crystal: !reached);
            Text number = UIFactory.Label(medal, tier.Tier.ToString(), Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(number.rectTransform);
            Widgets.TitleOutline(number);
            if (tier.Tier == _pass.Tier + 1)
            {
                medal.gameObject.AddComponent<Pulse>().Scale = true;
            }

            RewardCell(row, tier, premium: false, 0.01f, 0.42f, reached);
            RewardCell(row, tier, premium: true, 0.58f, 0.99f, reached && _pass.Premium);
            return row;
        }

        private void RewardCell(RectTransform row, BattlePassTierDto tier, bool premium, float minX, float maxX, bool claimable)
        {
            RewardDto reward = premium ? tier.Premium : tier.Free;
            bool claimed = premium ? tier.PremiumClaimed : tier.FreeClaimed;
            Image cell = UIFactory.Panel(premium ? "Premium" : "Free", row, Theme.Panel);
            UIFactory.Anchor(cell.rectTransform, minX, 0.05f, maxX, 0.95f);
            UiKit.CardFrame(cell);
            if (!claimable && !claimed)
            {
                cell.color = new Color(0.7f, 0.68f, 0.78f, 1f);
            }

            List<RevealItem> items = RevealOverlay.FromReward(reward);
            RevealItem first = items.Count > 0 ? items[0] : null;
            if (first?.Art != null)
            {
                Image icon = UIFactory.Icon(cell.transform, first.Art, claimed ? new Color(1f, 1f, 1f, 0.5f) : Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.05f, 0.25f, 0.45f, 0.95f);
            }
            string caption = string.Join("\n", items.Select(i => i.Caption));
            Text label = UIFactory.Label(cell.transform, caption, Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.47f, 0.25f, 0.97f, 0.95f);

            if (premium && !_pass.Premium)
            {
                Sprite lockArt = UiKit.Art("item_lock");
                if (lockArt != null)
                {
                    Image lockIcon = UIFactory.Icon(cell.transform, lockArt, Color.white, 0);
                    UIFactory.Anchor(lockIcon.rectTransform, 0.78f, 0.62f, 0.98f, 1.05f);
                }
            }
            if (claimed)
            {
                Text done = UIFactory.Label(cell.transform, "✔", Theme.HeaderSize, Theme.Success, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(done.rectTransform, 0.3f, 0.02f, 0.7f, 0.3f);
                Widgets.TitleOutline(done);
            }
            else if (claimable)
            {
                int t = tier.Tier;
                Button claim = UIFactory.Button(cell.transform, Loc.T("achievements.claim"), () => _ = ClaimAsync(t, premium), Theme.Success, Theme.SmallSize - 4);
                UIFactory.Anchor(claim.GetComponent<RectTransform>(), 0.1f, 0.03f, 0.9f, 0.3f);
                claim.gameObject.AddComponent<Breathe>().Amount = 0.03f;
            }
        }

        public override async Task OnShownAsync()
        {
            _pass = await Api(api => api.GetBattlePassAsync(), loading: false);
            if (Widgets.FeatureUnlocked("DailyQuests"))
            {
                _quests = await Api(api => api.GetQuestsAsync(), loading: false);
            }
            if (this != null)
            {
                Rebuild();
            }
        }

        private async Task ClaimAsync(int tier, bool premium)
        {
            ClaimResponse claim = await Api(api => api.ClaimBattlePassAsync(tier, premium));
            if (claim != null)
            {
                Game.Backend.ApplyWallet(claim.Wallet);
                await RevealOverlay.PlayChestAsync(RevealOverlay.FromReward(claim.Reward), UiKit.Art("item_ticket"), UiKit.Art("item_ticket"), Loc.T("battlepass.tier", tier));
                await OnShownAsync();
            }
        }

        /// <summary>XP inside the current tier.</summary>
        private readonly struct LiveOpsBalanceView
        {
            public LiveOpsBalanceView(BattlePassResponse pass)
            {
                IntoTier = (int)Math.Max(0, Math.Min(pass.XpPerTier, pass.Xp - pass.Tier * (long)pass.XpPerTier));
            }

            public int IntoTier { get; }
        }
    }

    /// <summary>Daily quests (icons, progress, rewards incl. pass XP) and the 7-day login calendar.</summary>
    public sealed class QuestsScreen : UIScreen
    {
        private QuestsResponse _quests;

        public override Type BackTarget => typeof(MainMenuScreen);

        private static string QuestIcon(string type)
        {
            switch (type)
            {
                case "WinStages": return "item_stars";
                case "EarnStars": return "item_stars";
                case "TriggerCascades": return "item_bolt";
                case "CreateSpecials": return "item_fragment";
                case "BreakStones": return "item_pouch";
                case "UsePowerUps": return "item_bolt";
                case "PlayPvp": return "item_trophy";
                case "WinPvp": return "item_trophy";
                case "AttackGuildBoss": return "item_medal";
                default: return "item_medal";
            }
        }

        protected override void Build()
        {
            RectTransform body = Frame("quests.title");
            RectTransform list = UIFactory.ScrollList(body, 16, 28);
            if (_quests == null)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("common.loading"), Theme.BodySize, Theme.TextMuted), 100);
                return;
            }

            TimeSpan left = DateTimeOffset.FromUnixTimeMilliseconds(_quests.ResetAtUnixMs) - DateTimeOffset.UtcNow;
            Image info = UIFactory.Panel("Reset", list, Theme.Panel);
            UIFactory.Height(info, 110);
            UiKit.CardFrame(info);
            Sprite hourglass = UiKit.Art("item_hourglass");
            if (hourglass != null)
            {
                Image icon = UIFactory.Icon(info.transform, hourglass, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.03f, 0.1f, 0.11f, 0.9f);
            }
            Text reset = UIFactory.Label(info.transform, Loc.T("quests.reset", (int)left.TotalHours, left.Minutes), Theme.SmallSize + 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(reset.rectTransform, 0.13f, 0.05f, 0.97f, 0.95f);

            var live = Game.Backend.Balance.LiveOps;
            foreach (QuestDto quest in _quests.Quests.OrderBy(q => q.Claimed).ThenByDescending(q => q.Progress >= q.Target))
            {
                QuestCard(list, quest, live.QuestRewardCoins, live.XpQuest);
            }

            Widgets.SectionTitle(list, Loc.T("login.calendar"));
            BuildCalendar(list);
        }

        private void QuestCard(Transform list, QuestDto quest, int coins, int xp)
        {
            bool ready = !quest.Claimed && quest.Progress >= quest.Target;
            Image card = UIFactory.Panel("Quest", list, Theme.Panel);
            UIFactory.Height(card, 270);
            UiKit.CardFrame(card);
            if (quest.Claimed)
            {
                card.color = new Color(0.72f, 0.7f, 0.78f, 1f);
            }
            else if (ready)
            {
                Outline glow = card.gameObject.AddComponent<Outline>();
                glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                glow.effectDistance = new Vector2(6, -6);
            }

            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Icon", card.transform), 0.02f, 0.25f, 0.2f, 0.9f);
            medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            UiKit.RoundBadge(medal, crystal: !ready && !quest.Claimed);
            Sprite art = UiKit.Art(QuestIcon(quest.Type));
            if (art != null)
            {
                Image icon = UIFactory.Icon(medal, art, Color.white, 0);
                UIFactory.Anchor(icon.rectTransform, 0.18f, 0.18f, 0.82f, 0.82f);
            }

            Text title = UIFactory.Label(card.transform, Loc.T("quest." + quest.Type, quest.Target), Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.23f, 0.66f, 0.97f, 0.93f);
            Widgets.TitleOutline(title);
            UIFactory.ProgressBar(card.transform, quest.Claimed ? 1f : quest.Progress / (float)Math.Max(1, quest.Target), Theme.Success, out RectTransform bar);
            UIFactory.Anchor(bar, 0.23f, 0.44f, 0.97f, 0.62f);
            Text count = UIFactory.Label(bar, Math.Min(quest.Progress, quest.Target) + " / " + quest.Target, Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(count.rectTransform);
            Widgets.TitleOutline(count);

            RectTransform rewards = UIFactory.Anchor(UIFactory.Rect("Rewards", card.transform), 0.04f, 0.06f, 0.6f, 0.36f);
            Widgets.RewardChips(rewards, new RewardDto { Coins = coins, BattlePassXp = xp });

            if (quest.Claimed)
            {
                Text claimed = UIFactory.Label(card.transform, "✔ " + Loc.T("achievements.claimed"), Theme.SmallSize, Theme.Success, TextAnchor.MiddleRight, FontStyle.Bold);
                UIFactory.Anchor(claimed.rectTransform, 0.6f, 0.06f, 0.97f, 0.38f);
            }
            else
            {
                string id = quest.Id;
                Button claim = UIFactory.Button(card.transform, Loc.T("achievements.claim"), () => _ = ClaimAsync(id), ready ? Theme.Success : Theme.PanelLight, Theme.SmallSize + 2);
                UIFactory.Anchor(claim.GetComponent<RectTransform>(), 0.62f, 0.05f, 0.97f, 0.39f);
                claim.interactable = ready;
                if (ready)
                {
                    claim.gameObject.AddComponent<Breathe>().Amount = 0.03f;
                }
            }
        }

        private void BuildCalendar(Transform list)
        {
            ProfileDto profile = Game.Backend.Profile;
            int[] calendar = Game.Backend.Balance.LiveOps.LoginCalendar;
            RectTransform gridRect = UIFactory.Rect("Calendar", list);
            int rows = (calendar.Length + 3) / 4;
            UIFactory.Height(gridRect, rows * 250 + (rows - 1) * 14);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(236, 250);
            grid.spacing = new Vector2(14, 14);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            GridFit.On(grid);
            grid.childAlignment = TextAnchor.UpperCenter;
            int slot = profile?.LoginCalendarSlot ?? -1;
            for (int i = 0; i < calendar.Length; i++)
            {
                bool next = slot == i;
                bool past = slot > i;
                Image tile = UIFactory.Panel("Day", gridRect, Theme.Panel);
                UiKit.CardFrame(tile);
                if (past)
                {
                    tile.color = new Color(0.65f, 0.63f, 0.72f, 1f);
                }
                if (next)
                {
                    Outline glow = tile.gameObject.AddComponent<Outline>();
                    glow.effectColor = new Color(1f, 0.82f, 0.3f, 0.95f);
                    glow.effectDistance = new Vector2(6, -6);
                    tile.gameObject.AddComponent<Breathe>().Amount = 0.02f;
                }
                Text day = UIFactory.Label(tile.transform, Loc.T("login.day", i + 1), Theme.SmallSize, next ? Theme.Gold : Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(day.rectTransform, 0.05f, 0.76f, 0.95f, 0.96f);
                Widgets.TitleOutline(day);
                bool orbes = calendar[i] < 0;
                Sprite art = UiKit.Art(orbes ? "item_orbs" : "item_coins");
                if (art != null)
                {
                    Image icon = UIFactory.Icon(tile.transform, art, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, 0.18f, 0.28f, 0.82f, 0.76f);
                }
                Text amount = UIFactory.Label(tile.transform, past ? "✔" : Loc.Number(Math.Abs(calendar[i])), Theme.BodySize, past ? Theme.Success : orbes ? Theme.Orbe : Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(amount.rectTransform, 0.05f, 0.04f, 0.95f, 0.3f);
                Widgets.TitleOutline(amount);
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
                await RevealOverlay.PlayChestAsync(RevealOverlay.FromReward(claim.Reward), UiKit.Art("item_gift"), UiKit.Art("item_gift"), Loc.T("quests.title"));
                await OnShownAsync();
            }
        }
    }
}
