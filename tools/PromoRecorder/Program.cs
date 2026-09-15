using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;

namespace CrushRoyale.Tools.PromoRecorder
{
    /// <summary>
    /// Plays a scripted PvP match with the real deterministic engine (greedy moves + three power-ups) and writes every
    /// animation step (clears, bonuses, falls, refills, scores) to JSON. tools/promo/render_promo.py turns it into the
    /// promo video, so the gameplay shown is exactly what the game rules produce.
    ///   dotnet run --project tools/PromoRecorder -- art/promo/promo_match.json
    /// </summary>
    internal static class Program
    {
        private const int Moves = 18;
        private const int MultiplierAt = 3;
        private const int NukeAt = 8;
        private const int FireStormAt = 12;

        private static int Main(string[] args)
        {
            string output = args.Length > 0 ? args[0] : Path.Combine("art", "promo", "promo_match.json");
            GameBalance balance = GameBalance.CreateDefault();

            Recording best = null;
            for (ulong seed = 1; seed <= 80; seed++)
            {
                Recording candidate = Play(seed, balance);
                if (best == null || candidate.Excitement > best.Excitement)
                {
                    best = candidate;
                }
            }

            var root = new Dictionary<string, object>
            {
                ["seed"] = best.Seed.ToString(),
                ["width"] = balance.Board.Width,
                ["height"] = balance.Board.Height,
                ["timeLimitMs"] = balance.Pvp.TimeLimitMs,
                ["initial"] = best.Initial,
                ["actions"] = best.Actions,
                ["finalScore"] = best.FinalScore,
                ["opponent"] = Opponent(best.Seed + 1000, balance, best.LastTimeMs + 4000)
            };

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonSerializer.Serialize(root));
            Console.WriteLine("Seed " + best.Seed + ": " + best.Actions.Count + " actions, score " + best.FinalScore + ", excitement " + best.Excitement + " -> " + output);
            return 0;
        }

        private sealed class Recording
        {
            public ulong Seed;
            public List<Dictionary<string, object>> Initial;
            public List<Dictionary<string, object>> Actions = new List<Dictionary<string, object>>();
            public int Excitement;
            public long FinalScore;
            public int LastTimeMs;
            public int PowerUpsAccepted;
        }

        private static Recording Play(ulong seed, GameBalance balance)
        {
            var loadout = new[]
            {
                new LoadoutEntry(PowerUpType.Multiplier2x, 1),
                new LoadoutEntry(PowerUpType.NuclearBomb, 1),
                new LoadoutEntry(PowerUpType.FireStorm, 1)
            };
            SessionConfig config = SessionConfig.ForPvp(seed, balance, GameMode.PvpRanked, loadout, League.Master);
            var session = new GameSession(config, balance, "promo");
            var rec = new Recording { Seed = seed, Initial = DumpBoard(session.Board) };

            int t = 1500;
            for (int i = 0; i < Moves && session.IsRunning; i++)
            {
                if (i == MultiplierAt)
                {
                    t = Next(session, t);
                    Record(rec, session.ActivatePowerUp(PowerUpType.Multiplier2x, null, t));
                }
                if (i == NukeAt)
                {
                    t = Next(session, t);
                    Record(rec, session.ActivatePowerUp(PowerUpType.NuclearBomb, BestBlastTarget(session.Board), t));
                }
                if (i == FireStormAt)
                {
                    t = Next(session, t);
                    Record(rec, session.ActivatePowerUp(PowerUpType.FireStorm, null, t));
                }

                Move? hint = session.BoardManager.GetHint();
                if (!hint.HasValue)
                {
                    break;
                }
                t = Next(session, t);
                Record(rec, session.AttemptMove(hint.Value.From, hint.Value.To, t));
            }

            rec.FinalScore = session.Score;
            rec.LastTimeMs = t;
            if (rec.PowerUpsAccepted < 3)
            {
                rec.Excitement -= 100000;
            }
            return rec;
        }

        private static int Next(GameSession session, int t) => Math.Max(session.NextActionAllowedAtMs, t) + 650;

        /// <summary>The cell whose 5x5 neighbourhood holds the most bonus pieces (then gems), closest to the centre.</summary>
        private static Pos BestBlastTarget(GameBoard board)
        {
            Pos best = new Pos(board.Width / 2, board.Height / 2);
            double bestValue = double.MinValue;
            for (int x = 1; x < board.Width - 1; x++)
            {
                for (int y = 1; y < board.Height - 1; y++)
                {
                    double value = 0;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= board.Width || ny >= board.Height)
                            {
                                continue;
                            }
                            Piece p = board[nx, ny];
                            value += p.IsSpecial ? 10 : p.IsEmpty ? 0 : 1;
                        }
                    }
                    value -= (Math.Abs(x - board.Width / 2.0) + Math.Abs(y - board.Height / 2.0)) * 0.3;
                    if (value > bestValue)
                    {
                        bestValue = value;
                        best = new Pos(x, y);
                    }
                }
            }
            return best;
        }

        private static void Record(Recording rec, ActionOutcome outcome)
        {
            if (!outcome.Accepted)
            {
                return;
            }

            PlayerAction action = outcome.Action;
            var entry = new Dictionary<string, object>
            {
                ["kind"] = action.Type == ActionType.PowerUp ? "powerup" : "swap",
                ["t"] = action.TimestampMs,
                ["points"] = outcome.PointsGained,
                ["score"] = outcome.ScoreAfter,
                ["mult"] = outcome.ScoreMultiplierPermille,
                ["surge"] = outcome.RedSurgeActivated
            };
            if (action.Type == ActionType.PowerUp)
            {
                entry["power"] = action.PowerUp.ToString();
                if (action.HasTarget)
                {
                    entry["target"] = new[] { action.Target.X, action.Target.Y };
                }
                rec.PowerUpsAccepted++;
                rec.Excitement += 20;
            }
            else
            {
                entry["from"] = new[] { action.From.X, action.From.Y };
                entry["to"] = new[] { action.To.X, action.To.Y };
            }

            var steps = new List<Dictionary<string, object>>();
            if (outcome.Resolution != null)
            {
                foreach (ResolutionStep step in outcome.Resolution.Steps)
                {
                    steps.Add(new Dictionary<string, object>
                    {
                        ["level"] = step.CascadeLevel,
                        ["base"] = step.BasePoints,
                        ["cleared"] = step.Cleared.Select(c => new Dictionary<string, object>
                        {
                            ["id"] = c.Piece.Id, ["x"] = c.Position.X, ["y"] = c.Position.Y, ["c"] = (int)c.Piece.Color, ["t"] = (int)c.Piece.Type, ["cause"] = c.Cause.ToString()
                        }).ToList(),
                        ["specials"] = step.SpecialsCreated.Select(Spawn).ToList(),
                        ["falls"] = step.Falls.Select(f => new Dictionary<string, object>
                        {
                            ["id"] = f.PieceId, ["fx"] = f.From.X, ["fy"] = f.From.Y, ["tx"] = f.To.X, ["ty"] = f.To.Y
                        }).ToList(),
                        ["refills"] = step.Refills.Select(Spawn).ToList(),
                        ["stones"] = step.StoneHits.Select(s => new Dictionary<string, object>
                        {
                            ["id"] = s.PieceId, ["x"] = s.Position.X, ["y"] = s.Position.Y, ["hp"] = s.RemainingHp
                        }).ToList()
                    });
                    rec.Excitement += step.Cleared.Count + step.SpecialsCreated.Count * 6 + step.CascadeLevel * 4;
                }
                if (outcome.Resolution.Shuffled && outcome.Resolution.BoardAfterShuffle != null)
                {
                    entry["shuffle"] = DumpBoard(outcome.Resolution.BoardAfterShuffle);
                }
            }
            entry["steps"] = steps;
            rec.Actions.Add(entry);
        }

        private static Dictionary<string, object> Spawn(PieceSpawn s) => new Dictionary<string, object>
        {
            ["id"] = s.Piece.Id, ["x"] = s.Position.X, ["y"] = s.Position.Y, ["row"] = s.SpawnRow, ["c"] = (int)s.Piece.Color, ["t"] = (int)s.Piece.Type, ["hp"] = (int)s.Piece.Hp
        };

        private static List<Dictionary<string, object>> DumpBoard(GameBoard board)
        {
            var pieces = new List<Dictionary<string, object>>();
            for (int x = 0; x < board.Width; x++)
            {
                for (int y = 0; y < board.Height; y++)
                {
                    Piece p = board[x, y];
                    if (!p.IsEmpty)
                    {
                        pieces.Add(new Dictionary<string, object> { ["id"] = p.Id, ["x"] = x, ["y"] = y, ["c"] = (int)p.Color, ["t"] = (int)p.Type, ["hp"] = (int)p.Hp });
                    }
                }
            }
            return pieces;
        }

        /// <summary>Score timeline of a second (greedy, slower) player on another seed: the rival shown in the duel HUD.</summary>
        private static List<long[]> Opponent(ulong seed, GameBalance balance, int untilMs)
        {
            SessionConfig config = SessionConfig.ForPvp(seed, balance, GameMode.PvpRanked, new List<LoadoutEntry>(), League.Master);
            var session = new GameSession(config, balance, "rival");
            var timeline = new List<long[]> { new long[] { 0, 0 } };
            int t = 2200;
            while (session.IsRunning && t < untilMs)
            {
                Move? hint = session.BoardManager.GetHint();
                if (!hint.HasValue)
                {
                    break;
                }
                t = Math.Max(session.NextActionAllowedAtMs, t) + 1150;
                if (session.AttemptMove(hint.Value.From, hint.Value.To, t).Accepted)
                {
                    timeline.Add(new long[] { t, session.Score });
                }
            }
            return timeline;
        }
    }
}
