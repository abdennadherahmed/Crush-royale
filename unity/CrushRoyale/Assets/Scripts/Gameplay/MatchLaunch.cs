using System.Collections.Generic;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Replay;
using CrushRoyale.Game.Networking;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>Everything needed to open the gameplay screen: an online server ticket, or an offline practice match.</summary>
    public sealed class MatchLaunch
    {
        public GameMode Mode { get; private set; }

        public MatchStartResponse Ticket { get; private set; }

        public int StageId { get; private set; }

        public bool Offline { get; private set; }

        public ulong OfflineSeed { get; private set; }

        public List<LoadoutEntry> OfflineLoadout { get; private set; } = new List<LoadoutEntry>();

        /// <summary>Offline PvP opponent (a bot replay recorded on the same seed).</summary>
        public ReplayData OfflineGhost { get; private set; }

        public static MatchLaunch Online(MatchStartResponse ticket) => new MatchLaunch
        {
            Ticket = ticket,
            Mode = (GameMode)System.Enum.Parse(typeof(GameMode), ticket.Mode, true),
            StageId = ticket.StageId
        };

        public static MatchLaunch OfflineStory(int stageId) => new MatchLaunch
        {
            Mode = GameMode.Story,
            StageId = stageId,
            Offline = true
        };

        /// <summary>Practice PvP: a greedy bot plays the same board first and becomes the ghost.</summary>
        public static MatchLaunch OfflinePvp(GameBalance balance, ulong seed)
        {
            SessionConfig config = SessionConfig.ForPvp(seed, balance, GameMode.FriendlyChallenge, null, League.Bronze);
            var bot = new GameSession(config, balance, "practice-bot");
            HeadlessRunner.Run(bot, new GreedyBot(1400));
            return new MatchLaunch
            {
                Mode = GameMode.FriendlyChallenge,
                Offline = true,
                OfflineSeed = seed,
                OfflineGhost = bot.Replay
            };
        }

        public SessionConfig BuildConfig(BackendManager backend)
        {
            if (Ticket != null)
            {
                return MatchConfigFactory.Build(Ticket, backend.Balance, backend.Catalog);
            }
            if (Mode == GameMode.Story)
            {
                return SessionConfig.ForStage(backend.Catalog.Get(StageId), backend.Balance, OfflineLoadout, League.Bronze);
            }
            return SessionConfig.ForPvp(OfflineSeed, backend.Balance, GameMode.FriendlyChallenge, OfflineLoadout, League.Bronze);
        }

        public GhostPlayer CreateGhost(BackendManager backend)
        {
            if (OfflineGhost != null)
            {
                return new GhostPlayer(OfflineGhost, PvpGhostPlay.GhostConfig(OfflineGhost, backend.Balance), backend.Balance);
            }
            return Ticket?.Ghost != null ? MatchConfigFactory.CreateGhost(Ticket.Ghost, backend.Balance) : null;
        }
    }
}
