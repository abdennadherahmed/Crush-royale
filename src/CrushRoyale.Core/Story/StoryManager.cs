using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Story
{
    public enum StageStatus : byte
    {
        Locked = 0,
        Unlocked = 1,
        Won = 2,
        Lost = 3,
        Abandoned = 4
    }

    /// <summary>Game features gated by story progress.</summary>
    public enum Feature : byte
    {
        DailyQuests = 0,
        Ads = 1,
        Shop = 2,
        Friends = 3,
        Pvp = 4,
        BattlePass = 5,
        Guilds = 6
    }

    public sealed class StageProgress
    {
        public int StageId { get; set; }

        public StageStatus LastStatus { get; set; }

        public bool EverWon { get; set; }

        public long BestScore { get; set; }

        public int BestStars { get; set; }

        public int Attempts { get; set; }

        /// <summary>Failures since the last win (drives the difficulty assist).</summary>
        public int ConsecutiveFailures { get; set; }

        public long FirstWonAtUnixMs { get; set; }
    }

    /// <summary>Persisted story state of a player.</summary>
    public sealed class StoryProgress
    {
        public int HighestUnlockedStage { get; set; } = 1;

        public Dictionary<int, StageProgress> Stages { get; set; } = new Dictionary<int, StageProgress>();

        public HashSet<string> Flags { get; set; } = new HashSet<string>();

        public HashSet<string> SeenEvents { get; set; } = new HashSet<string>();

        /// <summary>choiceId -> optionId.</summary>
        public Dictionary<string, string> Choices { get; set; } = new Dictionary<string, string>();

        public StoryEnding? Ending { get; set; }

        /// <summary>Ending B (Corruption) unlocks a New Game+ campaign.</summary>
        public bool NewGamePlusUnlocked { get; set; }

        public int TotalStars { get; set; }

        /// <summary>Chapter star chests already opened ("chapter:tier").</summary>
        public HashSet<string> ClaimedChapterChests { get; set; } = new HashSet<string>();
    }

    public sealed class StageCompletion
    {
        public bool Accepted { get; internal set; }

        public ErrorCode Error { get; internal set; }

        public bool FirstWin { get; internal set; }

        public int Stars { get; internal set; }

        /// <summary>Base coins before VIP / collection / guild bonuses (see Economy.RewardCalculator).</summary>
        public int BaseCoins { get; internal set; }

        public int Orbes { get; internal set; }

        public int NextStage { get; internal set; }

        public List<StoryEvent> Events { get; } = new List<StoryEvent>();

        public List<string> CharactersJoined { get; } = new List<string>();

        public List<Feature> FeaturesUnlocked { get; } = new List<Feature>();
    }

    /// <summary>
    /// Task 8: story progression. Unlocks, completion, rewards, replays, events/dialogues, character roster,
    /// branching choices, endings and the difficulty assist (extra moves after repeated failures, a standard
    /// match-3 retention mechanic missing from the GDD).
    /// </summary>
    public sealed class StoryManager
    {
        public const int FailuresPerAssistStep = 3;
        public const int AssistMovesPerStep = 2;
        public const int MaxAssistMoves = 6;

        private readonly GameBalance _balance;
        private readonly StageCatalog _catalog;
        private readonly IClock _clock;

        public StoryManager(GameBalance balance, StageCatalog catalog, StoryProgress progress, IClock clock)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public StoryProgress Progress { get; }

        public int CurrentStage => Progress.HighestUnlockedStage;

        public bool CanPlay(int stageId) => stageId >= 1 && stageId <= Progress.HighestUnlockedStage;

        /// <summary>Prompt API. Throws for locked stages (callers should check <see cref="CanPlay"/>).</summary>
        public StageData LoadStage(int stageId)
        {
            if (!CanPlay(stageId))
            {
                throw new InvalidOperationException("Stage " + stageId + " is locked.");
            }
            return _catalog.Get(stageId);
        }

        public OperationResult<StageData> TryLoadStage(int stageId) =>
            CanPlay(stageId) ? OperationResult<StageData>.Ok(_catalog.Get(stageId)) : OperationResult<StageData>.Fail(ErrorCode.StageLocked);

        public StageStatus GetStatus(int stageId)
        {
            if (!CanPlay(stageId))
            {
                return StageStatus.Locked;
            }
            return Progress.Stages.TryGetValue(stageId, out StageProgress p) ? p.LastStatus : StageStatus.Unlocked;
        }

        public int GetAssistExtraMoves(int stageId)
        {
            if (!Progress.Stages.TryGetValue(stageId, out StageProgress p) || p.EverWon)
            {
                return 0;
            }
            return Math.Min(MaxAssistMoves, p.ConsecutiveFailures / FailuresPerAssistStep * AssistMovesPerStep);
        }

        /// <summary>Prompt API shape: returns true if the completion was recorded.</summary>
        public bool CompleteStage(int stageId, bool won, int finalScore) => CompleteStage(stageId, won ? StageStatus.Won : StageStatus.Lost, finalScore, won ? 1 : 0).Accepted;

        /// <summary>Records a finished attempt and returns rewards, unlocks and story events to play.</summary>
        public StageCompletion CompleteStage(int stageId, StageStatus status, long finalScore, int stars)
        {
            var completion = new StageCompletion();
            if (!CanPlay(stageId))
            {
                completion.Error = ErrorCode.StageLocked;
                return completion;
            }
            if (status != StageStatus.Won && status != StageStatus.Lost && status != StageStatus.Abandoned)
            {
                completion.Error = ErrorCode.InvalidArgument;
                return completion;
            }

            StageData stage = _catalog.Get(stageId);
            if (!Progress.Stages.TryGetValue(stageId, out StageProgress p))
            {
                p = new StageProgress { StageId = stageId };
                Progress.Stages[stageId] = p;
            }

            p.Attempts++;
            p.LastStatus = status;
            completion.Accepted = true;
            completion.NextStage = GetNextStage(stageId);

            if (status != StageStatus.Won)
            {
                p.ConsecutiveFailures++;
                return completion;
            }

            stars = Math.Max(1, Math.Min(3, stars));
            completion.Stars = stars;
            completion.FirstWin = !p.EverWon;
            p.ConsecutiveFailures = 0;
            p.BestScore = Math.Max(p.BestScore, finalScore);
            if (stars > p.BestStars)
            {
                Progress.TotalStars += stars - p.BestStars;
                p.BestStars = stars;
            }

            if (completion.FirstWin)
            {
                p.EverWon = true;
                p.FirstWonAtUnixMs = TimeUtil.ToUnixMs(_clock.UtcNow);
                completion.BaseCoins = stage.RewardCoins;
                completion.Orbes = stage.RewardOrbes;

                if (stageId == Progress.HighestUnlockedStage)
                {
                    int before = Progress.HighestUnlockedStage;
                    Progress.HighestUnlockedStage = stageId + 1;
                    foreach (Feature f in (Feature[])Enum.GetValues(typeof(Feature)))
                    {
                        int unlock = UnlockStage(f);
                        if (unlock >= before && unlock < Progress.HighestUnlockedStage)
                        {
                            completion.FeaturesUnlocked.Add(f);
                        }
                    }
                }

                completion.Events.AddRange(GetPendingEvents(stageId, StoryEventTrigger.AfterWin));
                foreach (StoryEvent e in GetPendingEvents(stageId + 1, StoryEventTrigger.BeforeStage))
                {
                    if (e.CharacterJoins != null)
                    {
                        completion.CharactersJoined.Add(e.CharacterJoins);
                    }
                }
            }
            else
            {
                completion.BaseCoins = stage.RewardCoins * _balance.Story.ReplayCoinPermille / 1000;
            }

            completion.NextStage = GetNextStage(stageId);
            return completion;
        }

        /// <summary>Prompt API: the next stage to play after <paramref name="currentStage"/>.</summary>
        public int GetNextStage(int currentStage)
        {
            if (currentStage < 1)
            {
                return 1;
            }
            return Math.Min(currentStage + 1, Progress.HighestUnlockedStage);
        }

        /// <summary>Prompt API: campaign completion 0-100 (endless stages don't count).</summary>
        public float GetProgressionPercent()
        {
            int total = _balance.Story.TotalStages;
            int won = Math.Min(total, Progress.HighestUnlockedStage - 1);
            return won * 100f / total;
        }

        public int UnlockStage(Feature feature)
        {
            StoryBalance s = _balance.Story;
            switch (feature)
            {
                case Feature.DailyQuests: return s.UnlockDailyQuestsStage;
                case Feature.Ads: return s.UnlockAdsStage;
                case Feature.Shop: return s.UnlockShopStage;
                case Feature.Friends: return s.UnlockFriendsStage;
                case Feature.Pvp: return s.UnlockPvpStage;
                case Feature.BattlePass: return s.UnlockBattlePassStage;
                default: return s.UnlockGuildsStage;
            }
        }

        /// <summary>A feature unlocks once the gating stage has been won (i.e. the next one is unlocked).</summary>
        public bool IsFeatureUnlocked(Feature feature) => Progress.HighestUnlockedStage > UnlockStage(feature);

        /// <summary>Events not yet seen for a stage/trigger whose flag requirements are met.</summary>
        public List<StoryEvent> GetPendingEvents(int stageId, StoryEventTrigger trigger)
        {
            var list = new List<StoryEvent>();
            foreach (StoryEvent e in StoryDatabase.GetEvents(_balance.Story))
            {
                if (e.StageId != stageId || e.Trigger != trigger || Progress.SeenEvents.Contains(e.Id))
                {
                    continue;
                }
                if (e.RequiresFlag != null && !Progress.Flags.Contains(e.RequiresFlag))
                {
                    continue;
                }
                list.Add(e);
            }
            return list;
        }

        public void MarkEventSeen(string eventId)
        {
            if (!string.IsNullOrEmpty(eventId))
            {
                Progress.SeenEvents.Add(eventId);
            }
        }

        /// <summary>Records a branching decision. A choice can only be made once, after its stage was won.</summary>
        public OperationResult MakeChoice(string choiceId, string optionId)
        {
            StoryChoice choice = StoryDatabase.GetChoice(choiceId);
            if (choice == null)
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            if (Progress.Choices.ContainsKey(choiceId))
            {
                return OperationResult.Fail(ErrorCode.AlreadyClaimed);
            }
            if (!Progress.Stages.TryGetValue(choice.StageId, out StageProgress p) || !p.EverWon)
            {
                return OperationResult.Fail(ErrorCode.StageLocked);
            }

            StoryChoiceOption option = choice.Options.Find(o => o.Id == optionId);
            if (option == null)
            {
                return OperationResult.Fail(ErrorCode.NotFound);
            }
            if (!StoryDatabase.IsOptionAvailable(option, Progress.Flags))
            {
                return OperationResult.Fail(ErrorCode.FeatureLocked, "This path requires earlier decisions.");
            }

            Progress.Choices[choiceId] = optionId;
            if (option.SetsFlag != null)
            {
                Progress.Flags.Add(option.SetsFlag);
            }
            MarkEventSeen(choiceId);

            if (choiceId == StoryDatabase.FinalChoiceId)
            {
                var ending = (StoryEnding)Enum.Parse(typeof(StoryEnding), optionId, true);
                Progress.Ending = ending;
                if (ending == StoryEnding.Corruption)
                {
                    Progress.NewGamePlusUnlocked = true;
                }
            }
            return OperationResult.Ok();
        }

        /// <summary>Current party at the player's progress.</summary>
        public List<CharacterDefinition> GetParty()
        {
            var party = new List<CharacterDefinition>();
            int stage = Math.Min(Progress.HighestUnlockedStage, _balance.Story.TotalStages);
            foreach (CharacterDefinition c in StoryDatabase.Characters)
            {
                if (StoryDatabase.IsInParty(c, stage, Progress.Flags))
                {
                    party.Add(c);
                }
            }
            return party;
        }

        public List<StoryEnding> GetAvailableEndings() => StoryDatabase.AvailableEndings(Progress.Flags);
    }
}
