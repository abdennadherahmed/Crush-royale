using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Board;
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
        private RectTransform _petIcon;
        private RectTransform _goalRow;
        private RectTransform _movesMedal;
        private StageLife _life;
        private readonly List<(Text Count, GameObject Check)> _goalChips = new List<(Text, GameObject)>();

        public override System.Type BackTarget => null;

        protected override void Build()
        {
            _launch = (MatchLaunch)Args;
            AddBackdrop();
            RectTransform safe = UIFactory.Stretch(UIFactory.Rect("Safe", Root));
            safe.gameObject.AddComponent<SafeArea>();

            Widgets.Fade(Root, true, 0.2f, 0.75f);

            // HUD: ornate top bar with moves in a gold medallion, score in the middle, timer on the right.
            Image hud = UIFactory.Panel("Hud", safe, Theme.Panel);
            UIFactory.Anchor(hud.rectTransform, 0.02f, 0.865f, 0.98f, 0.99f);
            UiKit.FramePanel(hud);

            Button pause = UIFactory.Button(hud.transform, "II", OpenPause, Theme.PanelLight, Theme.BodySize, Theme.Text);
            UIFactory.Anchor(pause.GetComponent<RectTransform>(), 0.03f, 0.2f, 0.13f, 0.8f);

            RectTransform medal = UIFactory.Anchor(UIFactory.Rect("Moves", hud.transform), 0.15f, -0.08f, 0.33f, 1.08f);
            _movesMedal = medal;
            if (UiKit.RoundBadge(medal, crystal: false) != null)
            {
                medal.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            }
            _moves = UIFactory.Label(medal, string.Empty, Theme.TitleSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_moves.rectTransform, 0.1f, 0.3f, 0.9f, 0.85f);
            Widgets.TitleOutline(_moves);
            Text movesCaption = UIFactory.Label(medal, Loc.T("hud.movesLabel"), Theme.SmallSize - 6, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(movesCaption.rectTransform, 0.1f, 0.1f, 0.9f, 0.34f);
            Widgets.TitleOutline(movesCaption);

            Text scoreCaption = UIFactory.Label(hud.transform, Loc.T("hud.scoreLabel"), Theme.SmallSize - 4, Theme.TextMuted, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(scoreCaption.rectTransform, 0.36f, 0.62f, 0.68f, 0.88f);
            _score = UIFactory.Label(hud.transform, "0", Theme.TitleSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_score.rectTransform, 0.34f, 0.1f, 0.7f, 0.68f);
            Widgets.TitleOutline(_score);

            Sprite hourglass = UiKit.Art("item_hourglass");
            if (hourglass != null)
            {
                Image clock = UIFactory.Icon(hud.transform, hourglass, Color.white, 0);
                UIFactory.Anchor(clock.rectTransform, 0.71f, 0.18f, 0.79f, 0.82f);
            }
            _time = UIFactory.Label(hud.transform, string.Empty, Theme.HeaderSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_time.rectTransform, 0.79f, 0.1f, 0.97f, 0.9f);
            Widgets.TitleOutline(_time);

            // Objectives: big icon chips (gem, ice, stone, stars, boss) with a live counter and a check mark when done.
            Image goals = UIFactory.Panel("Goals", safe, Theme.Panel);
            UIFactory.Anchor(goals.rectTransform, 0.02f, 0.775f, 0.7f, 0.86f);
            UiKit.CardFrame(goals);
            _goalRow = goals.rectTransform;
            _objectives = UIFactory.Label(goals.transform, string.Empty, Theme.BodySize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(_objectives.rectTransform, 24, 24, 8, 8);

            _bossFill = UIFactory.ProgressBar(safe, 1f, Theme.Danger, out RectTransform bossBar);
            UIFactory.Anchor(bossBar, 0.03f, 0.742f, 0.69f, 0.772f);
            bossBar.gameObject.SetActive(false);

            _meterFill = UIFactory.ProgressBar(safe, 0f, Theme.RedSurge, out RectTransform meter);
            UIFactory.Anchor(meter, 0.03f, 0.71f, 0.69f, 0.74f);
            _surge = UIFactory.Label(safe, Loc.T("hud.surge"), Theme.BodySize, Theme.RedSurge, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(_surge.rectTransform, 0.03f, 0.705f, 0.69f, 0.745f);
            Widgets.TitleOutline(_surge);
            _surge.gameObject.AddComponent<Pulse>().Scale = true;
            _surge.enabled = false;

            // Board
            // Slightly smaller board: room below it for the pet and the hero, and it fits narrow (tall) phones.
            BoardView board = BoardView.Create(safe, 940f, ghost: false);
            board.Rect.anchorMin = board.Rect.anchorMax = new Vector2(0.5f, 0.445f);
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
            PowerUpType? petGift = session.PowerUps.PetGift;
            if (petGift.HasValue && !config.Loadout.Exists(e => e.Type == petGift.Value))
            {
                PowerUpType type = petGift.Value;
                Button button = UIFactory.Button(bar, Loc.T("powerup." + type), () => OnPowerUp(type), Theme.Hex("2F6B4F"), Theme.SmallSize, Theme.Text);
                Widgets.AddPowerUpIcon(button, type);
                _powerUps.Add((type, button, button.GetComponentInChildren<Text>()));
            }
            if (config.Loadout.Count == 0 && !petGift.HasValue)
            {
                UIFactory.Label(bar, Loc.T("hud.noPowerUps"), Theme.SmallSize, Theme.TextMuted);
            }

            bossBar.gameObject.SetActive(config.BossHp > 0);
            _goalRow.gameObject.SetActive(session.Objectives.Progress.Count > 0);

            _controller = gameObject.AddComponent<MatchController>();
            _controller.Changed += RefreshHud;
            _controller.Finished += result => _ = OnFinishedAsync(result);
            _controller.Begin(session, board, input, ghost, ghostBoard);
            _life = StageLife.Attach(Root, safe, board, _controller, _launch, config);
            board.ApplySkin(Game.Backend.Profile?.Inventory?.EquippedBoardSkin);
            _controller.ActionPresented += outcome =>
            {
                if (outcome.PetMove.HasValue && _petIcon != null)
                {
                    StartCoroutine(HopPet());
                }
            };
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

                await ShowGoalBannerAsync();
                if (this == null)
                {
                    return;
                }
                _controller.Pause();
                await _life.PlayIntroAsync();
                if (this == null)
                {
                    return;
                }
                _controller.Resume();

                if (stage.Id == 1 && !Game.Save.Settings.TutorialDone)
                {
                    await RunTutorialAsync();
                }
            }
            else
            {
                Game.Audio.PlayMusic(SoundIds.PvpMusic);
                await _life.PlayIntroAsync();
            }
        }

        /// <summary>Stage 1 only, three short tips from Lyra (everyone knows match-3: no forced moves).</summary>
        private async Task RunTutorialAsync()
        {
            _controller.Pause();
            Move? hint = _controller.Session.BoardManager.GetHint();
            if (hint.HasValue)
            {
                _controller.Board.ShowHint(hint.Value);
            }
            Game.Telemetry.Track("tutorial_step", ("step", 1));
            await CoachAsync("hud.tutoSwap");
            if (this == null)
            {
                return;
            }
            _controller.Resume();

            var moved = new TaskCompletionSource<bool>();
            System.Action<ActionOutcome> onMove = _ => moved.TrySetResult(true);
            _controller.ActionPresented += onMove;
            await moved.Task;
            if (this == null || _controller == null)
            {
                return;
            }
            _controller.ActionPresented -= onMove;
            if (!_controller.Session.IsRunning)
            {
                return;
            }

            await Task.Delay(500);
            if (this == null)
            {
                return;
            }
            _controller.Pause();
            await CoachAsync("hud.tutoBonus");
            await CoachAsync("hud.tutoSurge");
            if (this == null)
            {
                return;
            }
            _controller.Resume();
            Game.Save.Settings.TutorialDone = true;
            Game.Save.SaveSettings();
            Game.Telemetry.Track("tutorial_done");
        }

        /// <summary>Speech bubble above the board (Lyra portrait), dismissed with a tap anywhere.</summary>
        private async Task CoachAsync(string key)
        {
            var done = new TaskCompletionSource<bool>();
            Image shade = UIFactory.Panel("Coach", Root, new Color(0f, 0f, 0f, 0.3f), rounded: false);
            UIFactory.Stretch(shade.rectTransform);
            Button tap = shade.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(() => done.TrySetResult(true));

            Image bubble = UIFactory.Panel("Bubble", shade.transform, Theme.Panel);
            UIFactory.Anchor(bubble.rectTransform, 0.03f, 0.715f, 0.97f, 0.875f);

            Sprite lyra = ArtLibrary.Character("lyra");
            float textLeft = 0.05f;
            if (lyra != null)
            {
                Image portrait = UIFactory.Icon(bubble.transform, lyra, Color.white, 0);
                portrait.preserveAspect = true;
                UIFactory.Anchor(portrait.rectTransform, -0.02f, -0.05f, 0.24f, 1.25f);
                textLeft = 0.25f;
            }
            Text speaker = UIFactory.Label(bubble.transform, Loc.T("char.lyra"), Theme.BodySize, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(speaker.rectTransform, textLeft, 0.74f, 0.97f, 0.97f);
            Text text = UIFactory.Label(bubble.transform, Loc.T(key), Theme.SmallSize + 4, Theme.Text, TextAnchor.UpperLeft);
            UIFactory.Anchor(text.rectTransform, textLeft, 0.2f, 0.97f, 0.74f);
            Text next = UIFactory.Label(bubble.transform, Loc.T("hud.tutoTap"), Theme.SmallSize, Theme.Crystal, TextAnchor.LowerRight);
            UIFactory.Anchor(next.rectTransform, 0.4f, 0.03f, 0.97f, 0.22f);

            Game.Audio.PlaySFX(SoundIds.Click);
            StartCoroutine(PopIn(bubble.rectTransform));
            await done.Task;
            if (shade != null)
            {
                Destroy(shade.gameObject);
            }
        }

        private static System.Collections.IEnumerator PopIn(RectTransform rect)
        {
            for (float t = 0; t < 1f && rect != null; t += Time.unscaledDeltaTime / 0.22f)
            {
                float k = 1f + 0.12f * Mathf.Sin(t * Mathf.PI) - 0.2f * (1f - t);
                rect.localScale = Vector3.one * k;
                yield return null;
            }
            if (rect != null)
            {
                rect.localScale = Vector3.one;
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
            _moves.text = s.Config.HasMoveLimit ? Loc.Number(s.MovesLeft) : "∞";
            _moves.color = s.Config.HasMoveLimit && s.MovesLeft <= 5 ? Theme.Danger : Theme.Text;
            int seconds = Mathf.CeilToInt(s.RemainingTimeMs(now) / 1000f);
            _time.text = (seconds / 60) + ":" + (seconds % 60).ToString("00");
            _time.color = seconds <= 10 ? Theme.Danger : Theme.Text;

            UIFactory.SetProgress(_meterFill, s.ComboMeterPermille / 1000f);
            _surge.enabled = s.IsRedSurgeActive(now);

            if (_goalChips.Count == 0 && s.Objectives.Progress.Count > 0)
            {
                BuildGoalChips(_goalRow, s, 92f, Theme.HeaderSize, _goalChips);
                _objectives.text = string.Empty;
            }
            for (int i = 0; i < _goalChips.Count && i < s.Objectives.Progress.Count; i++)
            {
                ObjectiveProgress p = s.Objectives.Progress[i];
                _goalChips[i].Count.text = GoalCount(p);
                _goalChips[i].Count.color = p.IsComplete ? Theme.Success : Theme.Text;
                _goalChips[i].Check.SetActive(p.IsComplete);
            }

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

        private static string GoalCount(ObjectiveProgress p) =>
            GameRoot.Instance.Loc.Number(System.Math.Min(p.Current, p.Target)) + "/" + GameRoot.Instance.Loc.Number(p.Target);

        private Sprite GoalIcon(StageObjective objective)
        {
            switch (objective.Type)
            {
                case ObjectiveType.CollectColor: return ArtLibrary.Gem(objective.Color);
                case ObjectiveType.ClearIce: return ArtLibrary.Ice();
                case ObjectiveType.BreakStones: return ArtLibrary.Stone();
                case ObjectiveType.DefeatBoss:
                    return (_launch.Mode == GameMode.Story && _launch.StageId > 0 ? ArtLibrary.Boss(Game.Backend.Catalog.Get(_launch.StageId)) : null)
                        ?? ArtLibrary.GuildBoss();
                default: return UiKit.Art("item_stars") ?? ArtLibrary.Icon("star");
            }
        }

        /// <summary>One chip per objective, laid out side by side: icon, big counter, check mark once reached.</summary>
        private void BuildGoalChips(RectTransform parent, GameSession s, float iconSize, int fontSize, List<(Text Count, GameObject Check)> into)
        {
            RectTransform row = UIFactory.Stretch(UIFactory.Rect("Chips", parent), 20, 20, 6, 6);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 18;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;

            foreach (ObjectiveProgress p in s.Objectives.Progress)
            {
                RectTransform chip = UIFactory.Rect("Chip", row);
                Sprite art = GoalIcon(p.Objective);
                float split = 0.42f;
                if (art != null)
                {
                    Image icon = UIFactory.Icon(chip, art, Color.white, 0);
                    UIFactory.Anchor(icon.rectTransform, 0f, 0.05f, split, 0.95f);
                    icon.rectTransform.sizeDelta = Vector2.zero;
                    split = Mathf.Clamp(iconSize / 400f, 0.3f, 0.45f);
                    UIFactory.Anchor(icon.rectTransform, 0f, 0.05f, split, 0.95f);
                }
                else
                {
                    split = 0f;
                }
                Text count = UIFactory.Label(chip, GoalCount(p), fontSize, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
                UIFactory.Anchor(count.rectTransform, split + 0.03f, 0f, 1f, 1f);
                Widgets.TitleOutline(count);

                Text check = UIFactory.Label(chip, "✔", fontSize, Theme.Success, TextAnchor.LowerLeft, FontStyle.Bold);
                UIFactory.Anchor(check.rectTransform, split * 0.55f, -0.05f, split + 0.3f, 0.6f);
                Widgets.TitleOutline(check);
                check.gameObject.SetActive(false);
                into.Add((count, check.gameObject));
            }
        }

        /// <summary>Stage start: a big "OBJECTIVE" window with the goals and the move budget, closed by a tap or after a few seconds.</summary>
        private async Task ShowGoalBannerAsync()
        {
            GameSession s = _controller.Session;
            if (s.Objectives.Progress.Count == 0)
            {
                return;
            }
            _controller.Pause();
            var done = new TaskCompletionSource<bool>();
            Image shade = UIFactory.Panel("GoalBanner", Root, new Color(0.02f, 0.01f, 0.06f, 0.6f), rounded: false);
            UIFactory.Stretch(shade.rectTransform);
            Button tap = shade.gameObject.AddComponent<Button>();
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(() => done.TrySetResult(true));

            Image box = UIFactory.Panel("Box", shade.transform, Theme.Panel);
            UIFactory.Anchor(box.rectTransform, 0.06f, 0.4f, 0.94f, 0.64f);
            UiKit.FramePanel(box);
            box.gameObject.AddComponent<PopIn>();

            RectTransform ribbon = UIFactory.Anchor(UIFactory.Rect("Ribbon", box.transform), -0.05f, 0.8f, 1.05f, 1.14f);
            UiKit.Ribbon(ribbon);
            Text title = UIFactory.Label(ribbon, Loc.T("hud.goalTitle"), Theme.TitleSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(title.rectTransform, 0.2f, 0.34f, 0.8f, 0.92f);
            Widgets.TitleOutline(title);

            RectTransform chips = UIFactory.Anchor(UIFactory.Rect("Goals", box.transform), 0.06f, 0.3f, 0.94f, 0.78f);
            BuildGoalChips(chips, s, 150f, Theme.TitleSize, new List<(Text, GameObject)>());

            string budget = s.Config.HasMoveLimit ? Loc.T("hud.goalMoves", s.MovesLeft) : Loc.T("hud.goalTime", Mathf.CeilToInt(s.Config.TimeLimitMs / 1000f));
            Text sub = UIFactory.Label(box.transform, budget, Theme.BodySize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(sub.rectTransform, 0.06f, 0.08f, 0.94f, 0.3f);
            Widgets.TitleOutline(sub);

            Game.Audio.PlaySFX(SoundIds.Click);
            await Task.WhenAny(done.Task, Task.Delay(2600));
            if (shade != null)
            {
                Destroy(shade.gameObject);
            }
            if (this != null && _controller != null)
            {
                _controller.Resume();
            }
        }

        private void TrackResult(StageResult result)
        {
            GameSession s = _controller.Session;
            string name = _launch.Mode == GameMode.Story ? "stage_end" : _launch.Mode == GameMode.GuildBoss ? "boss_end" : "pvp_end";
            string outcome = result.State.ToString();
            if (_launch.Mode != GameMode.Story && _controller.Ghost != null)
            {
                outcome = result.FinalScore > _controller.Ghost.CurrentScore ? "Won" : result.FinalScore == _controller.Ghost.CurrentScore ? "Draw" : "Lost";
            }
            Game.Telemetry.Track(name, ("stage", _launch.StageId), ("result", outcome), ("score", result.FinalScore), ("moves", result.MovesUsed),
                ("ms", result.DurationMs), ("offline", _launch.Offline), ("pet_level", s.Config.PetLevel), ("powerups", result.PowerUpsUsed.Count),
                ("continues", result.ContinuesUsed), ("bot", _launch.OfflineGhost != null));
        }

        /// <summary>Equipped pet under the board: portrait, level and auto-move chance; it hops when the pet plays.</summary>
        private void AddPetBadge(RectTransform safe, SessionConfig config)
        {
            if (config.Pet == PetType.None)
            {
                return;
            }
            Image badge = UIFactory.Panel("Pet", safe, new Color(0.08f, 0.2f, 0.14f, 0.85f));
            RectTransform rect = UIFactory.Anchor(badge.rectTransform, 0.02f, 0.123f, 0.36f, 0.168f);
            Sprite art = PetsScreen.PetArt(config.Pet.ToString());
            if (art != null)
            {
                Image image = UIFactory.Icon(rect, art, Color.white, 0);
                UIFactory.Anchor(image.rectTransform, 0.0f, -0.35f, 0.34f, 1.35f);
                image.gameObject.AddComponent<Breathe>().Amount = 0.04f;
                _petIcon = image.rectTransform;
            }
            Text label = UIFactory.Label(rect, Loc.T("pets.level", config.PetLevel) + " · " + Loc.T("pets.chance", config.PetLevel * Game.Backend.Balance.Pets.AutoMovePermillePerLevel / 10),
                Theme.SmallSize - 6, new Color(0.7f, 1f, 0.8f), TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.36f, 0f, 0.99f, 1f);
        }

        private System.Collections.IEnumerator HopPet()
        {
            RectTransform icon = _petIcon;
            for (float t = 0; t < 0.45f && icon != null; t += Time.deltaTime)
            {
                float k = t / 0.45f;
                icon.anchoredPosition = new Vector2(0, Mathf.Sin(k * Mathf.PI) * 40f);
                icon.localEulerAngles = new Vector3(0, 0, Mathf.Sin(k * Mathf.PI * 2f) * 12f);
                yield return null;
            }
            if (icon != null)
            {
                icon.anchoredPosition = Vector2.zero;
                icon.localEulerAngles = Vector3.zero;
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
            TrackResult(result);
            GameSession session = _controller.Session;

            if (_launch.Mode == GameMode.Story && result.State == SessionState.Lost && !_launch.Offline && Game.Backend.IsOnline)
            {
                if (await OfferContinueAsync(session))
                {
                    return;
                }
            }

            _submitting = true;
            if (_life != null)
            {
                await _life.PlayFinaleAsync(result, _movesMedal, left => _moves.text = Loc.Number(left));
                if (this == null)
                {
                    return;
                }
            }
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
                    // Stage unlocked, stars, achievements, chests, pet XP: the hub and map must see the new progress.
                    _ = Game.Backend.RefreshProfileAsync();
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
                    _ = Game.Backend.RefreshProfileAsync();
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
            if (response != null && response.Accepted)
            {
                _ = Game.Backend.RefreshProfileAsync();
            }
            await UI.Alert(Loc.T("boss.title"), message);
            UI.ShowRoot<GuildScreen>();
        }
    }
}
