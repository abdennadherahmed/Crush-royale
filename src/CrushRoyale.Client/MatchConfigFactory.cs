using System;
using System.Collections.Generic;
using System.Globalization;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Client
{
    /// <summary>
    /// Turns a server match ticket into the exact <see cref="SessionConfig"/> the server will re-simulate.
    /// Any drift here would make every replay invalid, so this is the only place clients build configs.
    /// </summary>
    public static class MatchConfigFactory
    {
        public static SessionConfig Build(MatchStartResponse start, GameBalance balance, StageCatalog catalog)
        {
            if (start == null)
            {
                throw new ArgumentNullException(nameof(start));
            }
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            var mode = (GameMode)Enum.Parse(typeof(GameMode), start.Mode, true);
            var league = (League)Enum.Parse(typeof(League), start.HighestLeague, true);
            ulong seed = ulong.Parse(start.Seed, NumberStyles.None, CultureInfo.InvariantCulture);
            List<LoadoutEntry> loadout = ParseLoadout(start.Loadout);
            PetType pet = !string.IsNullOrEmpty(start.Pet) && Enum.TryParse(start.Pet, true, out PetType parsed) ? parsed : PetType.None;

            SessionConfig config;
            switch (mode)
            {
                case GameMode.Story:
                    if (catalog == null)
                    {
                        throw new ArgumentNullException(nameof(catalog));
                    }
                    config = SessionConfig.ForStage(catalog.Get(start.StageId), balance, loadout, league, start.AssistExtraMoves)
                        .WithStartBoosters(start.StartBoosters);
                    break;
                case GameMode.GuildBoss:
                    config = SessionConfig.ForGuildBoss(seed, start.StageId, balance, loadout, league);
                    break;
                default:
                    config = SessionConfig.ForPvp(seed, balance, mode, loadout, league);
                    break;
            }
            return config.WithPet(pet, start.PetLevel, balance);
        }

        public static GameSession CreateSession(MatchStartResponse start, GameBalance balance, StageCatalog catalog, string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("Player id (Supabase user id) required: it is embedded in the replay.", nameof(playerId));
            }
            return new GameSession(Build(start, balance, catalog), balance, playerId);
        }

        /// <summary>Ghost playback for PvP / friendly challenges.</summary>
        public static GhostPlayer CreateGhost(GhostDto ghost, GameBalance balance)
        {
            if (ghost?.ReplayBase64 == null)
            {
                return null;
            }
            ReplayData replay = ReplayCodec.Decode(ghost.ReplayBase64);
            return new GhostPlayer(replay, PvpGhostPlay.GhostConfig(replay, balance), balance);
        }

        public static SubmitReplayRequest ToSubmission(string matchId, ReplayData replay) =>
            new SubmitReplayRequest { MatchId = matchId, ReplayBase64 = ReplayCodec.Encode(replay) };

        private static List<LoadoutEntry> ParseLoadout(IEnumerable<LoadoutEntryDto> entries)
        {
            var list = new List<LoadoutEntry>();
            if (entries != null)
            {
                foreach (LoadoutEntryDto e in entries)
                {
                    list.Add(new LoadoutEntry((PowerUpType)Enum.Parse(typeof(PowerUpType), e.Type, true), e.Quantity));
                }
            }
            return list;
        }
    }

    public static class ReplayCodec
    {
        public static string Encode(ReplayData replay) => Convert.ToBase64String(ReplaySerializer.Serialize(replay));

        public static ReplayData Decode(string base64) => ReplaySerializer.Deserialize(Convert.FromBase64String(base64));
    }
}
