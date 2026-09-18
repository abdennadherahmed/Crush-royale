using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Scoring;
using CrushRoyale.Core.Story;
using CrushRoyale.Game.Audio;
using CrushRoyale.Game.UI;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>
    /// Everything that makes a level feel alive around the (deterministic) board. Purely visual: nothing here changes
    /// the simulation, the replay or the timings the server checks.
    /// - The equipped pet sits by the board, jumps onto the gems it plays, cheers combos and worries at low moves.
    /// - The hero or a story companion comments the match in speech bubbles.
    /// - Bosses (story and guild) are shown big, take every hit, roar and "attack" (their stones are real rules).
    /// - In PvP the opponent reacts to its own combos and to yours.
    /// - Kingdom weather and time of day behind the board, an aura that follows the combo meter.
    /// - Announcer texts and voice with random styles, slow motion and zoom on huge chains, a final bonus show.
    /// </summary>
    public sealed class StageLife : MonoBehaviour
    {
        private GameRoot _game;
        private MatchController _controller;
        private MatchLaunch _launch;
        private SessionConfig _config;
        private RectTransform _root;
        private RectTransform _safe;
        private BoardView _board;
        private PetBuddy _pet;
        private Buddy _buddy;
        private BossView _boss;
        private OpponentView _opponent;
        private Image _aura;
        private long _lastScore;
        private long _lastGhostScore;
        private bool _lowMovesSaid;
        private int _goalsDone;
        private float _nextChatter;
        private bool _finished;
        private bool _leading;
        private long _guildBossMax;
        private long _guildBossRemaining;

        /// <summary>Remaining boss HP share for a score: story bosses from the stage HP, guild bosses from the weekly HP.</summary>
        public float BossHpShare(long score)
        {
            if (_config.BossHp > 0)
            {
                return 1f - Mathf.Clamp01(score / (float)_config.BossHp);
            }
            if (_guildBossMax > 0)
            {
                return Mathf.Clamp01((_guildBossRemaining - score) / (float)_guildBossMax);
            }
            return -1f;
        }

        private Localization Loc => _game.Loc;

        public static StageLife Attach(RectTransform root, RectTransform safe, BoardView board, MatchController controller, MatchLaunch launch, SessionConfig config)
        {
            StageLife life = root.gameObject.AddComponent<StageLife>();
            life._game = GameRoot.Instance;
            life._root = root;
            life._safe = safe;
            life._board = board;
            life._controller = controller;
            life._launch = launch;
            life._config = config;
            life.Build();
            return life;
        }

        private void Build()
        {
            Kingdom kingdom = _launch.Mode == GameMode.Story && _launch.StageId > 0
                ? _game.Backend.Catalog.Get(_launch.StageId).Kingdom
                : Kingdom.Central;
            if (!_game.Save.Settings.ReduceMotion)
            {
                Weather.Create(_root, kingdom);
            }

            // Aura behind the board, brighter as the combo meter fills.
            _aura = UIFactory.Icon(_board.Rect.parent, ProceduralSprites.Glow(128), new Color(0.5f, 0.3f, 1f, 0.2f), 0);
            _aura.rectTransform.anchorMin = _board.Rect.anchorMin;
            _aura.rectTransform.anchorMax = _board.Rect.anchorMax;
            _aura.rectTransform.sizeDelta = _board.Rect.sizeDelta * 1.35f;
            _aura.rectTransform.SetSiblingIndex(_board.Rect.GetSiblingIndex());

            if (_board.Fx != null)
            {
                _board.Fx.ComboTexts = false;
            }

            if (_config.Pet != PetType.None)
            {
                _pet = PetBuddy.Create(_safe, _config.Pet.ToString(), _config.PetLevel, _board);
            }

            bool bossFight = _config.BossHp > 0 || _launch.Mode == GameMode.GuildBoss;
            _guildBossMax = _launch.Mode == GameMode.GuildBoss ? _launch.Ticket?.BossMaxHp ?? 0 : 0;
            _guildBossRemaining = _launch.Mode == GameMode.GuildBoss ? _launch.Ticket?.BossRemainingHp ?? 0 : 0;
            if (bossFight)
            {
                StageData stage = _launch.Mode == GameMode.Story && _launch.StageId > 0 ? _game.Backend.Catalog.Get(_launch.StageId) : null;
                bool guild = _launch.Mode == GameMode.GuildBoss;
                Sprite art = stage != null ? ArtLibrary.Boss(stage) : guild ? ArtLibrary.GuildBoss(_launch.StageId) : ArtLibrary.GuildBoss();
                string name = stage != null && !string.IsNullOrEmpty(stage.BossId) ? Loc.T(stage.BossId + ".name")
                    : guild ? CrushRoyale.Game.Screens.GuildScreen.GuildBossName(Loc, _launch.StageId) : Loc.T("boss.guildName");
                _boss = BossView.Create(_safe, art ?? ArtLibrary.GuildBoss(), name, _config.BossHp > 0 ? _config.BossHp : _guildBossRemaining, _board, showDamage: _launch.Mode == GameMode.GuildBoss);
            }

            if (_launch.Mode == GameMode.Story || _launch.Mode == GameMode.GuildBoss)
            {
                _buddy = Buddy.Create(_safe, SpeakerIds(), bossFight);
            }
            else if (_controller.Ghost != null)
            {
                GhostDto ghostInfo = _launch.Ticket?.Ghost;
                string name = ghostInfo?.OpponentName ?? Loc.T("hud.botName");
                _opponent = OpponentView.Create(_safe, name, ghostInfo?.OpponentFrame, ghostInfo?.OpponentTitle, Loc);
            }

            _controller.StepPlayed += OnStep;
            _controller.ActionStarted += OnActionStarted;
            _controller.ActionPresented += OnActionPresented;
            _board.PetMoveStarted += (from, to) => _pet?.JumpOnto(to);
            _nextChatter = Time.unscaledTime + Random.Range(14f, 22f);
        }

        /// <summary>Hero first, then the companions of the story party.</summary>
        private List<string> SpeakerIds()
        {
            ProfileDto profile = _game.Backend.Profile;
            string gender = profile?.Hero?.Gender ?? _game.Save.Settings.HeroGender ?? "female";
            var ids = new List<string> { gender == "male" ? "hero" : "heroine" };
            if (profile?.Story?.Party != null)
            {
                foreach (string id in profile.Story.Party)
                {
                    if (ArtLibrary.Character(id) != null && !ids.Contains(id))
                    {
                        ids.Add(id);
                    }
                }
            }
            if (ids.Count == 1 && ArtLibrary.Character("lyra") != null)
            {
                ids.Add("lyra");
            }
            return ids;
        }

        // ------------------------------------------------------------------ start

        /// <summary>Intro: the boss rises and roars, the announcer calls the fight.</summary>
        public async Task PlayIntroAsync()
        {
            if (_boss != null)
            {
                await CoroutineTask.Run(this, _boss.Entrance());
                _board.Fx?.Shake(0.5f, 24f);
            }
            Voice(SoundIds.VoiceFight);
            if (_board.Fx != null)
            {
                Announce(Loc.T("hud.fight"), Theme.Gold, 1.1f, true);
            }
            if (_boss != null)
            {
                _boss.Say(Pick("boss.taunt1", "boss.taunt2", "boss.taunt3"));
            }
            else
            {
                _buddy?.Say(Pick("hud.say.start1", "hud.say.start2", "hud.say.start3"));
            }
        }

        // ------------------------------------------------------------------ reactions

        private void OnActionStarted(ActionOutcome outcome)
        {
            if (outcome.BossStones.Count > 0 && _boss != null)
            {
                StartCoroutine(_boss.StoneAttack(outcome.BossStones));
            }
        }

        private void OnStep(ResolutionStep step)
        {
            if (step.CombosTriggered > 0)
            {
                // Two bonuses fused: the biggest single moment of a match.
                Announce(Loc.T("hud.fusion"), Theme.Crystal, 1.25f, true);
                _board.Fx?.Shake(0.45f, 30f);
                _game.Haptics.Heavy();
                _game.Audio.PlaySFX(SoundIds.Explosion);
                Voice(SoundIds.VoiceIncredible);
            }
            int level = step.CascadeLevel + 1;
            bool bigShape = false;
            foreach (MatchGroup group in step.Groups)
            {
                bigShape |= group.Shape >= MatchShape.Line5;
            }
            if (level >= 2 || bigShape)
            {
                Hype(Mathf.Max(level, bigShape ? 3 : level));
            }
        }

        private void OnActionPresented(ActionOutcome outcome)
        {
            GameSession s = _controller.Session;
            if (outcome.RedSurgeActivated)
            {
                Voice(SoundIds.VoiceRedSurge);
            }

            long gained = s.Score - _lastScore;
            _lastScore = s.Score;
            if (gained > 0 && _boss != null)
            {
                _boss.TakeHit(gained, BossHpShare(s.Score));
            }

            int cascades = outcome.Resolution?.CascadeCount ?? 0;
            if (cascades >= 3)
            {
                _pet?.Cheer();
                _buddy?.Cheer(_board);
                if (Random.value < 0.6f)
                {
                    _buddy?.Say(Pick("hud.say.combo1", "hud.say.combo2", "hud.say.combo3"));
                }
            }

            int done = 0;
            foreach (ObjectiveProgress p in s.Objectives.Progress)
            {
                done += p.IsComplete ? 1 : 0;
            }
            if (done > _goalsDone)
            {
                _goalsDone = done;
                _game.Audio.PlaySFX(SoundIds.Sparkle);
                _buddy?.Say(Pick("hud.say.goal1", "hud.say.goal2"));
                _pet?.Cheer();
            }

            if (s.Config.HasMoveLimit && s.MovesLeft <= 5 && s.MovesLeft > 0 && !_lowMovesSaid)
            {
                _lowMovesSaid = true;
                _buddy?.Say(Pick("hud.say.lowMoves1", "hud.say.lowMoves2"));
                _buddy?.Worry();
                _pet?.Worry();
            }

            if (_opponent != null && _controller.Ghost != null)
            {
                bool leading = s.Score > _controller.Ghost.CurrentScore;
                if (leading && !_leading)
                {
                    _opponent.Say(Pick("pvp.worried1", "pvp.worried2"), worried: true);
                }
                _leading = leading;
            }
        }

        private void Update()
        {
            if (_controller?.Session == null || _finished)
            {
                return;
            }
            GameSession s = _controller.Session;

            if (_aura != null)
            {
                float meter = s.ComboMeterPermille / 1000f;
                bool surge = s.IsRedSurgeActive(_controller.Clock.NowMs);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * (surge ? 9f : 2.5f));
                Color c = surge ? new Color(1f, 0.15f, 0.3f) : Color.Lerp(new Color(0.45f, 0.3f, 1f), new Color(1f, 0.8f, 0.3f), meter);
                _aura.color = new Color(c.r, c.g, c.b, (0.12f + 0.55f * meter + (surge ? 0.3f : 0f)) * pulse);
            }

            if (_opponent != null && _controller.Ghost != null)
            {
                long ghost = _controller.Ghost.CurrentScore;
                long jump = ghost - _lastGhostScore;
                if (jump >= 1200)
                {
                    _opponent.Celebrate();
                    if (Random.value < 0.45f)
                    {
                        _opponent.Say(Pick("pvp.taunt1", "pvp.taunt2", "pvp.taunt3", "pvp.taunt4"), worried: false);
                    }
                }
                if (jump != 0)
                {
                    _lastGhostScore = ghost;
                }
            }

            if (!_controller.IsPaused && s.IsRunning && Time.unscaledTime >= _nextChatter)
            {
                _nextChatter = Time.unscaledTime + Random.Range(16f, 26f);
                if (_boss != null)
                {
                    StartCoroutine(_boss.Roar(_board));
                    if (Random.value < 0.5f)
                    {
                        _boss.Say(Pick("boss.taunt1", "boss.taunt2", "boss.taunt3"));
                    }
                }
                else if (_buddy != null && Random.value < 0.5f)
                {
                    _buddy.Say(Pick("hud.say.idle1", "hud.say.idle2", "hud.say.idle3"));
                }
            }
        }

        // ------------------------------------------------------------------ announcer

        private static readonly string[][] HypeKeys =
        {
            new[] { "hud.hype.sweet", "hud.hype.great" },
            new[] { "hud.hype.awesome", "hud.hype.amazing" },
            new[] { "hud.hype.incredible", "hud.hype.unstoppable" },
            new[] { "hud.hype.divine", "hud.hype.legendary" }
        };

        private static readonly string[][] HypeVoices =
        {
            new[] { SoundIds.VoiceSweet, SoundIds.VoiceGreat },
            new[] { SoundIds.VoiceAwesome, SoundIds.VoiceAmazing },
            new[] { SoundIds.VoiceIncredible, SoundIds.VoiceUnstoppable },
            new[] { SoundIds.VoiceDivine, SoundIds.VoiceLegendary }
        };

        private static readonly Color[] HypeColors =
        {
            new Color(0.45f, 0.9f, 1f), new Color(1f, 0.82f, 0.3f), new Color(0.8f, 0.5f, 1f), new Color(1f, 0.35f, 0.5f),
            new Color(0.45f, 1f, 0.6f), new Color(1f, 0.6f, 0.2f)
        };

        private float _lastHype;

        private void Hype(int level)
        {
            if (Time.unscaledTime - _lastHype < 0.45f)
            {
                return;
            }
            _lastHype = Time.unscaledTime;
            int tier = Mathf.Clamp(level - 2, 0, 3);
            int variant = Random.Range(0, 2);
            Voice(HypeVoices[tier][variant]);
            Announce(Loc.T(HypeKeys[tier][variant]), HypeColors[Random.Range(0, HypeColors.Length)], 1f + tier * 0.18f, tier >= 2);
            if (tier >= 2 && !_game.Save.Settings.ReduceMotion)
            {
                StartCoroutine(SlowMotion(tier >= 3 ? 0.3f : 0.5f, tier >= 3 ? 0.45f : 0.3f));
                StartCoroutine(Zoom(tier >= 3 ? 1.08f : 1.05f));
                _board.Fx?.Firework(Random.insideUnitCircle * _board.Cell * 2f, HypeColors[Random.Range(0, HypeColors.Length)], 0.7f + tier * 0.1f);
            }
        }

        /// <summary>Big outlined text with a random entrance (pop, slam, spin, slide) and star sparks.</summary>
        private void Announce(string text, Color color, float scale, bool stars)
        {
            BoardFx fx = _board.Fx;
            if (fx == null)
            {
                return;
            }
            Text label = UIFactory.Label(_board.Rect, text, Mathf.RoundToInt(104 * scale), color, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.resizeTextForBestFit = false;
            label.rectTransform.sizeDelta = new Vector2(_board.Rect.sizeDelta.x * 1.2f, 200 * scale);
            Vector2 at = new Vector2(Random.Range(-0.12f, 0.12f), Random.Range(0.05f, 0.25f)) * _board.Rect.sizeDelta.x;
            label.rectTransform.anchoredPosition = at;
            Outline outline = label.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.03f, 0.18f, 1f);
            outline.effectDistance = new Vector2(6, -6);
            Outline glow = label.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(1f, 1f, 1f, 0.5f);
            glow.effectDistance = new Vector2(-3, 3);
            Shadow shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(0, -12);
            StartCoroutine(AnimateAnnounce(label, at, Random.Range(0, 4), Random.Range(-10f, 10f)));
            if (stars)
            {
                for (int i = 0; i < 16; i++)
                {
                    float angle = i / 16f * Mathf.PI * 2f;
                    fx.Spawn(at, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * fx.CellSize * Random.Range(4f, 8f), Random.Range(0.5f, 0.9f),
                        fx.CellSize * 0.5f, 0f, Color.Lerp(color, Color.white, 0.5f), ProceduralSprites.Spark(), drag: 2.5f, spin: 220f, twinkle: true);
                }
            }
        }

        private IEnumerator AnimateAnnounce(Text label, Vector2 at, int style, float tilt)
        {
            RectTransform rect = label.rectTransform;
            const float life = 1.15f;
            for (float t = 0; t < life && label != null; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / 0.25f);
                float scale;
                float angle = tilt;
                Vector2 offset = Vector2.zero;
                switch (style)
                {
                    case 0: // pop
                        scale = Ease.OutBack(k);
                        break;
                    case 1: // slam from big
                        scale = Mathf.Lerp(3f, 1f, Ease.OutCubic(k));
                        break;
                    case 2: // spin in
                        scale = Ease.OutBack(k);
                        angle = tilt + (1f - Ease.OutCubic(k)) * 360f;
                        break;
                    default: // slide from the side
                        scale = 1f;
                        offset = new Vector2((1f - Ease.OutBack(k)) * -900f, 0f);
                        break;
                }
                float wobble = t > 0.25f ? 1f + Mathf.Sin((t - 0.25f) * 14f) * 0.04f * (1f - Mathf.Clamp01((t - 0.25f) * 2f)) : 1f;
                rect.localScale = Vector3.one * scale * wobble;
                rect.localEulerAngles = new Vector3(0, 0, angle);
                rect.anchoredPosition = at + offset + new Vector2(0, t > 0.75f ? (t - 0.75f) * 300f : 0f);
                float fade = t < 0.8f ? 1f : 1f - (t - 0.8f) / (life - 0.8f);
                Color c = label.color;
                label.color = new Color(c.r, c.g, c.b, Mathf.Clamp01(fade));
                yield return null;
            }
            if (label != null)
            {
                Destroy(label.gameObject);
            }
        }

        private IEnumerator SlowMotion(float scale, float realSeconds)
        {
            // The match clock is real time (Stopwatch): slowing animations never changes scores or replays.
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(realSeconds);
            Time.timeScale = 1f;
        }

        private IEnumerator Zoom(float peak)
        {
            RectTransform rect = _board.Rect;
            for (float t = 0; t < 0.6f && rect != null; t += Time.unscaledDeltaTime)
            {
                float k = t / 0.6f;
                float s = k < 0.25f ? Mathf.Lerp(1f, peak, Ease.OutCubic(k / 0.25f)) : Mathf.Lerp(peak, 1f, Mathf.SmoothStep(0f, 1f, (k - 0.25f) / 0.75f));
                rect.localScale = Vector3.one * s;
                yield return null;
            }
            if (rect != null)
            {
                rect.localScale = Vector3.one;
            }
        }

        private void OnDestroy()
        {
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------ end

        /// <summary>
        /// Before the result screen: on a story win the moves left become rockets exploding on the board (the bonus
        /// itself was already computed by the simulation), then confetti; the boss dies; the pet dances or sulks.
        /// </summary>
        public async Task PlayFinaleAsync(StageResult result, RectTransform movesAnchor, Action<int> showMoves)
        {
            _finished = true;
            Time.timeScale = 1f;
            GameSession s = _controller.Session;
            bool won = result.Won || (_launch.Mode != GameMode.Story && _controller.Ghost != null && result.FinalScore > _controller.Ghost.CurrentScore);

            if (_boss != null && (result.Won || _launch.Mode == GameMode.GuildBoss))
            {
                await CoroutineTask.Run(this, _boss.Death(_board));
                Voice(SoundIds.VoiceBossDefeated);
            }
            else if (_boss != null)
            {
                _boss.Laugh();
            }

            if (won)
            {
                _pet?.Dance();
                _buddy?.Cheer(_board);
                _buddy?.Say(Pick("hud.say.win1", "hud.say.win2"));
                _opponent?.Say(Pick("pvp.worried1", "pvp.worried2"), worried: true);
            }
            else
            {
                _pet?.Sulk();
                _buddy?.Worry();
                _buddy?.Say(Pick("hud.say.lose1", "hud.say.lose2"));
                _opponent?.Celebrate();
            }

            if (result.Won && _launch.Mode == GameMode.Story && s.Config.HasMoveLimit && s.MovesLeft > 0)
            {
                await CoroutineTask.Run(this, FinalBonus(s.MovesLeft, result.BonusPoints, movesAnchor, showMoves));
            }
            if (won)
            {
                _board.Fx?.Confetti(120);
                Voice(_launch.Mode == GameMode.Story ? SoundIds.VoiceLevelComplete : SoundIds.VoiceVictory);
                await Task.Delay(1200);
            }
            else
            {
                await Task.Delay(700);
            }
        }

        private IEnumerator FinalBonus(int moves, long bonus, RectTransform movesAnchor, Action<int> showMoves)
        {
            BoardFx fx = _board.Fx;
            if (fx == null)
            {
                yield break;
            }
            Voice(SoundIds.VoiceFinalBonus);
            Announce(Loc.T("hud.finalBonus"), Theme.Gold, 1.2f, true);
            yield return new WaitForSeconds(0.6f);

            int rockets = Mathf.Min(moves, 15);
            long each = rockets > 0 ? bonus / rockets : 0;
            Vector2 start = movesAnchor != null ? (Vector2)_board.Rect.InverseTransformPoint(movesAnchor.position) : new Vector2(0, _board.Rect.sizeDelta.y);
            for (int i = 0; i < rockets; i++)
            {
                showMoves?.Invoke(moves - i - 1);
                var cell = new CrushRoyale.Core.Common.Pos(Random.Range(0, _board.Width), Random.Range(0, _board.Height));
                Vector2 target = _board.CellPosition(cell);
                _game.Audio.PlaySFX(SoundIds.Whoosh, Random.Range(0.9f, 1.2f));
                Color color = HypeColors[i % HypeColors.Length];
                for (float t = 0; t < 0.22f; t += Time.deltaTime)
                {
                    Vector2 p = Vector2.Lerp(start, target, Ease.InQuad(t / 0.22f)) + new Vector2(0, Mathf.Sin(t / 0.22f * Mathf.PI) * fx.CellSize * 2f);
                    fx.Spawn(p, Vector2.zero, 0.3f, fx.CellSize * 0.5f, 0f, color, ProceduralSprites.Glow());
                    yield return null;
                }
                fx.Firework(target, color, 0.6f);
                _game.Audio.PlaySFX(SoundIds.Firework, Random.Range(0.9f, 1.15f));
                if (each > 0)
                {
                    fx.Text("+" + Loc.Number(each), Theme.Gold, target, 56);
                }
                yield return new WaitForSeconds(0.06f);
            }
        }

        private string Pick(params string[] keys) => Loc.T(keys[Random.Range(0, keys.Length)]);

        private static readonly Dictionary<string, bool> VoiceExists = new Dictionary<string, bool>();

        /// <summary>Announcer line in the game language (Resources/Audio/voice/{lang}/), English otherwise.</summary>
        private void Voice(string id)
        {
            string lang = Loc.Language ?? "en";
            if (lang != "en")
            {
                string localized = "voice/" + lang + "/" + id;
                if (!VoiceExists.TryGetValue(localized, out bool exists))
                {
                    exists = Resources.Load<AudioClip>("Audio/" + localized) != null;
                    VoiceExists[localized] = exists;
                }
                if (exists)
                {
                    _game.Audio.PlaySFX(localized);
                    return;
                }
            }
            _game.Audio.PlaySFX(id);
        }
    }

    // ====================================================================== pet

    /// <summary>The equipped pet next to the board: idle bob, jumps onto the gems it plays, emotes.</summary>
    public sealed class PetBuddy : MonoBehaviour
    {
        private RectTransform _rect;
        private RectTransform _body;
        private Image _image;
        private BoardView _board;
        private Vector2 _home;
        private bool _busy;
        private float _mood; // >0 happy bounce, <0 worried shake
        private float _moodTime;

        private static GameRoot Game => GameRoot.Instance;

        public static PetBuddy Create(RectTransform safe, string petType, int level, BoardView board)
        {
            RectTransform rect = UIFactory.Anchor(UIFactory.Rect("PetBuddy", safe), 0.02f, 0.125f, 0.24f, 0.215f);
            PetBuddy pet = rect.gameObject.AddComponent<PetBuddy>();
            pet._rect = rect;
            pet._board = board;
            Image shadow = UIFactory.Icon(rect, ProceduralSprites.Glow(64), new Color(0f, 0f, 0f, 0.45f), 0);
            UIFactory.Anchor(shadow.rectTransform, 0.15f, -0.05f, 0.85f, 0.15f);
            pet._body = UIFactory.Rect("Body", rect);
            UIFactory.Stretch(pet._body);
            pet._body.pivot = new Vector2(0.5f, 0f);
            pet._image = UIFactory.Icon(pet._body, Screens.PetsScreen.PetArt(petType), Color.white, 0);
            UIFactory.Stretch(pet._image.rectTransform);
            pet._image.rectTransform.pivot = new Vector2(0.5f, 0f);

            Text label = UIFactory.Label(rect, Game.Loc.T("pets.level", level), Theme.SmallSize - 6, new Color(0.7f, 1f, 0.8f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, 0.6f, 0.72f, 1.1f, 1f);
            Widgets.TitleOutline(label);
            return pet;
        }

        private void Update()
        {
            if (_busy || _body == null)
            {
                return;
            }
            float t = Time.unscaledTime;
            _moodTime = Mathf.Max(0f, _moodTime - Time.unscaledDeltaTime);
            float squash = Mathf.Sin(t * 3f) * 0.04f;
            Vector2 offset = Vector2.zero;
            float angle = Mathf.Sin(t * 1.4f) * 3f;
            if (_moodTime > 0f && _mood > 0f)
            {
                offset.y = Mathf.Abs(Mathf.Sin(t * 12f)) * 40f;
                angle = Mathf.Sin(t * 12f) * 10f;
            }
            else if (_moodTime > 0f && _mood < 0f)
            {
                offset.x = Mathf.Sin(t * 45f) * 6f;
            }
            _body.anchoredPosition = offset;
            _body.localScale = new Vector3(1f - squash, 1f + squash, 1f);
            _body.localEulerAngles = new Vector3(0, 0, angle);
        }

        public void Cheer()
        {
            _mood = 1f;
            _moodTime = 1.2f;
            Emote(UiKit.Art("item_heart"), null);
        }

        public void Worry()
        {
            _mood = -1f;
            _moodTime = 2f;
            Emote(null, "!");
        }

        public void Dance()
        {
            _mood = 1f;
            _moodTime = 4f;
            Emote(UiKit.Art("item_stars"), null);
        }

        public void Sulk()
        {
            if (_image != null)
            {
                _image.color = new Color(0.65f, 0.65f, 0.75f, 1f);
            }
            _mood = -1f;
            _moodTime = 1.5f;
        }

        /// <summary>Leaps onto a board cell (board-local), stomps with sparkles, then hops back home.</summary>
        public void JumpOnto(Vector2 boardLocal)
        {
            if (!_busy && isActiveAndEnabled)
            {
                StartCoroutine(Jump(boardLocal));
            }
        }

        private IEnumerator Jump(Vector2 boardLocal)
        {
            _busy = true;
            Transform parent = _rect.parent;
            Vector3 world = _board.Rect.TransformPoint(boardLocal);
            Vector2 target = (Vector2)_rect.InverseTransformPoint(world) - new Vector2(0, _rect.rect.height * 0.25f);
            _home = Vector2.zero;
            Game.Audio.PlaySFX(SoundIds.PetHop);
            _rect.SetAsLastSibling();

            yield return CoroutineTask.Tween(0.1f, k => _body.localScale = new Vector3(1f + 0.2f * k, 1f - 0.25f * k, 1f));
            yield return CoroutineTask.Tween(0.32f, k =>
            {
                Vector2 p = Vector2.Lerp(_home, target, k) + new Vector2(0, Mathf.Sin(k * Mathf.PI) * 260f);
                _body.anchoredPosition = p;
                _body.localScale = Vector3.one * (1f + Mathf.Sin(k * Mathf.PI) * 0.25f);
                _body.localEulerAngles = new Vector3(0, 0, (target.x > 0 ? -1f : 1f) * k * 360f);
            });
            _body.localEulerAngles = Vector3.zero;
            Game.Audio.PlaySFX(SoundIds.PetLand);
            BoardFx fx = _board.Fx;
            if (fx != null)
            {
                fx.BonusCreated(boardLocal);
                fx.Shake(0.15f, 10f);
            }
            yield return CoroutineTask.Tween(0.12f, k => _body.localScale = new Vector3(1f + 0.3f * (1f - k), 1f - 0.3f * (1f - k), 1f));
            yield return new WaitForSeconds(0.12f);
            yield return CoroutineTask.Tween(0.3f, k =>
            {
                _body.anchoredPosition = Vector2.Lerp(target, _home, k) + new Vector2(0, Mathf.Sin(k * Mathf.PI) * 200f);
            });
            _body.anchoredPosition = _home;
            _body.localScale = Vector3.one;
            _busy = false;
            Cheer();
        }

        private void Emote(Sprite icon, string text)
        {
            RectTransform bubble = UIFactory.Anchor(UIFactory.Rect("Emote", _rect), 0.55f, 0.85f, 0.95f, 1.35f);
            if (icon != null)
            {
                Image image = UIFactory.Icon(bubble, icon, Color.white, 0);
                UIFactory.Stretch(image.rectTransform);
            }
            else
            {
                Text label = UIFactory.Label(bubble, text, Theme.TitleSize, Theme.Warning, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Stretch(label.rectTransform);
                Widgets.TitleOutline(label);
            }
            bubble.gameObject.AddComponent<PopIn>();
            StartCoroutine(FloatAway(bubble));
        }

        private static IEnumerator FloatAway(RectTransform rect)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            for (float t = 0; t < 0.9f && rect != null; t += Time.unscaledDeltaTime)
            {
                rect.anchoredPosition = new Vector2(Mathf.Sin(t * 6f) * 8f, t * 90f);
                foreach (Graphic g in rect.GetComponentsInChildren<Graphic>())
                {
                    Color c = g.color;
                    g.color = new Color(c.r, c.g, c.b, 1f - t / 0.9f);
                }
                yield return null;
            }
            if (rect != null)
            {
                Destroy(rect.gameObject);
            }
        }
    }

    // ====================================================================== hero and companions

    /// <summary>Portrait of the hero or a companion by the board, with speech bubbles and reactions.</summary>
    public sealed class Buddy : MonoBehaviour
    {
        private List<string> _speakers;
        private RectTransform _rect;
        private RectTransform _frame;
        private Image _portrait;
        private RectTransform _bubble;
        private Text _bubbleName;
        private Text _bubbleText;
        private float _bubbleUntil;
        private float _worry;
        private float _cheer;

        private static GameRoot Game => GameRoot.Instance;

        public static Buddy Create(RectTransform safe, List<string> speakers, bool bossFight)
        {
            RectTransform rect = UIFactory.Anchor(UIFactory.Rect("Buddy", safe), 0.78f, 0.125f, 0.98f, 0.215f);
            Buddy buddy = rect.gameObject.AddComponent<Buddy>();
            buddy._rect = rect;
            buddy._speakers = speakers;

            buddy._frame = UIFactory.Rect("Frame", rect);
            buddy._frame.anchorMin = buddy._frame.anchorMax = new Vector2(0.5f, 0.5f);
            buddy._frame.sizeDelta = new Vector2(170, 170);
            UiKit.RoundBadge(buddy._frame, crystal: false);
            RectTransform mask = UIFactory.Stretch(UIFactory.Rect("Mask", buddy._frame), 14, 14, 14, 14);
            Image maskImage = mask.gameObject.AddComponent<Image>();
            maskImage.sprite = ProceduralSprites.Circle();
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            maskImage.color = new Color(0.1f, 0.06f, 0.2f, 1f);
            buddy._portrait = UIFactory.Icon(mask, ArtLibrary.Character(speakers[0]), Color.white, 0);
            UIFactory.Stretch(buddy._portrait.rectTransform, -10, -10, -6, -30);

            // Bubble to the left, above the power-ups.
            Image bubble = UIFactory.Panel("Bubble", safe, Theme.Panel);
            buddy._bubble = UIFactory.Anchor(bubble.rectTransform, 0.3f, 0.125f, 0.77f, 0.215f);
            UiKit.CardFrame(bubble);
            buddy._bubbleName = UIFactory.Label(bubble.transform, string.Empty, Theme.SmallSize - 6, Theme.Gold, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Anchor(buddy._bubbleName.rectTransform, 0.06f, 0.6f, 0.96f, 0.92f);
            buddy._bubbleText = UIFactory.Label(bubble.transform, string.Empty, Theme.SmallSize - 2, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Anchor(buddy._bubbleText.rectTransform, 0.06f, 0.08f, 0.96f, 0.64f);
            buddy._bubble.gameObject.SetActive(false);
            return buddy;
        }

        public void Say(string line)
        {
            if (_bubble == null || string.IsNullOrEmpty(line))
            {
                return;
            }
            string speaker = _speakers[Random.Range(0, _speakers.Count)];
            Sprite art = ArtLibrary.Character(speaker);
            if (art != null)
            {
                _portrait.sprite = art;
            }
            _bubbleName.text = speaker == "hero" || speaker == "heroine"
                ? Game.Backend.Profile?.DisplayName ?? Game.Save.Settings.HeroPseudo ?? string.Empty
                : Game.Loc.T("char." + speaker);
            _bubbleText.text = line;
            _bubble.gameObject.SetActive(true);
            _bubble.GetComponent<PopIn>()?.Replay();
            if (_bubble.GetComponent<PopIn>() == null)
            {
                _bubble.gameObject.AddComponent<PopIn>();
            }
            _bubbleUntil = Time.unscaledTime + 2.8f;
            Game.Audio.PlaySFX(SoundIds.Chat);
        }

        public void Cheer(BoardView board)
        {
            _cheer = 0.9f;
            BoardFx fx = board.Fx;
            if (fx == null)
            {
                return;
            }
            // A spell flies from the portrait to the board.
            Vector2 from = board.Rect.InverseTransformPoint(_frame.position);
            for (int i = 0; i < 14; i++)
            {
                Vector2 dir = (Vector2.zero - from).normalized;
                fx.Spawn(from, (dir * fx.CellSize * Random.Range(9f, 14f)) + Random.insideUnitCircle * fx.CellSize * 2f, Random.Range(0.4f, 0.7f),
                    fx.CellSize * 0.45f, 0f, Color.Lerp(Theme.Crystal, Color.white, Random.value), ProceduralSprites.Spark(), drag: 1.5f, spin: 300f, twinkle: true);
            }
            Game.Audio.PlaySFX(SoundIds.Sparkle);
        }

        public void Worry() => _worry = 1.5f;

        private void Update()
        {
            if (_frame == null)
            {
                return;
            }
            float t = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            _cheer = Mathf.Max(0f, _cheer - dt);
            _worry = Mathf.Max(0f, _worry - dt);
            float bob = Mathf.Sin(t * 2f) * 4f;
            if (_cheer > 0f)
            {
                _frame.anchoredPosition = new Vector2(0, Mathf.Abs(Mathf.Sin(t * 14f)) * 30f);
                _frame.localScale = Vector3.one * (1f + _cheer * 0.12f);
            }
            else if (_worry > 0f)
            {
                _frame.anchoredPosition = new Vector2(Mathf.Sin(t * 50f) * 5f, bob);
                _frame.localScale = Vector3.one;
            }
            else
            {
                _frame.anchoredPosition = new Vector2(0, bob);
                _frame.localScale = Vector3.one;
            }
            if (_bubble != null && _bubble.gameObject.activeSelf && t > _bubbleUntil)
            {
                _bubble.gameObject.SetActive(false);
            }
        }
    }

    // ====================================================================== boss

    /// <summary>Big animated boss above the board: takes hits with damage numbers, roars, throws its stones, dies.</summary>
    public sealed class BossView : MonoBehaviour
    {
        private RectTransform _rect;
        private RectTransform _body;
        private Image _art;
        private Text _name;
        private RectTransform _bubble;
        private Text _bubbleText;
        private float _bubbleUntil;
        private float _hit;
        private float _roar;
        private float _hpShare = 1f;
        private long _hp;
        private long _damage;
        private Text _damageTotal;
        private bool _dead;
        private BoardView _board;

        private static GameRoot Game => GameRoot.Instance;

        public static BossView Create(RectTransform safe, Sprite art, string name, long hp, BoardView board, bool showDamage = false)
        {
            RectTransform rect = UIFactory.Anchor(UIFactory.Rect("Boss", safe), 0.6f, 0.68f, 1.02f, 0.87f);
            BossView boss = rect.gameObject.AddComponent<BossView>();
            boss._rect = rect;
            boss._board = board;
            boss._hp = hp;
            Image glow = UIFactory.Icon(rect, ProceduralSprites.Glow(128), new Color(1f, 0.2f, 0.3f, 0.35f), 0);
            UIFactory.Stretch(glow.rectTransform, -40, -40, -40, -40);
            glow.gameObject.AddComponent<Pulse>();
            boss._body = UIFactory.Stretch(UIFactory.Rect("Body", rect));
            boss._body.pivot = new Vector2(0.5f, 0.1f);
            boss._art = UIFactory.Icon(boss._body, art, Color.white, 0);
            UIFactory.Stretch(boss._art.rectTransform);

            boss._name = UIFactory.Label(rect, name, Theme.SmallSize, Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(boss._name.rectTransform, -0.1f, -0.1f, 1.0f, 0.08f);
            Widgets.TitleOutline(boss._name);
            if (hp <= 0 || showDamage)
            {
                boss._damageTotal = UIFactory.Label(rect, string.Empty, Theme.SmallSize, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
                UIFactory.Anchor(boss._damageTotal.rectTransform, -0.1f, 0.84f, 1.0f, 1.02f);
                Widgets.TitleOutline(boss._damageTotal);
            }

            Image bubble = UIFactory.Panel("Taunt", safe, Theme.Panel);
            boss._bubble = UIFactory.Anchor(bubble.rectTransform, 0.3f, 0.7f, 0.66f, 0.77f);
            UiKit.CardFrame(bubble);
            boss._bubbleText = UIFactory.Label(bubble.transform, string.Empty, Theme.SmallSize - 2, Theme.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(boss._bubbleText.rectTransform, 18, 18, 8, 8);
            boss._bubble.gameObject.SetActive(false);
            rect.localScale = Vector3.zero;
            return boss;
        }

        public IEnumerator Entrance()
        {
            Game.Audio.PlaySFX(SoundIds.BossRoar);
            yield return CoroutineTask.Tween(0.6f, k => _rect.localScale = Vector3.one * Ease.OutBack(k));
            _roar = 1f;
        }

        public void Say(string line)
        {
            _bubbleText.text = line;
            _bubble.gameObject.SetActive(true);
            if (_bubble.GetComponent<PopIn>() == null)
            {
                _bubble.gameObject.AddComponent<PopIn>();
            }
            else
            {
                _bubble.GetComponent<PopIn>().Replay();
            }
            _bubbleUntil = Time.unscaledTime + 2.5f;
        }

        /// <summary>Recoil, red flash, damage number; <paramref name="hpShare"/> &lt; 0 for guild bosses (no HP bar).</summary>
        public void TakeHit(long damage, float hpShare)
        {
            if (_dead)
            {
                return;
            }
            _hit = 0.35f;
            _damage += damage;
            if (hpShare >= 0f)
            {
                _hpShare = hpShare;
            }
            if (_damageTotal != null)
            {
                _damageTotal.text = Game.Loc.T("boss.damageTotal", Game.Loc.Number(_damage));
            }
            Game.Audio.PlaySFX(SoundIds.BossHit, Random.Range(0.9f, 1.1f));

            Text number = UIFactory.Label(_rect, "-" + Game.Loc.Number(damage), Theme.HeaderSize + Mathf.Min(30, (int)(damage / 400)), new Color(1f, 0.35f, 0.3f), TextAnchor.MiddleCenter, FontStyle.Bold);
            number.raycastTarget = false;
            number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(Random.Range(0.3f, 0.7f), Random.Range(0.5f, 0.8f));
            number.rectTransform.sizeDelta = new Vector2(400, 90);
            number.horizontalOverflow = HorizontalWrapMode.Overflow;
            Widgets.TitleOutline(number);
            StartCoroutine(FloatNumber(number));
        }

        private static IEnumerator FloatNumber(Text number)
        {
            RectTransform rect = number.rectTransform;
            Vector2 start = rect.anchoredPosition;
            for (float t = 0; t < 0.9f && number != null; t += Time.unscaledDeltaTime)
            {
                rect.anchoredPosition = start + new Vector2(Mathf.Sin(t * 8f) * 10f, t * 160f);
                rect.localScale = Vector3.one * (t < 0.15f ? Ease.OutBack(t / 0.15f) * 1.2f : 1.2f - (t - 0.15f) * 0.3f);
                Color c = number.color;
                number.color = new Color(c.r, c.g, c.b, 1f - Mathf.Clamp01((t - 0.5f) / 0.4f));
                yield return null;
            }
            if (number != null)
            {
                Destroy(number.gameObject);
            }
        }

        /// <summary>Visual attack: roar, the screen shakes, a dark wave passes over the board (no rule change).</summary>
        public IEnumerator Roar(BoardView board)
        {
            if (_dead)
            {
                yield break;
            }
            _roar = 1.2f;
            Game.Audio.PlaySFX(SoundIds.BossRoar);
            Game.Haptics.Medium();
            board.Fx?.Shake(0.6f, 26f);
            BoardFx fx = board.Fx;
            if (fx != null)
            {
                Vector2 from = board.Rect.InverseTransformPoint(_body.position);
                fx.Spawn(from, Vector2.zero, 0.8f, fx.CellSize * 1f, fx.CellSize * 14f, new Color(0.6f, 0f, 0.1f, 0.35f), ProceduralSprites.Ring(128));
                fx.Spawn(Vector2.zero, Vector2.zero, 0.7f, fx.CellSize * 12f, fx.CellSize * 12f, new Color(0.35f, 0f, 0.05f, 0.25f), ProceduralSprites.Glow());
            }
            yield return null;
        }

        /// <summary>The real boss phase: stones fly from the boss onto the cells the simulation picked.</summary>
        public IEnumerator StoneAttack(List<CrushRoyale.Core.Common.Pos> stones)
        {
            _roar = 1.2f;
            Game.Audio.PlaySFX(SoundIds.BossRoar);
            BoardView board = _board;
            if (board == null || board.Fx == null)
            {
                yield break;
            }
            Vector2 from = board.Rect.InverseTransformPoint(_body.position);
            Sprite stone = ArtLibrary.Stone() ?? ProceduralSprites.Stone();
            foreach (CrushRoyale.Core.Common.Pos cell in stones)
            {
                Vector2 to = board.CellPosition(cell);
                Image rock = UIFactory.Icon(board.Rect, stone, Color.white, board.Cell * 0.9f);
                StartCoroutine(Throw(rock, from, to, board));
                yield return new WaitForSeconds(0.06f);
            }
        }

        private static IEnumerator Throw(Image rock, Vector2 from, Vector2 to, BoardView board)
        {
            RectTransform rect = rock.rectTransform;
            for (float t = 0; t < 0.3f && rock != null; t += Time.deltaTime)
            {
                float k = t / 0.3f;
                rect.anchoredPosition = Vector2.Lerp(from, to, k) + new Vector2(0, Mathf.Sin(k * Mathf.PI) * board.Cell * 2f);
                rect.localEulerAngles = new Vector3(0, 0, k * 540f);
                rect.localScale = Vector3.one * (1.4f - 0.4f * k);
                yield return null;
            }
            if (rock != null)
            {
                board.Fx?.Spawn(to, Vector2.zero, 0.3f, board.Cell * 0.5f, board.Cell * 2f, new Color(0.7f, 0.6f, 0.5f, 0.7f), ProceduralSprites.Glow());
                Destroy(rock.gameObject);
            }
        }

        public void Laugh() => _roar = 2f;

        public IEnumerator Death(BoardView board)
        {
            _dead = true;
            Game.Audio.PlaySFX(SoundIds.BossDeath);
            Game.Haptics.Heavy();
            BoardFx fx = board.Fx;
            Vector2 at = board.Rect.InverseTransformPoint(_body.position);
            for (float t = 0; t < 1.2f; t += Time.unscaledDeltaTime)
            {
                _body.anchoredPosition = new Vector2(Mathf.Sin(t * 60f) * 14f, Mathf.Cos(t * 47f) * 8f);
                _art.color = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.3f), Mathf.PingPong(t * 8f, 1f));
                if (fx != null && Random.value < 0.2f)
                {
                    fx.Firework(at + Random.insideUnitCircle * fx.CellSize * 2f, Random.value < 0.5f ? Theme.Gold : Theme.Danger, 0.5f);
                }
                yield return null;
            }
            fx?.Firework(at, Color.white, 1.4f);
            yield return CoroutineTask.Tween(0.4f, k =>
            {
                _rect.localScale = Vector3.one * (1f + 0.3f * k);
                _art.color = new Color(1f, 1f, 1f, 1f - k);
            });
            _rect.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_body == null || _dead)
            {
                return;
            }
            float t = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            _hit = Mathf.Max(0f, _hit - dt);
            _roar = Mathf.Max(0f, _roar - dt);
            bool enraged = _hp > 0 && _hpShare < 0.3f;
            float breathe = Mathf.Sin(t * (enraged ? 4f : 1.8f)) * 0.03f;
            float roarScale = _roar > 0f ? Mathf.Sin(Mathf.Clamp01(_roar) * Mathf.PI) * 0.15f : 0f;
            _body.localScale = new Vector3(1f - breathe * 0.5f + roarScale, 1f + breathe + roarScale, 1f);
            _body.anchoredPosition = _hit > 0f ? new Vector2(_hit * 60f + Mathf.Sin(t * 70f) * 6f, 0f) : _roar > 0f ? new Vector2(Mathf.Sin(t * 50f) * 5f, 0f) : Vector2.zero;
            Color tint = Color.white;
            if (_hit > 0f)
            {
                tint = Color.Lerp(Color.white, new Color(1f, 0.25f, 0.25f), _hit / 0.35f);
            }
            else if (enraged)
            {
                tint = Color.Lerp(Color.white, new Color(1f, 0.55f, 0.55f), 0.5f + 0.5f * Mathf.Sin(t * 6f));
            }
            _art.color = tint;
            if (_bubble != null && _bubble.gameObject.activeSelf && t > _bubbleUntil)
            {
                _bubble.gameObject.SetActive(false);
            }
        }
    }

    // ====================================================================== PvP opponent

    /// <summary>Opponent portrait by the ghost board: celebrates its combos, taunts, sweats when you lead.</summary>
    public sealed class OpponentView : MonoBehaviour
    {
        private static readonly string[] Portraits = { "kael", "mira", "thorin", "zara", "elian", "mark", "soren" };

        private RectTransform _frame;
        private RectTransform _bubble;
        private Text _bubbleText;
        private float _bubbleUntil;
        private float _celebrate;
        private float _worry;

        private static GameRoot Game => GameRoot.Instance;

        public static OpponentView Create(RectTransform safe, string name, string frameId, string titleId, Localization loc)
        {
            RectTransform rect = UIFactory.Anchor(UIFactory.Rect("Opponent", safe), 0.56f, 0.77f, 0.72f, 0.86f);
            OpponentView view = rect.gameObject.AddComponent<OpponentView>();
            string id = Portraits[Mathf.Abs((name ?? string.Empty).GetHashCode()) % Portraits.Length];
            view._frame = CosmeticLook.Avatar(rect, ArtLibrary.Character(id), frameId, 110);
            view._frame.anchorMin = view._frame.anchorMax = new Vector2(0.5f, 0.58f);
            Text label = UIFactory.Label(rect, name, Theme.SmallSize - 8, CosmeticLook.Frame(frameId).Main, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Anchor(label.rectTransform, -0.3f, -0.12f, 1.3f, 0.14f);
            Widgets.TitleOutline(label);
            if (!string.IsNullOrEmpty(titleId))
            {
                RectTransform plate = CosmeticLook.TitlePlate(rect, loc, titleId, Theme.SmallSize - 12);
                UIFactory.Anchor(plate, -0.35f, -0.36f, 1.35f, -0.1f);
            }

            Image bubble = UIFactory.Panel("Taunt", safe, Theme.Panel);
            view._bubble = UIFactory.Anchor(bubble.rectTransform, 0.03f, 0.78f, 0.55f, 0.85f);
            UiKit.CardFrame(bubble);
            view._bubbleText = UIFactory.Label(bubble.transform, string.Empty, Theme.SmallSize - 4, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(view._bubbleText.rectTransform, 16, 16, 6, 6);
            view._bubble.gameObject.SetActive(false);
            return view;
        }

        public void Say(string line, bool worried)
        {
            if (Time.unscaledTime < _bubbleUntil)
            {
                return;
            }
            _bubbleText.text = line;
            _bubbleText.color = worried ? Theme.Warning : Theme.Crystal;
            _bubble.gameObject.SetActive(true);
            _bubbleUntil = Time.unscaledTime + 2.2f;
            if (worried)
            {
                _worry = 1.5f;
            }
            Game.Audio.PlaySFX(SoundIds.Chat);
        }

        public void Celebrate() => _celebrate = 1f;

        private void Update()
        {
            if (_frame == null)
            {
                return;
            }
            float t = Time.unscaledTime;
            float dt = Time.unscaledDeltaTime;
            _celebrate = Mathf.Max(0f, _celebrate - dt);
            _worry = Mathf.Max(0f, _worry - dt);
            if (_celebrate > 0f)
            {
                _frame.anchoredPosition = new Vector2(0, Mathf.Abs(Mathf.Sin(t * 14f)) * 20f);
                _frame.localEulerAngles = new Vector3(0, 0, Mathf.Sin(t * 14f) * 12f);
            }
            else if (_worry > 0f)
            {
                _frame.anchoredPosition = new Vector2(Mathf.Sin(t * 50f) * 4f, 0f);
                _frame.localEulerAngles = Vector3.zero;
            }
            else
            {
                _frame.anchoredPosition = new Vector2(0, Mathf.Sin(t * 2f) * 3f);
                _frame.localEulerAngles = Vector3.zero;
            }
            if (_bubble.gameObject.activeSelf && t > _bubbleUntil)
            {
                _bubble.gameObject.SetActive(false);
            }
        }
    }

    // ====================================================================== weather and time of day

    /// <summary>
    /// Kingdom ambience between the backdrop and the HUD: snow, sand, leaves and fireflies, embers, crystal shards; a
    /// tint following the real time of day (dawn, day, dusk, night with stars), lightning flashes and aurora at night.
    /// </summary>
    public sealed class Weather : MonoBehaviour
    {
        private struct Mote
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Velocity;
            public float Phase;
            public float Spin;
            public float Twinkle;
            public Color Color;
        }

        private readonly List<Mote> _motes = new List<Mote>();
        private RectTransform _rect;
        private Kingdom _kingdom;
        private Image _tint;
        private Image _flash;
        private Image[] _aurora;
        private bool _night;
        private float _nextLightning;

        public static Weather Create(RectTransform root, Kingdom kingdom)
        {
            RectTransform rect = UIFactory.Stretch(UIFactory.Rect("Weather", root));
            rect.SetSiblingIndex(Mathf.Min(1, root.childCount - 1));
            Weather weather = rect.gameObject.AddComponent<Weather>();
            weather._rect = rect;
            weather._kingdom = kingdom;
            weather.Build();
            return weather;
        }

        private void Build()
        {
            int hour = DateTime.Now.Hour;
            _night = hour >= 20 || hour < 6;
            Color tint = hour >= 6 && hour < 9 ? new Color(1f, 0.55f, 0.25f, 0.12f)
                : hour >= 17 && hour < 20 ? new Color(0.75f, 0.3f, 0.55f, 0.18f)
                : _night ? new Color(0.05f, 0.08f, 0.3f, 0.38f)
                : new Color(1f, 1f, 1f, 0f);
            _tint = UIFactory.Panel("Tint", _rect, tint, rounded: false);
            UIFactory.Stretch(_tint.rectTransform);
            _tint.raycastTarget = false;

            if (_night)
            {
                for (int i = 0; i < 40; i++)
                {
                    AddMote(ProceduralSprites.Spark(), Color.white, Random.Range(6f, 16f), new Vector2(Random.value, Random.Range(0.6f, 1f)), Vector2.zero, 0f, 3f);
                }
                if (_kingdom == Kingdom.North || Random.value < 0.25f)
                {
                    _aurora = new Image[2];
                    for (int i = 0; i < 2; i++)
                    {
                        _aurora[i] = UIFactory.Icon(_rect, ProceduralSprites.Glow(128), new Color(0.3f, 1f, 0.6f, 0.18f), 0);
                        UIFactory.Anchor(_aurora[i].rectTransform, -0.3f, 0.72f + i * 0.08f, 1.3f, 0.95f + i * 0.04f);
                        _aurora[i].raycastTarget = false;
                    }
                }
                _nextLightning = Time.unscaledTime + Random.Range(12f, 30f);
            }

            int count = 46;
            for (int i = 0; i < count; i++)
            {
                SpawnKingdomMote(randomHeight: true);
            }

            _flash = UIFactory.Panel("Flash", _rect, new Color(0.85f, 0.9f, 1f, 0f), rounded: false);
            UIFactory.Stretch(_flash.rectTransform);
            _flash.raycastTarget = false;
        }

        private void SpawnKingdomMote(bool randomHeight)
        {
            Vector2 start = new Vector2(Random.value, randomHeight ? Random.value : 1.05f);
            switch (_kingdom)
            {
                case Kingdom.North:
                    AddMote(ProceduralSprites.Circle(), new Color(1f, 1f, 1f, Random.Range(0.5f, 0.9f)), Random.Range(8f, 22f), start,
                        new Vector2(Random.Range(-0.01f, 0.01f), -Random.Range(0.03f, 0.07f)), 0f, 0f);
                    break;
                case Kingdom.East:
                    AddMote(ProceduralSprites.Circle(), new Color(0.95f, 0.8f, 0.5f, Random.Range(0.35f, 0.7f)), Random.Range(4f, 10f), new Vector2(randomHeight ? Random.value : -0.05f, Random.value),
                        new Vector2(Random.Range(0.08f, 0.16f), Random.Range(-0.01f, 0.01f)), 0f, 0f);
                    break;
                case Kingdom.West:
                    if (Random.value < 0.5f)
                    {
                        Color leaf = Color.Lerp(new Color(0.4f, 0.8f, 0.3f), new Color(0.95f, 0.75f, 0.2f), Random.value);
                        AddMote(ProceduralSprites.Gem(ProceduralSprites.GemShape.Diamond), new Color(leaf.r, leaf.g, leaf.b, 0.8f), Random.Range(14f, 26f), start,
                            new Vector2(Random.Range(-0.02f, 0.02f), -Random.Range(0.025f, 0.05f)), Random.Range(-120f, 120f), 0f);
                    }
                    else
                    {
                        AddMote(ProceduralSprites.Glow(64), new Color(1f, 0.95f, 0.4f, 0.9f), Random.Range(14f, 24f), new Vector2(Random.value, Random.Range(0.1f, 0.7f)),
                            new Vector2(Random.Range(-0.01f, 0.01f), Random.Range(-0.01f, 0.01f)), 0f, 5f);
                    }
                    break;
                case Kingdom.South:
                    AddMote(ProceduralSprites.Glow(64), new Color(1f, Random.Range(0.35f, 0.65f), 0.1f, 0.9f), Random.Range(8f, 20f), new Vector2(Random.value, randomHeight ? Random.value : -0.05f),
                        new Vector2(Random.Range(-0.015f, 0.015f), Random.Range(0.04f, 0.09f)), 0f, 8f);
                    break;
                default:
                    Color crystal = Random.value < 0.5f ? new Color(0.45f, 0.9f, 1f, 0.8f) : new Color(0.8f, 0.5f, 1f, 0.8f);
                    AddMote(ProceduralSprites.Gem(ProceduralSprites.GemShape.Diamond), crystal, Random.Range(10f, 20f), new Vector2(Random.value, randomHeight ? Random.value : -0.05f),
                        new Vector2(Random.Range(-0.01f, 0.01f), Random.Range(0.015f, 0.035f)), Random.Range(-60f, 60f), 4f);
                    break;
            }
        }

        private void AddMote(Sprite sprite, Color color, float size, Vector2 normalized, Vector2 velocity, float spin, float twinkle)
        {
            Image image = UIFactory.Icon(_rect, sprite, color, size);
            image.raycastTarget = false;
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = normalized;
            _motes.Add(new Mote { Rect = image.rectTransform, Image = image, Velocity = velocity, Phase = Random.value * 10f, Spin = spin, Twinkle = twinkle, Color = color });
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float t = Time.unscaledTime;
            for (int i = 0; i < _motes.Count; i++)
            {
                Mote m = _motes[i];
                if (m.Rect == null)
                {
                    continue;
                }
                Vector2 anchor = m.Rect.anchorMin;
                if (m.Velocity != Vector2.zero)
                {
                    anchor += (m.Velocity + new Vector2(Mathf.Sin(t * 1.3f + m.Phase) * 0.01f, 0f)) * dt;
                    if (anchor.y < -0.06f || anchor.y > 1.06f || anchor.x < -0.06f || anchor.x > 1.06f)
                    {
                        // Wrap around to the side the motes come from.
                        if (m.Velocity.y < -0.001f) anchor = new Vector2(Random.value, 1.05f);
                        else if (m.Velocity.y > 0.001f && Mathf.Abs(m.Velocity.x) < 0.05f) anchor = new Vector2(Random.value, -0.05f);
                        else if (m.Velocity.x > 0.05f) anchor = new Vector2(-0.05f, Random.value);
                        else anchor = new Vector2(Random.value, Random.value);
                    }
                    m.Rect.anchorMin = m.Rect.anchorMax = anchor;
                }
                if (m.Spin != 0f)
                {
                    m.Rect.localEulerAngles = new Vector3(0, 0, t * m.Spin + m.Phase * 30f);
                }
                if (m.Twinkle > 0f)
                {
                    float a = m.Color.a * (0.35f + 0.65f * Mathf.Abs(Mathf.Sin(t * m.Twinkle * 0.3f + m.Phase)));
                    m.Image.color = new Color(m.Color.r, m.Color.g, m.Color.b, a);
                }
            }

            if (_aurora != null)
            {
                for (int i = 0; i < _aurora.Length; i++)
                {
                    float wave = Mathf.Sin(t * 0.3f + i * 2f);
                    _aurora[i].color = Color.Lerp(new Color(0.3f, 1f, 0.6f, 0.12f), new Color(0.6f, 0.4f, 1f, 0.22f), 0.5f + 0.5f * wave);
                    _aurora[i].rectTransform.anchoredPosition = new Vector2(wave * 80f, 0f);
                }
            }

            if (_night && t >= _nextLightning)
            {
                _nextLightning = t + Random.Range(15f, 40f);
                StartCoroutine(Lightning());
            }
        }

        private IEnumerator Lightning()
        {
            float[] flashes = { 0.5f, 0f, 0.35f, 0f };
            foreach (float alpha in flashes)
            {
                _flash.color = new Color(0.85f, 0.9f, 1f, alpha);
                yield return new WaitForSecondsRealtime(0.06f);
            }
            for (float a = 0.2f; a > 0f; a -= Time.unscaledDeltaTime * 0.6f)
            {
                _flash.color = new Color(0.85f, 0.9f, 1f, a);
                yield return null;
            }
            _flash.color = new Color(0.85f, 0.9f, 1f, 0f);
        }
    }
}
