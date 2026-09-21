using System;
using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Story
{
    /// <summary>
    /// What a repair task brings back to the zone. Structure tasks uncover a region of the restored painting; the others
    /// add a looping decoration on top of it, which is also exactly what the task card promises the player.
    /// </summary>
    public enum RestorationEffect : byte
    {
        Structure = 0,
        Water = 1,
        Banner = 2,
        Torch = 3,
        Smoke = 4,
        Radiance = 5
    }

    public sealed class RestorationTask
    {
        /// <summary>"z1.t3": zone 1, task 3 (also the localization and art key).</summary>
        public string Id { get; internal set; }

        public int Zone { get; internal set; }

        /// <summary>1-based index inside the zone.</summary>
        public int Index { get; internal set; }

        /// <summary>Stars spent to build it.</summary>
        public int Cost { get; internal set; }

        public RestorationEffect Effect { get; internal set; }

        /// <summary>Localization key of the one-line promise shown on the card ("water flows again").</summary>
        public string EffectKey => EffectKeys[(int)Effect];

        private static readonly string[] EffectKeys =
        {
            "kingdom.fx.structure", "kingdom.fx.water", "kingdom.fx.banner", "kingdom.fx.torch", "kingdom.fx.smoke", "kingdom.fx.radiance"
        };
    }

    public sealed class RestorationZone
    {
        /// <summary>1-based.</summary>
        public int Number { get; internal set; }

        public string Id => "z" + Number;

        public List<RestorationTask> Tasks { get; } = new List<RestorationTask>();

        public int TotalCost => Tasks.Sum(t => t.Cost);
    }

    /// <summary>Spent stars and what has been rebuilt.</summary>
    public sealed class RestorationState
    {
        public int StarsSpent { get; set; }

        public HashSet<string> Built { get; set; } = new HashSet<string>();

        /// <summary>Zones whose completion reward was granted.</summary>
        public HashSet<string> ZonesRewarded { get; set; } = new HashSet<string>();
    }

    public sealed class RestorationBuildResult
    {
        public RestorationTask Task { get; internal set; }

        /// <summary>Set when this task finished its zone: the completion reward to grant.</summary>
        public RestorationZone CompletedZone { get; internal set; }

        public RewardData ZoneReward { get; internal set; }

        public int ZonePetFragments { get; internal set; }

        public PetType ZoneFragmentsPet { get; internal set; }
    }

    /// <summary>
    /// "Rebuild Crystalheim" meta-game: stars earned on stages are spent on repair tasks. Zones open one after the other
    /// (the Market Square, the Crystal Bridge, the Royal Gardens, the Mages' Tower, the Throne Hall); inside the open
    /// zone tasks can be built in any order. Finishing a zone pays a reward. Stars stay counted for chapter chests.
    /// </summary>
    public static class Restoration
    {
        public const int TasksPerZone = 10;

        /// <summary>
        /// Costs are sized against the whole campaign, not the first chapters: 1000 stages x 3 stars = 3000 stars at
        /// most, and the 50 tasks cost 2500 in total, so a player averaging 2.5 stars per stage rebuilds the last piece
        /// of the Throne Hall right as the story ends. Zone totals grow steeply (60 / 180 / 390 / 690 / 1180) while the
        /// first tasks stay at 1-2 stars, so a beginner still sees the square change after two or three stages.
        /// </summary>
        private static readonly int[][] Costs =
        {
            new[] { 1, 1, 2, 2, 3, 4, 6, 8, 13, 20 },
            new[] { 8, 10, 12, 14, 16, 18, 20, 24, 28, 30 },
            new[] { 26, 30, 32, 36, 38, 40, 42, 44, 48, 54 },
            new[] { 50, 56, 60, 64, 68, 72, 74, 78, 82, 86 },
            new[] { 95, 100, 105, 110, 115, 120, 125, 130, 135, 145 }
        };

        /// <summary>
        /// Tasks 1-6 rebuild the painted structures of the zone; tasks 7-10 bring it to life (water, banners, fire,
        /// smoke, lights) so the second half of every zone is what makes the picture move.
        /// </summary>
        private static readonly RestorationEffect[][] Effects =
        {
            Zone(RestorationEffect.Water, RestorationEffect.Banner, RestorationEffect.Torch, RestorationEffect.Radiance),
            Zone(RestorationEffect.Water, RestorationEffect.Banner, RestorationEffect.Torch, RestorationEffect.Radiance),
            Zone(RestorationEffect.Water, RestorationEffect.Torch, RestorationEffect.Smoke, RestorationEffect.Radiance),
            Zone(RestorationEffect.Smoke, RestorationEffect.Torch, RestorationEffect.Banner, RestorationEffect.Radiance),
            Zone(RestorationEffect.Water, RestorationEffect.Torch, RestorationEffect.Banner, RestorationEffect.Radiance)
        };

        private static readonly PetType[] Pets = { PetType.FrostFox, PetType.SunFennec, PetType.ForestOwl, PetType.EmberSalamander, PetType.CrystalDrake };

        public static readonly IReadOnlyList<RestorationZone> Zones = BuildZones();

        /// <summary>Stars needed to rebuild all of Crystalheim (compared against the campaign total in the tests).</summary>
        public static int TotalCost => Zones.Sum(z => z.TotalCost);

        /// <summary>Tasks of every zone, built or not (progress header).</summary>
        public static int TotalTasks => Zones.Count * TasksPerZone;

        private static RestorationEffect[] Zone(RestorationEffect a, RestorationEffect b, RestorationEffect c, RestorationEffect d)
        {
            var effects = new RestorationEffect[TasksPerZone];
            effects[6] = a;
            effects[7] = b;
            effects[8] = c;
            effects[9] = d;
            return effects;
        }

        private static List<RestorationZone> BuildZones()
        {
            var zones = new List<RestorationZone>();
            for (int z = 0; z < Costs.Length; z++)
            {
                var zone = new RestorationZone { Number = z + 1 };
                for (int t = 0; t < Costs[z].Length; t++)
                {
                    zone.Tasks.Add(new RestorationTask
                    {
                        Id = zone.Id + ".t" + (t + 1),
                        Zone = z + 1,
                        Index = t + 1,
                        Cost = Costs[z][t],
                        Effect = Effects[z][t]
                    });
                }
                zones.Add(zone);
            }
            return zones;
        }

        /// <summary>Tasks built across all zones (progress header).</summary>
        public static int BuiltCount(RestorationState state) =>
            state == null ? 0 : Zones.Sum(z => z.Tasks.Count(t => state.Built.Contains(t.Id)));

        public static RestorationTask FindTask(string id) => Zones.SelectMany(z => z.Tasks).FirstOrDefault(t => t.Id == id);

        public static int AvailableStars(StoryProgress progress) =>
            Math.Max(0, progress.TotalStars - (progress.Restoration?.StarsSpent ?? 0));

        public static bool IsZoneComplete(RestorationState state, RestorationZone zone) =>
            state != null && zone.Tasks.All(t => state.Built.Contains(t.Id));

        /// <summary>First zone not finished (null when all of Crystalheim is rebuilt).</summary>
        public static RestorationZone CurrentZone(RestorationState state) => Zones.FirstOrDefault(z => !IsZoneComplete(state, z));

        /// <summary>A task of the open zone that the player can afford right now (drives the hub badge).</summary>
        public static bool CanBuildSomething(StoryProgress progress)
        {
            RestorationZone zone = CurrentZone(progress.Restoration);
            int stars = AvailableStars(progress);
            return zone != null && zone.Tasks.Any(t => !(progress.Restoration?.Built.Contains(t.Id) ?? false) && t.Cost <= stars);
        }

        /// <summary>Zone completion pay-out, scaled on the zone cost (zone 5 is 20x zone 1 in stars, so it cannot pay 5x).</summary>
        private static readonly long[] ZoneCoins = { 2_000, 9_000, 20_000, 36_000, 60_000 };

        private static readonly int[] ZoneOrbes = { 25, 80, 160, 260, 400 };

        public static RewardData ZoneReward(int zoneNumber, out int petFragments, out PetType pet)
        {
            int z = Math.Max(1, Math.Min(Zones.Count, zoneNumber));
            var reward = new RewardData { Coins = ZoneCoins[z - 1], Orbes = ZoneOrbes[z - 1] };
            reward.AddPowerUp(PowerUpType.ChronoBomb, 1 + z / 2);
            reward.AddPowerUp(PowerUpType.GoldenChain, 1 + (z - 1) / 2);
            if (z >= 3)
            {
                reward.AddPowerUp(PowerUpType.NuclearBomb, 1);
            }
            petFragments = z >= 2 ? 10 + 5 * z : 0;
            pet = Pets[(z - 1) % Pets.Length];
            return reward;
        }

        public static OperationResult<RestorationBuildResult> Build(StoryProgress progress, string taskId)
        {
            if (progress == null)
            {
                throw new ArgumentNullException(nameof(progress));
            }
            progress.Restoration ??= new RestorationState();
            RestorationState state = progress.Restoration;
            RestorationTask task = FindTask(taskId);
            if (task == null)
            {
                return OperationResult<RestorationBuildResult>.Fail(ErrorCode.NotFound, "Unknown restoration task.");
            }
            if (state.Built.Contains(task.Id))
            {
                return OperationResult<RestorationBuildResult>.Fail(ErrorCode.AlreadyClaimed);
            }
            RestorationZone current = CurrentZone(state);
            if (current == null || current.Number != task.Zone)
            {
                return OperationResult<RestorationBuildResult>.Fail(ErrorCode.StageLocked, "Finish the previous zone first.");
            }
            if (AvailableStars(progress) < task.Cost)
            {
                return OperationResult<RestorationBuildResult>.Fail(ErrorCode.NotEnoughItems, "Not enough stars.");
            }

            state.StarsSpent += task.Cost;
            state.Built.Add(task.Id);
            var result = new RestorationBuildResult { Task = task };
            if (IsZoneComplete(state, current) && state.ZonesRewarded.Add(current.Id))
            {
                result.CompletedZone = current;
                result.ZoneReward = ZoneReward(current.Number, out int fragments, out PetType pet);
                result.ZonePetFragments = fragments;
                result.ZoneFragmentsPet = pet;
            }
            return OperationResult<RestorationBuildResult>.Ok(result);
        }
    }
}
