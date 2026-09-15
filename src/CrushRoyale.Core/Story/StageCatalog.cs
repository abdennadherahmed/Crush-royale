using System;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Story
{
    /// <summary>
    /// Deterministic generator of the 1000 campaign stages (+ endless post-game stages).
    /// Structure: 5 acts x 10 chapters x 20 stages; mini-boss at stage 10 and boss at stage 20 of every chapter.
    /// Difficulty follows the GDD (stage / 1000 * 100%) with a small relief right after each chapter boss.
    /// Stage objectives rotate (score, collect, ice, stones) so 1000 stages don't feel identical.
    /// </summary>
    public sealed class StageCatalog
    {
        /// <summary>Endless stages reuse the layout rhythm of the last act with new seeds.</summary>
        public const int EndlessCycle = 200;

        private const ulong StageSeedSalt = 0xC4A57A6E;

        private readonly GameBalance _balance;
        private readonly Dictionary<int, StageData> _cache = new Dictionary<int, StageData>();
        private readonly object _lock = new object();

        public StageCatalog(GameBalance balance)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public int TotalCampaignStages => _balance.Story.TotalStages;

        public StageData Get(int stageId)
        {
            if (stageId < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(stageId), "Stages start at 1.");
            }
            lock (_lock)
            {
                if (!_cache.TryGetValue(stageId, out StageData stage))
                {
                    stage = Build(stageId);
                    _cache[stageId] = stage;
                }
                return stage;
            }
        }

        public IEnumerable<StageData> GetRange(int firstId, int lastId)
        {
            for (int id = firstId; id <= lastId; id++)
            {
                yield return Get(id);
            }
        }

        /// <summary>Act (1-5), chapter (1-50) and index in chapter (1-20).</summary>
        public void Locate(int stageId, out int act, out int chapter, out int indexInChapter)
        {
            StoryBalance s = _balance.Story;
            int campaignId = CampaignEquivalent(stageId);
            int perAct = s.ChaptersPerAct * s.StagesPerChapter;
            act = (campaignId - 1) / perAct + 1;
            chapter = (campaignId - 1) / s.StagesPerChapter + 1;
            indexInChapter = (campaignId - 1) % s.StagesPerChapter + 1;
        }

        /// <summary>Difficulty permille: 0 at stage 1, stage/total afterwards, -3% relief on the 3 stages after a boss.</summary>
        public int DifficultyForStage(int stageId)
        {
            StoryBalance s = _balance.Story;
            if (stageId <= 1)
            {
                return 0;
            }
            if (stageId > s.TotalStages)
            {
                return 1000;
            }

            int raw = (int)((long)stageId * 1000 / s.TotalStages);
            int index = (stageId - 1) % s.StagesPerChapter + 1;
            int chapter = (stageId - 1) / s.StagesPerChapter + 1;
            if (chapter > 1 && index <= 3)
            {
                raw -= 30;
            }
            return Math.Max(0, Math.Min(1000, raw));
        }

        private int CampaignEquivalent(int stageId)
        {
            int total = _balance.Story.TotalStages;
            if (stageId <= total)
            {
                return stageId;
            }
            int cycle = Math.Min(EndlessCycle, total);
            return total - cycle + 1 + (stageId - total - 1) % cycle;
        }

        private StageData Build(int id)
        {
            StoryBalance s = _balance.Story;
            bool endless = id > s.TotalStages;
            Locate(id, out int act, out int chapter, out int index);
            int d = DifficultyForStage(id);
            var rng = DeterministicRandom.Derive(StageSeedSalt, (ulong)id);

            var stage = new StageData
            {
                Id = id,
                Act = act,
                Chapter = endless ? s.Acts * s.ChaptersPerAct + (id - s.TotalStages - 1) / s.StagesPerChapter + 1 : chapter,
                IndexInChapter = index,
                Kingdom = (Kingdom)((act - 1) % 5),
                DifficultyPermille = d,
                ColorCount = id <= 10 ? 5 : 6,
                Seed = StableHash.Mix(StageSeedSalt, (ulong)id),
                IsEndless = endless,
                CharacterId = StoryDatabase.FeaturedCharacterFor(Math.Min(id, s.TotalStages))
            };

            stage.BossKind = BossKindFor(id, act, chapter, index, endless);
            stage.TimeLimitMs = Lerp(s.EasyTimeLimitMs, s.HardTimeLimitMs, d);
            stage.MoveLimit = Lerp(s.EasyMoveLimit, s.HardMoveLimit, d);
            if (stage.IsBoss)
            {
                stage.MoveLimit += s.BossExtraMoves;
                stage.TimeLimitMs += s.BossExtraTimeMs;
            }

            int factor = Lerp(s.EasyTargetFactorPermille, s.HardTargetFactorPermille, d);
            int target = (int)((long)stage.MoveLimit * s.ExpectedPointsPerMove * factor / 1000);
            target = RoundTo(Math.Max(200, target), 50);

            // Obstacles unlock gradually: stones from stage 21, ice from stage 41.
            int campaignId = CampaignEquivalent(id);
            int stones = campaignId >= 21 && index % 3 != 1 ? 1 + d * 6 / 1000 : 0;
            int iceCells = campaignId >= 41 && index % 4 == 3 ? 4 + d * 12 / 1000 : 0;

            if (stage.IsBoss)
            {
                stage.BossPhases = BossPhasesFor(stage.BossKind);
                stage.BossHp = RoundTo((int)((long)target * 13 / 10), 50);
                stage.BossStonesPerPhase = 1 + (int)stage.BossKind;
                stage.TargetScore = stage.BossHp;
                stage.Objectives.Add(new StageObjective { Type = ObjectiveType.DefeatBoss, Target = stage.BossHp });
                stones = Math.Min(stones, 3);
            }
            else
            {
                switch (index % 5)
                {
                    case 2 when campaignId >= 11:
                        stage.TargetScore = RoundTo(target / 2, 50);
                        stage.Objectives.Add(CollectObjective(stage, rng));
                        break;
                    case 3 when campaignId >= 41:
                        iceCells = Math.Max(iceCells, 6 + d * 10 / 1000);
                        stage.TargetScore = RoundTo(target / 2, 50);
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ClearIce, Target = 0 });
                        break;
                    case 4 when campaignId >= 21:
                        stones = Math.Max(stones, 3 + d * 5 / 1000);
                        stage.TargetScore = RoundTo(target / 2, 50);
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.BreakStones, Target = 0 });
                        break;
                    case 0 when campaignId >= 11:
                        stage.TargetScore = RoundTo(target * 7 / 10, 50);
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ReachScore, Target = stage.TargetScore });
                        stage.Objectives.Add(CollectObjective(stage, rng, 700));
                        break;
                    default:
                        stage.TargetScore = target;
                        stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ReachScore, Target = target });
                        break;
                }
            }

            stage.StoneCount = Math.Min(stones, 12);
            stage.StoneHp = d >= 700 ? 2 : 1;
            stage.IceCells = Math.Min(iceCells, 24);
            stage.IceLayers = 1 + (d >= 600 ? 1 : 0) + (d >= 900 ? 1 : 0);

            int starBase = Math.Max(stage.TargetScore, 200);
            stage.TwoStarScore = RoundTo((int)((long)starBase * s.TwoStarPermille / 1000), 50);
            stage.ThreeStarScore = RoundTo((int)((long)starBase * s.ThreeStarPermille / 1000), 50);

            int coins = Lerp(s.BaseStageCoins, s.MaxDifficultyStageCoins, d);
            if (stage.IsBoss)
            {
                coins = coins * s.BossCoinMultiplierPermille / 1000;
            }
            stage.RewardCoins = coins;
            stage.RewardOrbes = !endless && id % s.OrbeMilestoneInterval == 0 ? s.OrbeMilestoneReward : 0;

            if (!endless)
            {
                if (index == 1)
                {
                    stage.DialogueBeforeId = "dlg.ch" + chapter + ".intro";
                }
                if (index == s.MiniBossIndex)
                {
                    stage.DialogueAfterId = "dlg.ch" + chapter + ".mid";
                }
                if (index == s.FinalBossIndex)
                {
                    stage.DialogueAfterId = "dlg.ch" + chapter + ".outro";
                }
                stage.HasCinematic = stage.BossKind >= BossKind.ChapterBoss || HasCharacterEvent(id);
            }

            if (stage.IsBoss)
            {
                stage.BossId = "boss.a" + act + ".c" + chapter + "." + stage.BossKind.ToString().ToLowerInvariant();
                if (stage.BossKind == BossKind.FinalBoss)
                {
                    stage.BossId = "boss.valdorax";
                }
            }
            return stage;
        }

        private BossKind BossKindFor(int id, int act, int chapter, int index, bool endless)
        {
            StoryBalance s = _balance.Story;
            if (index == s.MiniBossIndex)
            {
                return BossKind.MiniBoss;
            }
            if (index != s.FinalBossIndex)
            {
                return BossKind.None;
            }
            if (!endless && id == s.TotalStages)
            {
                return BossKind.FinalBoss;
            }
            return chapter % s.ChaptersPerAct == 0 ? BossKind.ActBoss : BossKind.ChapterBoss;
        }

        private static int BossPhasesFor(BossKind kind)
        {
            switch (kind)
            {
                case BossKind.MiniBoss: return 2;
                case BossKind.ChapterBoss: return 3;
                case BossKind.ActBoss: return 4;
                default: return 5;
            }
        }

        private static StageObjective CollectObjective(StageData stage, DeterministicRandom rng, int scalePermille = 1000)
        {
            int amount = Math.Max(8, stage.MoveLimit * 450 / 1000 * scalePermille / 1000);
            return new StageObjective
            {
                Type = ObjectiveType.CollectColor,
                Target = amount,
                Color = (PieceColor)rng.NextInt(stage.ColorCount)
            };
        }

        private static bool HasCharacterEvent(int stageId)
        {
            foreach (CharacterDefinition c in StoryDatabase.Characters)
            {
                if (c.JoinStage == stageId || c.LeaveStage == stageId || c.ReturnStage == stageId)
                {
                    return true;
                }
            }
            return false;
        }

        private static int Lerp(int easy, int hard, int permille) => easy + (int)((long)(hard - easy) * permille / 1000);

        private static int RoundTo(int value, int step) => Math.Max(step, (value + step / 2) / step * step);
    }
}
