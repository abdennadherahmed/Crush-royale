using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Story;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.Gameplay;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CrushRoyale.Game.Screens
{
    /// <summary>Gameplay HUD for story, PvP ghost play, friendly challenges and guild boss attacks.</summary>
    public sealed class GameplayScreen : UIScreen
    {
        private MatchLaunch _launch;
        private MatchController _controller;
        private Text _score;
        private Text _moves;
        private Text _time;
        private Text _objectives;
        private Image _meterFill;
        private Image _bossFill;
        private Text _surge;
        private Text _opponentScore;
        private readonly List<(PowerUpType Type, Button Button, Text Label)> _powerUps = new List<(PowerUpType, Button, Text)>();
        private RectTransform _pausePanel;
        private bool _submitting;

        public override System.Type BackTarget => null;

        protected override void Build()
        {
            _launch = (MatchLaunch)Args;
            AddBackdrop();
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeArea>();

            // HUD
            Image hud = UIFactory.Panel("Hud", safe, Theme.Panel);
            UIFactory.Anchor(hud.rectTransform, 0.02f, 0.86f, 0.98f, 0.99f);
            _moves = UIFactory.Label(hud.transform, string.Empty, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(_moves.rectTransform, 0.14f, 0.1f, 0.38f, 0.9f);
            _score = UIFactory.Label(hud.transform, "0", Theme.TitleSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_score.rectTransform, 0.36f, 0.1f, 0.66f, 0.9f);
            _time = UIFactory.Label(hud.transform, string.Empty, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UIFactory.Anchor(_time.rectTransform, 0.66f, 0.1f, 0.96f, 0.9f);

            Button pause = UIFactory.Button(hud.transform, "II", OpenPause, Theme.PanelLight, Theme.BodySize, Theme.Text);
            UIFactory.Anchor(pause.GetComponent<RectTransform>(), 0.01f, 0.15f, 0.12f, 0.85f);

            // Objectives / boss / opponent row
            _objectives = UIFactory.Label(safe, string.Empty, Theme.SmallSize + 2, Theme.TextMuted, TextAnchor.MiddleLeft);
            UIFactory.Anchor(_objectives.rectTransform, 0.04f, 0.79f, 0.7f, 0.86f);
            _bossFill = UIFactory.ProgressBar(safe, 1f, Theme.Danger, out RectTransform bossBar);
            UIFactory.Anchor(bossBar, 0.04f, 0.765f, 0.7f, 0.785f);
            bossBar.gameObject.SetActive(false);

            _meterFill = UIFactory.ProgressBar(safe, 0f, Theme.RedSurge, out RectTransform meter);
            UIFactory.Anchor(meter, 0.04f, 0.735f, 0.7f, 0.755f);
            _surge = UIFactory.Label(safe, Loc.T("hud.surge"), Theme.SmallSize, Theme.RedSurge, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(_surge.rectTransform, 0.04f, 0.70f, 0.7f, 0.735f);
            _surge.enabled = false;

            // Board
            BoardView board = BoardView.Create(safe, 1000f, ghost: false);
            board.Rect.anchorMin = board.Rect.anchorMax = new Vector2(0.5f, 0.43f);
            board.Rect.anchoredPosition = Vector2.zero;
            BoardInput input = board.gameObject.AddComponent<BoardInput>();

            // Ghost
            BoardView ghostBoard = null;
            GhostPlayer ghost = _launch.CreateGhost(Game.Backend);
            if (ghost != null)
            {
                ghostBoard = BoardView.Create(safe, 230f, ghost: true);
                ghostBoard.Rect.anchorMin = ghostBoard.Rect.anchorMax = new Vector2(0.85f, 0.775f);
                ghostBoard.Rect.anchoredPosition = Vector2.zero;
                _opponentScore = UIFactory.Label(safe, "0", Theme.SmallSize + 4, Theme.Crystal, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(_opponentScore.rectTransform, 0.72f, 0.695f, 0.98f, 0.72f);
            }

            // Power-ups
            RectTransform bar = UIFactory.Anchor(UIFactory.Rect("PowerUps", safe), 0.02f, 0.02f, 0.98f, 0.12f);
            HorizontalLayoutGroup row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16;
            row.childForceExpandWidth = true;
            row.childControlWidth = true;
            row.childControlHeight = true;

            SessionConfig config;
            try
            {
                config = _launch.BuildConfig(Game.Backend);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                _ = UI.Alert(Loc.T("error.title"), Loc.T("error.config"));
                return;
            }

            var session = new GameSession(config, Game.Backend.Balance, Game.Backend.PlayerId);
            foreach (LoadoutEntry entry in config.Loadout)
            {
                PowerUpType type = entry.Type;
                Button button = UIFactory.Button(bar, Loc.T("powerup." + type), () => OnPowerUp(type), Theme.PanelLight, Theme.SmallSize, Theme.Text);
                Widgets.AddPowerUpIcon(button, type);
                _powerUps.Add((type, button, button.GetComponentInChildren<Text>()));
            }
            if (config.Loadout.Count == 0)
            {
                UIFactory.Label(bar, Loc.T("hud.noPowerUps"), Theme.SmallSize, Theme.TextMuted);
            }

            bossBar.gameObject.SetActive(config.BossHp > 0);

            _controller = gameObject.AddComponent<MatchController>();
            _controller.Changed += RefreshHud;
            _controller.Finished += result => _ = OnFinishedAsync(result);
            _controller.Begin(session, board, input, ghost, ghostBoard);
            input.TargetPicked += cell => _controller.UsePowerUp(PowerUpType.NuclearBomb, cell);
        }

        public override async Task OnShownAsync()
        {
            if (_controller == null)
            {
                return;
            }

            if (_launch.Mode == GameMode.Story)
            {
                StageData stage = Game.Backend.Catalog.Get(_launch.StageId);
                Game.Audio.PlayMusic(stage.IsBoss ? SoundIds.PvpMusic : SoundIds.StoryMusicForAct(stage.Act));

                ProfileDto profile = Game.Backend.Profile;
                var seen = new HashSet<string>(profile?.Story?.SeenEvents ?? new List<string>());
                List<StoryEvent> before = StoryDatabase.GetEvents(Game.Backend.Balance.Story)
                    .Where(e => e.StageId == stage.Id && e.Trigger == StoryEventTrigger.BeforeStage && !seen.Contains(e.Id))
                    .ToList();
                if (before.Count > 0)
                {
                    _controller.Pause();
                    foreach (StoryEvent e in before)
                    {
                        await DialogueOverlay.PlayAsync(UI, Loc, e.DialogueId);
                        if (Game.Backend.IsOnline)
                        {
                            _ = Game.Backend.Client.Api.MarkEventSeenAsync(e.Id);
                            profile?.Story?.SeenEvents.Add(e.Id);
                        }
                    }
                    _controller.Resume();
                }
            }
            else
            {
                Game.Audio.PlayMusic(SoundIds.PvpMusic);
            }
        }

        public override bool HandleBack()
        {
            OpenPause();
            return true;
        }

        private void RefreshHud()
        {
            if (_controller?.Session == null)
            {
                return;
            }
            GameSession s = _controller.Session;
            int now = _controller.Clock.NowMs;

            _score.text = Loc.Number(s.Score);
            _moves.text = s.Config.HasMoveLimit ? Loc.T("hud.moves", s.MovesLeft) : string.Empty;
            int seconds = Mathf.CeilToInt(s.RemainingTimeMs(now) / 1000f);
            _time.text = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            _time.color = seconds <= 10 ? Theme.Danger : Theme.Text;

            UIFactory.SetProgress(_meterFill, s.ComboMeterPermille / 1000f);
            _surge.enabled = s.IsRedSurgeActive(now);

            var parts = new List<string>();
            foreach (ObjectiveProgress p in s.Objectives.Progress)
            {
                string label = p.Objective.Type == ObjectiveType.CollectColor
                    ? Loc.T("objective.CollectColor", Loc.T("color." + p.Objective.Color))
                    : Loc.T("objective." + p.Objective.Type);
                parts.Add(label + " " + Loc.Number(System.Math.Min(p.Current, p.Target)) + "/" + Loc.Number(p.Target));
            }
            _objectives.text = string.Join("   ", parts);

            if (s.Config.BossHp > 0)
            {
                UIFactory.SetProgress(_bossFill, 1f - Mathf.Clamp01(s.Score / (float)s.Config.BossHp));
            }
            if (_opponentScore != null && _controller.Ghost != null)
            {
                _opponentScore.text = Loc.T("hud.opponent", Loc.Number(_controller.Ghost.CurrentScore));
            }

            foreach ((PowerUpType type, Button button, Text label) in _powerUps)
            {
                ErrorCode can = s.PowerUps.CanActivate(type, true);
                button.interactable = s.IsRunning && can == ErrorCode.None;
                label.text = Loc.T("powerup." + type) + " x" + s.PowerUps.Remaining(type);
            }
        }

        /// <summary>Kingdom illustration behind the whole screen (story: the stage's kingdom; PvP and guild boss: Crystalheim).</summary>
        private void AddBackdrop()
        {
            Kingdom kingdom = Kingdom.Central;
            if (_launch.Mode == GameMode.Story && _launch.StageId > 0)
            {
                kingdom = Game.Backend.Catalog.Get(_launch.StageId).Kingdom;
            }

            Widgets.Backdrop(Root, kingdom);
        }

        private void Update()
        {
            if (_controller?.Session != null && _controller.Session.IsRunning && Time.frameCount % 10 == 0)
            {
                RefreshHud();
            }
        }

        private void OnPowerUp(PowerUpType type)
        {
            PowerUpDefinition def = Game.Backend.Balance.PowerUps.Get(type);
            if (def.NeedsTarget)
            {
                _controller.Input.TargetingMode = true;
                UI.Toast(Loc.T("hud.pickTarget"));
                return;
            }
            _controller.UsePowerUp(type, null);
        }

        private void OpenPause()
        {
            if (_pausePanel != null || _controller == null)
            {
                return;
            }
            _controller.Pause();
            _pausePanel = UI.Popup(0.35f, 0.65f);
            Text title = UIFactory.Label(_pausePanel, Loc.T(_controller.IsPaused ? "hud.paused" : "hud.noPause"), Theme.HeaderSize, Theme.Gold);
            UIFactory.Anchor(title.rectTransform, 0.05f, 0.7f, 0.95f, 0.95f);

            Button resume = UIFactory.Button(_pausePanel, Loc.T("pause.resume"), ClosePause);
            UIFactory.Anchor(resume.GetComponent<RectTransform>(), 0.15f, 0.4f, 0.85f, 0.62f);
            Button quit = UIFactory.Button(_pausePanel, Loc.T("pause.quit"), () => _ = QuitAsync(), Theme.Danger);
            UIFactory.Anchor(quit.GetComponent<RectTransform>(), 0.15f, 0.1f, 0.85f, 0.32f);
        }

        private void ClosePause()
        {
            if (_pausePanel != null)
            {
                Destroy(_pausePanel.parent.gameObject);
                _pausePanel = null;
            }
            _controller.Resume();
        }

        private async Task QuitAsync()
        {
            bool confirm = await UI.Confirm(Loc.T("pause.quit"), Loc.T(_launch.Mode == GameMode.Story ? "pause.quitStory" : "pause.quitPvp"));
            if (!confirm)
            {
                return;
            }
            ClosePause();
            _controller.Quit();
            await OnFinishedAsync(_controller.Session.GetResult());
        }

        private async Task OnFinishedAsync(StageResult result)
        {
            if (_submitting)
            {
                return;
            }
            GameSession session = _controller.Session;

            if (_launch.Mode == GameMode.Story && result.State == SessionState.Lost && !_launch.Offline && Game.Backend.IsOnline)
            {
                if (await OfferContinueAsync(session))
                {
                    return;
                }
            }

            _submitting = true;
            Game.Audio.PlaySFX(result.State == SessionState.Won || result.State == SessionState.Completed ? SoundIds.WinFanfare : SoundIds.LoseJingle);

            switch (_launch.Mode)
            {
                case GameMode.Story:
                    await SubmitStoryAsync(result);
                    break;
                case GameMode.GuildBoss:
                    await SubmitBossAsync(result);
                    break;
                default:
                    await SubmitPvpAsync(result);
                    break;
            }
        }

        private async Task<bool> OfferContinueAsync(GameSession session)
        {
            ProfileDto profile = Game.Backend.Profile;
            if (session.ContinuesUsed >= SessionConfig.MaxContinues)
            {
                return false;
            }
            bool free = profile?.Lives?.FreeContinues > 0;
            int price = ContinuePrice(session.ContinuesUsed);
            string body = free
                ? Loc.T("continue.free", Game.Backend.Balance.Stamina.ContinueExtraMoves)
                : Loc.T("continue.body", Game.Backend.Balance.Stamina.ContinueExtraMoves, price);

            if (!await UI.Confirm(Loc.T("continue.title"), body, Loc.T("continue.accept"), Loc.T("continue.decline")))
            {
                return false;
            }

            ContinueResponse response = await Api(api => api.ContinueStageAsync(_launch.Ticket.MatchId, free));
            if (response == null)
            {
                return false;
            }
            Game.Backend.ApplyWallet(response.Wallet);
            if (profile?.Lives != null && response.Free)
            {
                profile.Lives.FreeContinues = 0;
            }
            return _controller.Continue();
        }

        private int ContinuePrice(int alreadyBought)
        {
            long price = Game.Backend.Balance.Stamina.ContinueBaseOrbes;
            for (int i = 0; i < alreadyBought; i++)
            {
                price = price * Game.Backend.Balance.Stamina.OrbeEscalationPermille / 1000;
            }
            return (int)price;
        }

        private async Task SubmitStoryAsync(StageResult result)
        {
            var args = new StoryResultArgs { StageId = _launch.StageId, Local = result, Offline = _launch.Offline || !Game.Backend.IsOnline };
            if (args.Offline && result.Won)
            {
                PlayerSettings settings = Game.Save.Settings;
                settings.LastSeenStage = Mathf.Min(WorldMapScreen.OfflineStageCap, Mathf.Max(settings.LastSeenStage, _launch.StageId + 1));
                Game.Save.SaveSettings();
            }
            if (!args.Offline)
            {
                using (UI.Loading())
                {
                    try
                    {
                        args.Server = await Game.Backend.Client.SubmitStageAsync(_launch.Ticket.MatchId, _controller.Session.Replay);
                        args.Queued = args.Server == null;
                    }
                    catch (CrushApiException ex)
                    {
                        UI.ShowError(ex);
                    }
                }
                if (args.Server != null)
                {
                    Game.Backend.ApplyWallet(args.Server.Wallet);
                    Game.Backend.ApplyLives(args.Server.Lives);
                    _ = Game.Ads.ShowInterstitialIfRequested(args.Server.ShowInterstitial);
                }
            }
            UI.Show<StoryResultScreen>(args, addToHistory: false);
        }

        private async Task SubmitPvpAsync(StageResult result)
        {
            var args = new PvpResultArgs
            {
                Launch = _launch,
                Local = result,
                OpponentScore = _controller.Ghost?.CurrentScore ?? 0,
                Offline = _launch.Offline || !Game.Backend.IsOnline
            };
            if (!args.Offline)
            {
                using (UI.Loading())
                {
                    try
                    {
                        args.Server = await Game.Backend.Client.SubmitPvpAsync(_launch.Ticket.MatchId, _controller.Session.Replay);
                        args.Queued = args.Server == null;
                    }
                    catch (CrushApiException ex)
                    {
                        UI.ShowError(ex);
                    }
                }
                if (args.Server != null)
                {
                    Game.Backend.ApplyWallet(args.Server.Wallet);
                    _ = Game.Ads.ShowInterstitialIfRequested(args.Server.ShowInterstitial);
                }
            }
            UI.Show<PvpResultScreen>(args, addToHistory: false);
        }

        private async Task SubmitBossAsync(StageResult result)
        {
            GuildBossAttackResponse response = null;
            using (UI.Loading())
            {
                try
                {
                    response = await Game.Backend.Client.SubmitGuildBossAsync(_launch.Ticket.MatchId, _controller.Session.Replay);
                }
                catch (CrushApiException ex)
                {
                    UI.ShowError(ex);
                }
            }

            string message = response == null
                ? Loc.T("boss.queued")
                : response.Accepted
                    ? Loc.T(response.DefeatedNow ? "boss.defeated" : "boss.damage", Loc.Number(response.Damage), response.AttacksLeft)
                    : Loc.T("error." + response.Error);
            await UI.Alert(Loc.T("boss.title"), message);
            UI.ShowRoot<GuildScreen>();
        }
    }
}
