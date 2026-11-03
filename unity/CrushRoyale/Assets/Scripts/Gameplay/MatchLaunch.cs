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

        /// <summary>
        /// Le duel d'accueil, joue avant meme la creation du heros. Rien n'est soumis, rien n'est recompense :
        /// il n'existe que pour qu'un nouveau joueur ait gagne un duel avant d'avoir vu un menu.
        /// </summary>
        public bool Onboarding { get; private set; }

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

        /// <summary>Practice PvP: a human-paced bot of the player's league plays the same board first and becomes the ghost.</summary>
        public static MatchLaunch OfflinePvp(GameBalance balance, ulong seed, League league = League.Bronze, bool easy = false)
        {
            SessionConfig config = SessionConfig.ForPvp(seed, balance, GameMode.FriendlyChallenge, null, League.Bronze);
            var bot = new GameSession(config, balance, "practice-bot");
            HeadlessRunner.Run(bot, easy ? new SkilledBot(seed, 150, 3200, 4200) : SkilledBot.ForLeague(league, seed));
            return new MatchLaunch
            {
                Mode = GameMode.FriendlyChallenge,
                Offline = true,
                OfflineSeed = seed,
                OfflineGhost = bot.Replay
            };
        }

        /// <summary>
        /// Premier lancement : un duel contre un bot lent, sur le plateau le plus abordable d'un lot de graines.
        ///
        /// Le bot joue son premier coup en 150 ms puis met trois a quatre secondes par coup. Il prend donc
        /// l'avance, le joueur le rattrape, et c'est exactement la forme d'un duel qu'on raconte. Choisir
        /// ensuite la graine ou ce bot finit le plus bas ne change aucune regle : la partie reste celle que le
        /// moteur produit, on choisit seulement laquelle d'une vingtaine on propose en premier.
        /// </summary>
        public static MatchLaunch OnboardingDuel(GameBalance balance, ulong firstSeed = 1, int candidates = 24)
        {
            MatchLaunch easiest = null;
            long lowest = long.MaxValue;
            for (ulong seed = firstSeed; seed < firstSeed + (ulong)candidates; seed++)
            {
                MatchLaunch candidate = OfflinePvp(balance, seed, League.Bronze, easy: true);
                long score = candidate.OfflineGhost?.FinalScore ?? long.MaxValue;
                if (score < lowest)
                {
                    lowest = score;
                    easiest = candidate;
                }
            }
            easiest = easiest ?? OfflinePvp(balance, firstSeed, League.Bronze, easy: true);
            easiest.Onboarding = true;
            return easiest;
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
