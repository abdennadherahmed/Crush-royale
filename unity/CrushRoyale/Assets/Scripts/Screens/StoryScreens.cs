using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>World map: 5 kingdoms (acts), 10 chapters each, 20 stages per chapter, stars and friends' positions.</summary>
    public sealed class WorldMapScreen : UIScreen
    {
        /// <summary>Offline practice lets players try the first stages without an account.</summary>
        public const int OfflineStageCap = 50;

        private int _act = -1;
        private RectTransform _list;
        private Dictionary<int, List<string>> _friendsByStage = new Dictionary<int, List<string>>();

        public override System.Type BackTarget => typeof(MainMenuScreen);

        // Acts 1-5 are the kingdoms in enum order (North, East, West, South, Central).
        protected override Kingdom BackdropKingdom => (Kingdom)Mathf.Clamp(_act - 1, 0, 4);

        private int HighestUnlocked => Game.Backend.IsOnline
            ? Game.Backend.Profile.Story.HighestUnlockedStage
            : Mathf.Clamp(Game.Save.Settings.LastSeenStage, 1, OfflineStageCap);

        protected override void Build()
        {
            StoryBalance story = Game.Backend.Balance.Story;
            if (_act < 1)
            {
                _act = Mathf.Clamp((Mathf.Min(HighestUnlocked, story.TotalStages) - 1) / (story.ChaptersPerAct * story.StagesPerChapter) + 1, 1, story.Acts);
            }

            RectTransform body = Frame("map.title");
            RectTransform container = UIFactory.Stretch(UIFactory.Rect("Container", body));

            var labels = new List<string>();
            for (int a = 1; a <= story.Acts; a++)
            {
                labels.Add(Loc.T("kingdom." + (Kingdom)(a - 1) + ".short"));
            }
            RectTransform tabsHolder = UIFactory.Anchor(UIFactory.Rect("Tabs", container), 0.02f, 0.9f, 0.98f, 1f);
            VerticalLayoutGroup tabLayout = tabsHolder.gameObject.AddComponent<VerticalLayoutGroup>();
            tabLayout.childControlWidth = tabLayout.childControlHeight = true;
            tabLayout.childForceExpandWidth = true;
            System.Action<int> setTab = Widgets.Tabs(tabsHolder, labels, index =>
            {
                _act = index + 1;
                Rebuild();
            });
            setTab(_act - 1);

            RectTransform listHolder = UIFactory.Anchor(UIFactory.Rect("ListHolder", container), 0, 0, 1, 0.89f);
            _list = UIFactory.ScrollList(listHolder, 20, 24);
            UIFactory.Stretch((RectTransform)_list.parent.parent);
            FillAct(story);
        }

        public override async Task OnShownAsync()
        {
            if (!Game.Backend.IsOnline || !Widgets.FeatureUnlocked("Friends"))
            {
                return;
            }
            WorldMapResponse map = await Api(api => api.GetWorldMapAsync(), loading: false);
            if (map == null || this == null)
            {
                return;
            }
            _friendsByStage = map.Friends.GroupBy(f => f.Stage).ToDictionary(g => g.Key, g => g.Select(f => f.DisplayName).ToList());
            Rebuild();
        }

        private void FillAct(StoryBalance story)
        {
            int highest = HighestUnlocked;
            string stars = Game.Backend.Profile?.Story?.StarsByStage ?? string.Empty;
            int firstChapter = (_act - 1) * story.ChaptersPerAct + 1;

            Text kingdom = UIFactory.Label(_list, Loc.T("kingdom." + (Kingdom)(_act - 1)), Theme.HeaderSize, Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Height(kingdom, 100);

            for (int chapter = firstChapter; chapter < firstChapter + story.ChaptersPerAct; chapter++)
            {
                int firstStage = (chapter - 1) * story.StagesPerChapter + 1;
                bool chapterVisible = firstStage <= highest + story.StagesPerChapter;
                Widgets.SectionTitle(_list, Loc.T("chapter.title", chapter, Loc.T("chapter." + chapter + ".name")));
                if (!chapterVisible)
                {
                    UIFactory.Height(UIFactory.Label(_list, Loc.T("map.chapterLocked"), Theme.SmallSize, Theme.TextMuted), 60);
                    continue;
                }

                RectTransform gridRect = UIFactory.Rect("Stages", _list);
                UIFactory.Height(gridRect, 4 * 190 + 3 * 16);
                GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(180, 190);
                grid.spacing = new Vector2(16, 16);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 5;
                grid.childAlignment = TextAnchor.UpperCenter;

                for (int id = firstStage; id < firstStage + story.StagesPerChapter; id++)
                {
                    StageButton(gridRect, id, highest, stars);
                }
            }
        }

        private void StageButton(Transform parent, int stageId, int highest, string stars)
        {
            bool unlocked = stageId <= highest && (Game.Backend.IsOnline || stageId <= OfflineStageCap);
            bool current = stageId == highest;
            int starCount = stageId - 1 < stars.Length ? stars[stageId - 1] - '0' : 0;
            StageData stage = Game.Backend.Catalog.Get(stageId);

            Color color = !unlocked ? Theme.BackgroundLight : current ? Theme.Crystal : starCount > 0 ? Theme.GoldDark : Theme.PanelLight;
            string label = stageId + "\n" + (stage.IsBoss ? Loc.T("map.boss") : new string('*', starCount));
            if (_friendsByStage.TryGetValue(stageId, out List<string> friends))
            {
                label += "\n" + string.Join(",", friends.Select(f => f.Substring(0, 1)));
            }

            int id = stageId;
            Button button = UIFactory.Button(parent, label, () =>
            {
                if (unlocked)
                {
                    UI.Show<StagePreviewScreen>(id);
                }
                else
                {
                    UI.Toast(Loc.T("map.stageLocked"));
                }
            }, color, Theme.SmallSize + 2, current ? Theme.Background : Theme.Text);
            button.interactable = true;
        }
    }

    /// <summary>Stage details, objectives, power-up loadout and play (consumes a life server-side).</summary>
    public sealed class StagePreviewScreen : UIScreen
    {
        private int _stageId;
        private readonly HashSet<string> _selected = new HashSet<string>();

        protected override Kingdom BackdropKingdom => Game.Backend.Catalog.Get(_stageId).Kingdom;

        protected override void Build()
        {
            _stageId = Args is int id ? id : 1;
            StageData stage = Game.Backend.Catalog.Get(_stageId);
            RectTransform body = Frame("stage.title");
            RectTransform list = UIFactory.ScrollList(body, 20, 40);
            UIFactory.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 240);

            Text title = UIFactory.Label(list, Loc.T("stage.heading", stage.Id, Loc.T("kingdom." + stage.Kingdom)), Theme.HeaderSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Height(title, 100);
            if (stage.IsBoss)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("boss." + stage.BossKind) + " - " + Loc.T(stage.BossId + ".name"), Theme.BodySize, Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold), 80);
                Sprite bossArt = ArtLibrary.Boss(stage);
                if (bossArt != null)
                {
                    UIFactory.Height(UIFactory.Icon(list, bossArt, Color.white, 360), 360);
                }
            }

            RectTransform info = Widgets.Card(list, 260);
            UIFactory.Label(info, Loc.T("stage.limits", stage.MoveLimit, stage.TimeLimitMs / 1000), Theme.BodySize);
            UIFactory.Label(info, Loc.T("stage.difficulty", Mathf.RoundToInt(stage.DifficultyPercent)), Theme.BodySize, Theme.TextMuted);
            UIFactory.Label(info, Loc.T("stage.stars", Loc.Number(stage.TargetScore), Loc.Number(stage.TwoStarScore), Loc.Number(stage.ThreeStarScore)), Theme.SmallSize, Theme.TextMuted);

            Widgets.SectionTitle(list, Loc.T("stage.objectives"));
            foreach (StageObjective objective in stage.Objectives)
            {
                string text = objective.Type == ObjectiveType.CollectColor
                    ? Loc.T("objective.CollectColor", Loc.T("color." + objective.Color)) + " " + objective.Target
                    : Loc.T("objective." + objective.Type) + (objective.Target > 0 ? " " + Loc.Number(objective.Target) : string.Empty);
                UIFactory.Height(UIFactory.Label(list, "- " + text, Theme.BodySize, Theme.Text, TextAnchor.MiddleLeft), 70);
            }
            if (stage.StoneCount > 0 || stage.IceCells > 0)
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("stage.obstacles", stage.StoneCount, stage.IceCells), Theme.SmallSize, Theme.TextMuted, TextAnchor.MiddleLeft), 60);
            }

            if (Game.Backend.IsOnline)
            {
                BuildLoadout(list);
                UIFactory.Height(UIFactory.Label(list, Loc.T("stage.rewards", stage.RewardCoins, stage.RewardOrbes), Theme.BodySize, Theme.Gold), 70);
            }
            else
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("result.practice"), Theme.SmallSize, Theme.Warning), 90);
            }

            Button play = UIFactory.Button(body, Loc.T("stage.play"), () => _ = PlayAsync());
            UIFactory.Anchor(play.GetComponent<RectTransform>(), 0.15f, 0.02f, 0.85f, 0.11f);
        }

        private void BuildLoadout(Transform list)
        {
            ProfileDto profile = Game.Backend.Profile;
            GameBalance balance = Game.Backend.Balance;
            Widgets.SectionTitle(list, Loc.T("stage.loadout", balance.PowerUps.LoadoutSlots));

            League highest = (League)System.Enum.Parse(typeof(League), profile.Pvp.HighestLeague);
            RectTransform gridRect = UIFactory.Rect("Loadout", list);
            UIFactory.Height(gridRect, 3 * 150 + 2 * 16);
            GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(300, 150);
            grid.spacing = new Vector2(16, 16);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            foreach (PowerUpDefinition def in balance.PowerUps.Definitions)
            {
                string type = def.Type.ToString();
                int count = profile.Inventory.PowerUps.TryGetValue(type, out int n) ? n : 0;
                bool locked = highest < def.UnlockLeague || def.PvpOnly;
                Button button = null;
                button = UIFactory.Button(gridRect, Loc.T("powerup." + type) + "\nx" + count, () =>
                {
                    if (_selected.Remove(type))
                    {
                        button.GetComponent<Image>().color = Theme.PanelLight;
                    }
                    else if (_selected.Count < balance.PowerUps.LoadoutSlots)
                    {
                        _selected.Add(type);
                        button.GetComponent<Image>().color = Theme.GoldDark;
                    }
                }, Theme.PanelLight, Theme.SmallSize, Theme.Text);
                Widgets.AddPowerUpIcon(button, def.Type);
                button.interactable = !locked && count > 0;
            }
        }

        private async Task PlayAsync()
        {
            if (!Game.Backend.IsOnline)
            {
                UI.Show<GameplayScreen>(MatchLaunch.OfflineStory(_stageId), addToHistory: false);
                return;
            }

            ProfileDto profile = Game.Backend.Profile;
            if (profile.Lives.Lives <= 0)
            {
                bool buy = await UI.Confirm(Loc.T("lives.emptyTitle"), Loc.T("lives.emptyBody", profile.Lives.NextLifeCoins, profile.Lives.NextLifeOrbes), Loc.T("lives.buy"));
                if (!buy)
                {
                    return;
                }
                PurchaseResponse bought = await Api(api => api.BuyLivesAsync(1));
                if (bought == null)
                {
                    return;
                }
                Game.Backend.ApplyWallet(bought.Wallet);
                Game.Backend.ApplyLives(bought.Lives);
            }

            MatchStartResponse ticket = await Api(api => api.StartStageAsync(_stageId, new StartStageRequest { Loadout = _selected.ToList() }));
            if (ticket == null)
            {
                return;
            }
            Game.Backend.ApplyLives(ticket.Lives);
            if (ticket.BalanceHash != Game.Backend.Client.BalanceHash)
            {
                await Game.Backend.Client.RefreshConfigAsync();
            }
            UI.Show<GameplayScreen>(MatchLaunch.Online(ticket), addToHistory: false);
        }
    }

    /// <summary>Ranked PvP lobby: loadout, matchmaking with widening range, versus screen. Offline: practice against a bot ghost.</summary>
    public sealed class PvpScreen : UIScreen
    {
        private readonly HashSet<string> _selected = new HashSet<string>();
        private Text _status;
        private Button _find;
        private bool _searching;

        public override System.Type BackTarget => typeof(MainMenuScreen);

        protected override void Build()
        {
            RectTransform body = Frame("pvp.title");
            RectTransform list = UIFactory.ScrollList(body, 20, 40);
            UIFactory.Stretch((RectTransform)list.parent.parent, 0, 0, 0, 380);

            ProfileDto profile = Game.Backend.Profile;
            if (profile != null)
            {
                Text league = UIFactory.Label(list, Loc.T("league." + profile.Pvp.League), 90, Theme.League(profile.Pvp.League), TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Height(league, 140);
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.record", profile.Pvp.Trophies, profile.Pvp.Wins, profile.Pvp.Losses, profile.Pvp.WinStreak), Theme.BodySize), 80);
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.rules"), Theme.SmallSize, Theme.TextMuted), 150);

                Widgets.SectionTitle(list, Loc.T("stage.loadout", Game.Backend.Balance.PowerUps.LoadoutSlots));
                League highest = (League)System.Enum.Parse(typeof(League), profile.Pvp.HighestLeague);
                RectTransform gridRect = UIFactory.Rect("Loadout", list);
                UIFactory.Height(gridRect, 3 * 150 + 2 * 16);
                GridLayoutGroup grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(300, 150);
                grid.spacing = new Vector2(16, 16);
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 3;
                foreach (PowerUpDefinition def in Game.Backend.Balance.PowerUps.Definitions)
                {
                    string type = def.Type.ToString();
                    int count = profile.Inventory.PowerUps.TryGetValue(type, out int n) ? n : 0;
                    Button button = null;
                    button = UIFactory.Button(gridRect, Loc.T("powerup." + type) + "\nx" + count, () =>
                    {
                        if (_selected.Remove(type))
                        {
                            button.GetComponent<Image>().color = Theme.PanelLight;
                        }
                        else if (_selected.Count < Game.Backend.Balance.PowerUps.LoadoutSlots)
                        {
                            _selected.Add(type);
                            button.GetComponent<Image>().color = Theme.GoldDark;
                        }
                    }, Theme.PanelLight, Theme.SmallSize, Theme.Text);
                    Widgets.AddPowerUpIcon(button, def.Type);
                    button.interactable = highest >= def.UnlockLeague && count > 0;
                }
            }
            else
            {
                UIFactory.Height(UIFactory.Label(list, Loc.T("pvp.practiceBody"), Theme.BodySize, Theme.Warning), 200);
            }

            _status = UIFactory.Label(body, string.Empty, Theme.BodySize, Theme.Crystal);
            UIFactory.Anchor(_status.rectTransform, 0.05f, 0.13f, 0.95f, 0.19f);
            _find = UIFactory.Button(body, Loc.T(profile != null ? "pvp.find" : "pvp.practice"), () => _ = FindAsync());
            UIFactory.Anchor(_find.GetComponent<RectTransform>(), 0.15f, 0.02f, 0.85f, 0.11f);
        }

        public override bool HandleBack()
        {
            if (!_searching)
            {
                return false;
            }
            _ = CancelAsync();
            return true;
        }

        private async Task FindAsync()
        {
            if (_searching)
            {
                await CancelAsync();
                return;
            }

            if (!Game.Backend.IsOnline)
            {
                await PlayBotAsync(League.Bronze);
                return;
            }

            MatchmakingStatusResponse status = await Api(api => api.RequestMatchAsync(new MatchmakingRequest { Loadout = _selected.ToList() }));
            if (status == null)
            {
                return;
            }

            _searching = true;
            bool offered = false;
            _find.GetComponentInChildren<Text>().text = Loc.T("common.cancel");
            while (_searching && this != null)
            {
                _status.text = Loc.T("pvp.searching", status.WaitedMs / 1000, status.Range);
                if (status.Status == "Matched" && status.Match != null)
                {
                    _searching = false;
                    await ShowVersusAsync(status.Match);
                    UI.Show<GameplayScreen>(MatchLaunch.Online(status.Match), addToHistory: false);
                    return;
                }
                if (status.Status == "TimedOut" || status.Status == "Cancelled" || status.Status == "None")
                {
                    _searching = false;
                    _status.text = Loc.T("pvp.noOpponent");
                    _find.GetComponentInChildren<Text>().text = Loc.T("pvp.find");
                    await OfferBotAsync();
                    return;
                }
                if (status.WaitedMs >= BotOfferAfterMs && !offered)
                {
                    offered = true;
                    _ = OfferBotWhileSearchingAsync();
                }

                await Task.Delay(1000);
                try
                {
                    status = await Game.Backend.Client.Api.GetMatchmakingStatusAsync();
                }
                catch (CrushApiException ex)
                {
                    if (!ex.IsNetwork)
                    {
                        UI.ShowError(ex);
                        _searching = false;
                    }
                }
            }
        }

        /// <summary>Few players online (launch, night): after this wait a training bot is offered instead of an empty queue.</summary>
        private const int BotOfferAfterMs = 20000;

        private async Task OfferBotWhileSearchingAsync()
        {
            if (await UI.Confirm(Loc.T("pvp.botTitle"), Loc.T("pvp.botBody")) && _searching && this != null)
            {
                await CancelAsync();
                await PlayBotAsync(CurrentLeague());
            }
        }

        private async Task OfferBotAsync()
        {
            if (await UI.Confirm(Loc.T("pvp.botTitle"), Loc.T("pvp.botBody")) && this != null)
            {
                await PlayBotAsync(CurrentLeague());
            }
        }

        private League CurrentLeague()
        {
            ProfileDto profile = Game.Backend.Profile;
            return profile != null && System.Enum.TryParse(profile.Pvp.League, out League league) ? league : League.Bronze;
        }

        /// <summary>Training duel (no trophies) against a human-paced bot of the given league.</summary>
        private async Task PlayBotAsync(League league)
        {
            using (UI.Loading())
            {
                ulong seed = (ulong)Random.Range(1, int.MaxValue) * 2654435761UL;
                MatchLaunch launch = await Task.Run(() => MatchLaunch.OfflinePvp(Game.Backend.Balance, seed, league));
                UI.Show<GameplayScreen>(launch, addToHistory: false);
            }
        }

        private async Task CancelAsync()
        {
            _searching = false;
            if (_status != null)
            {
                _status.text = string.Empty;
                _find.GetComponentInChildren<Text>().text = Loc.T("pvp.find");
            }
            await Api(api => api.CancelMatchmakingAsync(), loading: false);
        }

        private async Task ShowVersusAsync(MatchStartResponse match)
        {
            RectTransform box = UI.Popup(0.3f, 0.7f);
            ProfileDto me = Game.Backend.Profile;
            GhostDto ghost = match.Ghost;

            Text versus = UIFactory.Label(box, "VS", 140, Theme.RedSurge, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(versus.rectTransform, 0.3f, 0.35f, 0.7f, 0.65f);
            Text left = UIFactory.Label(box, me.DisplayName + "\n" + Loc.T("league." + me.Pvp.League) + " " + me.Pvp.Trophies, Theme.BodySize, Theme.League(me.Pvp.League));
            UIFactory.Anchor(left.rectTransform, 0.02f, 0.66f, 0.98f, 0.95f);

            string opponent = ghost != null
                ? ghost.OpponentName + "\n" + Loc.T("league." + ghost.OpponentLeague) + " " + ghost.OpponentTrophies + "\n" + string.Join(", ", ghost.OpponentLoadout.Select(l => Loc.T("powerup." + l.Type)))
                : Loc.T("pvp.liveOpponent");
            Text right = UIFactory.Label(box, opponent, Theme.BodySize, Theme.Crystal);
            UIFactory.Anchor(right.rectTransform, 0.02f, 0.05f, 0.98f, 0.34f);

            Game.Audio.PlaySFX(SoundIds.RedSurge);
            await Task.Delay(2500);
            if (box != null)
            {
                Destroy(box.parent.gameObject);
            }
        }
    }
}
