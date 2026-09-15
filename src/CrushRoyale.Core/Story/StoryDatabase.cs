using System;
using System.Collections.Generic;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Story
{
    public enum CharacterRole : byte
    {
        Hero,
        ElementalMage,
        KnightWarrior,
        RogueScout,
        PaladinHealer,
        RangerHunter,
        ShamanDruid,
        Assassin,
        EnchanterAlchemist,
        Antagonist
    }

    public sealed class CharacterDefinition
    {
        public string Id { get; set; }

        /// <summary>Localization key of the display name.</summary>
        public string NameKey { get; set; }

        public CharacterRole Role { get; set; }

        public int JoinStage { get; set; }

        /// <summary>Stage after which the character leaves the party (death, betrayal). 0 = stays.</summary>
        public int LeaveStage { get; set; }

        /// <summary>Stage where the character comes back if <see cref="ReturnFlag"/> is set. 0 = never.</summary>
        public int ReturnStage { get; set; }

        public string ReturnFlag { get; set; }

        /// <summary>Joins only if this flag is set (optional companions).</summary>
        public string RequiredFlag { get; set; }

        public bool Optional => RequiredFlag != null;

        public Kingdom Origin { get; set; }
    }

    public enum StoryEventTrigger : byte
    {
        BeforeStage = 0,
        AfterWin = 1
    }

    public sealed class StoryEvent
    {
        public string Id { get; set; }

        public int StageId { get; set; }

        public StoryEventTrigger Trigger { get; set; }

        public string DialogueId { get; set; }

        public bool Cinematic { get; set; }

        public string CharacterJoins { get; set; }

        public string CharacterLeaves { get; set; }

        public string ChoiceId { get; set; }

        public string RequiresFlag { get; set; }
    }

    public sealed class StoryChoiceOption
    {
        public string Id { get; set; }

        public string TextKey { get; set; }

        public string SetsFlag { get; set; }

        /// <summary>All flags required for the option to be selectable.</summary>
        public List<string> RequiresFlags { get; set; } = new List<string>();
    }

    public sealed class StoryChoice
    {
        public string Id { get; set; }

        public int StageId { get; set; }

        public string PromptKey { get; set; }

        public List<StoryChoiceOption> Options { get; set; } = new List<StoryChoiceOption>();
    }

    public enum StoryEnding : byte
    {
        Sacrifice = 0,
        Corruption = 1,
        Redemption = 2
    }

    /// <summary>
    /// Narrative data of Crystalheim. GDD fixes: Mira dies in Act 4 protecting the party from Mark (the story beats
    /// version; the character sheet said Act 3). Mark's arc carries the main twist so he is mandatory; Soren stays optional.
    /// Dialogue text lives in localization files keyed by the ids generated here.
    /// </summary>
    public static class StoryDatabase
    {
        public const string FlagTrustMira = "trust_mira";
        public const string FlagDoubtMira = "doubt_mira";
        public const string FlagLyraForgiven = "lyra_forgiven";
        public const string FlagLyraCondemned = "lyra_condemned";
        public const string FlagMarkSpared = "mark_spared";
        public const string FlagMarkExiled = "mark_exiled";
        public const string FlagSorenRecruited = "soren_recruited";
        public const string FlagSorenRefused = "soren_refused";
        public const string FinalChoiceId = "final_choice";

        private static readonly object CacheLock = new object();
        private static readonly Dictionary<int, List<StoryEvent>> EventCache = new Dictionary<int, List<StoryEvent>>();

        public static readonly IReadOnlyList<CharacterDefinition> Characters = new List<CharacterDefinition>
        {
            new CharacterDefinition { Id = "hero", NameKey = "char.hero", Role = CharacterRole.Hero, JoinStage = 1, Origin = Kingdom.North },
            new CharacterDefinition { Id = "lyra", NameKey = "char.lyra", Role = CharacterRole.ElementalMage, JoinStage = 50, Origin = Kingdom.North },
            new CharacterDefinition { Id = "kael", NameKey = "char.kael", Role = CharacterRole.KnightWarrior, JoinStage = 80, Origin = Kingdom.North },
            new CharacterDefinition { Id = "mira", NameKey = "char.mira", Role = CharacterRole.RogueScout, JoinStage = 250, LeaveStage = 760, Origin = Kingdom.East },
            new CharacterDefinition { Id = "thorin", NameKey = "char.thorin", Role = CharacterRole.PaladinHealer, JoinStage = 280, LeaveStage = 520, Origin = Kingdom.East },
            new CharacterDefinition { Id = "zara", NameKey = "char.zara", Role = CharacterRole.RangerHunter, JoinStage = 500, Origin = Kingdom.West },
            new CharacterDefinition { Id = "elian", NameKey = "char.elian", Role = CharacterRole.ShamanDruid, JoinStage = 550, Origin = Kingdom.West },
            new CharacterDefinition { Id = "mark", NameKey = "char.mark", Role = CharacterRole.Assassin, JoinStage = 750, LeaveStage = 760, ReturnStage = 900, ReturnFlag = FlagMarkSpared, Origin = Kingdom.South },
            new CharacterDefinition { Id = "soren", NameKey = "char.soren", Role = CharacterRole.EnchanterAlchemist, JoinStage = 801, RequiredFlag = FlagSorenRecruited, Origin = Kingdom.South },
            new CharacterDefinition { Id = "valdorax", NameKey = "char.valdorax", Role = CharacterRole.Antagonist, JoinStage = 0, Origin = Kingdom.Central }
        };

        public static readonly IReadOnlyList<StoryChoice> Choices = new List<StoryChoice>
        {
            new StoryChoice
            {
                Id = "trust_mira", StageId = 260, PromptKey = "choice.trust_mira",
                Options =
                {
                    new StoryChoiceOption { Id = "trust", TextKey = "choice.trust_mira.trust", SetsFlag = FlagTrustMira },
                    new StoryChoiceOption { Id = "doubt", TextKey = "choice.trust_mira.doubt", SetsFlag = FlagDoubtMira }
                }
            },
            new StoryChoice
            {
                Id = "lyra_truth", StageId = 400, PromptKey = "choice.lyra_truth",
                Options =
                {
                    new StoryChoiceOption { Id = "forgive", TextKey = "choice.lyra_truth.forgive", SetsFlag = FlagLyraForgiven },
                    new StoryChoiceOption { Id = "condemn", TextKey = "choice.lyra_truth.condemn", SetsFlag = FlagLyraCondemned }
                }
            },
            new StoryChoice
            {
                Id = "spare_mark", StageId = 760, PromptKey = "choice.spare_mark",
                Options =
                {
                    new StoryChoiceOption { Id = "spare", TextKey = "choice.spare_mark.spare", SetsFlag = FlagMarkSpared },
                    new StoryChoiceOption { Id = "exile", TextKey = "choice.spare_mark.exile", SetsFlag = FlagMarkExiled }
                }
            },
            new StoryChoice
            {
                Id = "recruit_soren", StageId = 800, PromptKey = "choice.recruit_soren",
                Options =
                {
                    new StoryChoiceOption { Id = "recruit", TextKey = "choice.recruit_soren.recruit", SetsFlag = FlagSorenRecruited },
                    new StoryChoiceOption { Id = "refuse", TextKey = "choice.recruit_soren.refuse", SetsFlag = FlagSorenRefused }
                }
            },
            new StoryChoice
            {
                Id = FinalChoiceId, StageId = 1000, PromptKey = "choice.final",
                Options =
                {
                    new StoryChoiceOption { Id = "sacrifice", TextKey = "choice.final.sacrifice", SetsFlag = "ending_sacrifice" },
                    new StoryChoiceOption { Id = "corruption", TextKey = "choice.final.corruption", SetsFlag = "ending_corruption" },
                    new StoryChoiceOption { Id = "redemption", TextKey = "choice.final.redemption", SetsFlag = "ending_redemption", RequiresFlags = { FlagLyraForgiven, FlagMarkSpared } }
                }
            }
        };

        public static CharacterDefinition GetCharacter(string id)
        {
            foreach (CharacterDefinition c in Characters)
            {
                if (c.Id == id)
                {
                    return c;
                }
            }
            return null;
        }

        public static StoryChoice GetChoice(string id)
        {
            foreach (StoryChoice c in Choices)
            {
                if (c.Id == id)
                {
                    return c;
                }
            }
            return null;
        }

        /// <summary>Is the character in the party at this stage given the player's flags?</summary>
        public static bool IsInParty(CharacterDefinition c, int stageId, ICollection<string> flags)
        {
            if (c.Role == CharacterRole.Antagonist || stageId < c.JoinStage)
            {
                return false;
            }
            if (c.RequiredFlag != null && (flags == null || !flags.Contains(c.RequiredFlag)))
            {
                return false;
            }
            if (c.LeaveStage > 0 && stageId > c.LeaveStage)
            {
                return c.ReturnStage > 0 && stageId >= c.ReturnStage && flags != null && flags.Contains(c.ReturnFlag);
            }
            return true;
        }

        /// <summary>Companion shown on a stage: rotates through the current party, the hero on early stages.</summary>
        public static string FeaturedCharacterFor(int stageId)
        {
            var party = new List<string>();
            foreach (CharacterDefinition c in Characters)
            {
                if (c.Role != CharacterRole.Antagonist && !c.Optional && stageId >= c.JoinStage && (c.LeaveStage == 0 || stageId <= c.LeaveStage))
                {
                    party.Add(c.Id);
                }
            }
            return party.Count == 0 ? "hero" : party[(stageId * 7) % party.Count];
        }

        /// <summary>
        /// All narrative events for a campaign structure: chapter intro / midpoint / outro, joins, departures,
        /// choices and the ending. Sorted by stage then trigger.
        /// </summary>
        public static IReadOnlyList<StoryEvent> GetEvents(StoryBalance story)
        {
            if (story == null)
            {
                throw new ArgumentNullException(nameof(story));
            }

            int key = story.TotalStages * 31 + story.StagesPerChapter;
            lock (CacheLock)
            {
                if (EventCache.TryGetValue(key, out List<StoryEvent> cached))
                {
                    return cached;
                }

                var events = new List<StoryEvent>();
                int chapters = story.Acts * story.ChaptersPerAct;
                for (int chapter = 1; chapter <= chapters; chapter++)
                {
                    int first = (chapter - 1) * story.StagesPerChapter + 1;
                    int mini = first + story.MiniBossIndex - 1;
                    int last = first + story.StagesPerChapter - 1;
                    bool actStart = (chapter - 1) % story.ChaptersPerAct == 0;

                    events.Add(new StoryEvent { Id = "ch" + chapter + "_intro", StageId = first, Trigger = StoryEventTrigger.BeforeStage, DialogueId = "dlg.ch" + chapter + ".intro", Cinematic = actStart });
                    events.Add(new StoryEvent { Id = "ch" + chapter + "_mid", StageId = mini, Trigger = StoryEventTrigger.AfterWin, DialogueId = "dlg.ch" + chapter + ".mid" });
                    events.Add(new StoryEvent { Id = "ch" + chapter + "_outro", StageId = last, Trigger = StoryEventTrigger.AfterWin, DialogueId = "dlg.ch" + chapter + ".outro", Cinematic = true });
                }

                foreach (CharacterDefinition c in Characters)
                {
                    if (c.Role == CharacterRole.Antagonist || c.Role == CharacterRole.Hero)
                    {
                        continue;
                    }
                    events.Add(new StoryEvent { Id = c.Id + "_joins", StageId = c.JoinStage, Trigger = StoryEventTrigger.BeforeStage, DialogueId = "dlg." + c.Id + ".join", Cinematic = true, CharacterJoins = c.Id, RequiresFlag = c.RequiredFlag });
                    if (c.LeaveStage > 0)
                    {
                        events.Add(new StoryEvent { Id = c.Id + "_leaves", StageId = c.LeaveStage, Trigger = StoryEventTrigger.AfterWin, DialogueId = "dlg." + c.Id + ".leave", Cinematic = true, CharacterLeaves = c.Id });
                    }
                    if (c.ReturnStage > 0)
                    {
                        events.Add(new StoryEvent { Id = c.Id + "_returns", StageId = c.ReturnStage, Trigger = StoryEventTrigger.BeforeStage, DialogueId = "dlg." + c.Id + ".return", Cinematic = true, CharacterJoins = c.Id, RequiresFlag = c.ReturnFlag });
                    }
                }

                foreach (StoryChoice choice in Choices)
                {
                    events.Add(new StoryEvent { Id = choice.Id, StageId = choice.StageId, Trigger = StoryEventTrigger.AfterWin, DialogueId = "dlg.choice." + choice.Id, ChoiceId = choice.Id, Cinematic = choice.Id == FinalChoiceId });
                }

                events.Sort((a, b) =>
                {
                    int byStage = a.StageId.CompareTo(b.StageId);
                    if (byStage != 0)
                    {
                        return byStage;
                    }
                    int byTrigger = a.Trigger.CompareTo(b.Trigger);
                    // Departures play before choices on the same stage (Mira's death precedes the choice about Mark).
                    return byTrigger != 0 ? byTrigger : Rank(a).CompareTo(Rank(b));
                });

                EventCache[key] = events;
                return events;
            }
        }

        public static bool IsOptionAvailable(StoryChoiceOption option, ICollection<string> flags)
        {
            foreach (string required in option.RequiresFlags)
            {
                if (flags == null || !flags.Contains(required))
                {
                    return false;
                }
            }
            return true;
        }

        public static List<StoryEnding> AvailableEndings(ICollection<string> flags)
        {
            var endings = new List<StoryEnding>();
            foreach (StoryChoiceOption option in GetChoice(FinalChoiceId).Options)
            {
                if (IsOptionAvailable(option, flags))
                {
                    endings.Add((StoryEnding)Enum.Parse(typeof(StoryEnding), option.Id, true));
                }
            }
            return endings;
        }

        private static int Rank(StoryEvent e)
        {
            if (e.CharacterJoins != null)
            {
                return 1;
            }
            if (e.CharacterLeaves != null)
            {
                return 2;
            }
            if (e.ChoiceId != null)
            {
                return 3;
            }
            return 0;
        }
    }
}
