using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Story
{
    /// <summary>What one chapter star chest gives (pet fragments are granted by the caller, they are not a wallet item).</summary>
    public sealed class ChapterChestReward
    {
        public RewardData Reward { get; } = new RewardData();

        public int PetFragments { get; internal set; }

        /// <summary>Pet receiving the fragments (rotates with the chapter).</summary>
        public PetType FragmentsPet { get; internal set; }
    }

    /// <summary>
    /// Star chests of a chapter: 3 chests at 30, 45 and 60 stars (half, three quarters and all of the 3-star maximum).
    /// Rewards grow with the tier and the chapter; each chest is claimed once.
    /// </summary>
    public static class ChapterChests
    {
        public const int Tiers = 3;

        // Tier 1: common boosts, tier 2: strong ones, tier 3: the rarest.
        private static readonly PowerUpType[][] Boosts =
        {
            new[] { PowerUpType.ChronoBomb, PowerUpType.CoinBooster, PowerUpType.BrightSpark, PowerUpType.Multiplier2x },
            new[] { PowerUpType.GoldenChain, PowerUpType.Multiplier2x, PowerUpType.BrightSpark, PowerUpType.ChronoBomb },
            new[] { PowerUpType.NuclearBomb, PowerUpType.FireStorm, PowerUpType.CascadeInfinity, PowerUpType.GoldenChain }
        };

        private static readonly PetType[] FragmentPets =
        {
            PetType.FrostFox, PetType.SunFennec, PetType.ForestOwl, PetType.EmberSalamander, PetType.CrystalDrake
        };

        /// <summary>Stars needed for chest <paramref name="tier"/> (0-2) of a chapter.</summary>
        public static int StarsNeeded(StoryBalance story, int tier)
        {
            int max = story.StagesPerChapter * 3;
            switch (tier)
            {
                case 0: return max / 2;
                case 1: return max * 3 / 4;
                default: return max;
            }
        }

        public static int StarsIn(StoryProgress progress, StoryBalance story, int chapter)
        {
            int first = (chapter - 1) * story.StagesPerChapter + 1;
            int stars = 0;
            for (int id = first; id < first + story.StagesPerChapter; id++)
            {
                if (progress.Stages.TryGetValue(id, out StageProgress stage) && stage.EverWon)
                {
                    stars += Math.Max(1, Math.Min(3, stage.BestStars));
                }
            }
            return stars;
        }

        public static string Key(int chapter, int tier) => chapter + ":" + tier;

        public static bool IsClaimed(StoryProgress progress, int chapter, int tier) =>
            progress.ClaimedChapterChests != null && progress.ClaimedChapterChests.Contains(Key(chapter, tier));

        /// <summary>Content of a chest, identical on client and server.</summary>
        public static ChapterChestReward RewardFor(int chapter, int tier)
        {
            chapter = Math.Max(1, chapter);
            tier = Math.Max(0, Math.Min(Tiers - 1, tier));
            var result = new ChapterChestReward();
            long growth = 1000 + 100L * (chapter - 1); // +10% per chapter
            long[] coins = { 300, 600, 1200 };
            long[] orbes = { 0, 10, 30 };
            int[] boosts = { 1, 2, 3 };
            result.Reward.Coins = coins[tier] * growth / 1000;
            result.Reward.Orbes = orbes[tier] + (tier == 0 ? 0 : (chapter - 1) / 5 * (tier == 1 ? 2 : 5));
            PowerUpType[] pool = Boosts[tier];
            for (int i = 0; i < boosts[tier]; i++)
            {
                result.Reward.AddPowerUp(pool[(chapter + i) % pool.Length], 1);
            }
            if (tier == Tiers - 1)
            {
                result.PetFragments = 10 + Math.Min(20, chapter - 1);
                result.FragmentsPet = FragmentPets[(chapter - 1) % FragmentPets.Length];
            }
            return result;
        }

        /// <summary>Marks the chest claimed when the chapter has enough stars. The caller grants the reward.</summary>
        public static OperationResult<ChapterChestReward> Claim(StoryProgress progress, StoryBalance story, int chapter, int tier)
        {
            if (progress == null || story == null)
            {
                throw new ArgumentNullException(progress == null ? nameof(progress) : nameof(story));
            }
            int chapters = story.Acts * story.ChaptersPerAct;
            if (chapter < 1 || chapter > chapters || tier < 0 || tier >= Tiers)
            {
                return OperationResult<ChapterChestReward>.Fail(ErrorCode.InvalidArgument, "Unknown chapter chest.");
            }
            if (IsClaimed(progress, chapter, tier))
            {
                return OperationResult<ChapterChestReward>.Fail(ErrorCode.AlreadyClaimed);
            }
            if (StarsIn(progress, story, chapter) < StarsNeeded(story, tier))
            {
                return OperationResult<ChapterChestReward>.Fail(ErrorCode.LimitReached, "Not enough stars in this chapter.");
            }
            progress.ClaimedChapterChests ??= new HashSet<string>();
            progress.ClaimedChapterChests.Add(Key(chapter, tier));
            return OperationResult<ChapterChestReward>.Ok(RewardFor(chapter, tier));
        }
    }
}
