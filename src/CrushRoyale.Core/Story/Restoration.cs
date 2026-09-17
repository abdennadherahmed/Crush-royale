using System;
using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Story
{
    public sealed class RestorationTask
    {
        /// <summary>"z1.t3": zone 1, task 3 (also the localization and art key).</summary>
        public string Id { get; internal set; }

        public int Zone { get; internal set; }

        /// <summary>Stars spent to build it.</summary>
        public int Cost { get; internal set; }
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
        public const int TasksPerZone = 6;

        private static readonly int[][] Costs =
        {
            new[] { 1, 1, 2, 2, 2, 3 },
            new[] { 2, 2, 3, 3, 3, 4 },
            new[] { 3, 3, 4, 4, 4, 5 },
            new[] { 4, 4, 5, 5, 5, 6 },
            new[] { 5, 5, 6, 6, 6, 8 }
        };

        private static readonly PetType[] Pets = { PetType.FrostFox, PetType.SunFennec, PetType.ForestOwl, PetType.EmberSalamander, PetType.CrystalDrake };

        public static readonly IReadOnlyList<RestorationZone> Zones = BuildZones();

        private static List<RestorationZone> BuildZones()
        {
            var zones = new List<RestorationZone>();
            for (int z = 0; z < Costs.Length; z++)
            {
                var zone = new RestorationZone { Number = z + 1 };
                for (int t = 0; t < Costs[z].Length; t++)
                {
                    zone.Tasks.Add(new RestorationTask { Id = zone.Id + ".t" + (t + 1), Zone = z + 1, Cost = Costs[z][t] });
                }
                zones.Add(zone);
            }
            return zones;
        }

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

        public static RewardData ZoneReward(int zoneNumber, out int petFragments, out PetType pet)
        {
            int z = Math.Max(1, zoneNumber);
            var reward = new RewardData { Coins = 1500L * z, Orbes = 25 + 15 * (z - 1) };
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
