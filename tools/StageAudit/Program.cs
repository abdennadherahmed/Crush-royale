using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Tools.StageAudit
{
    /// <summary>
    /// Plays every campaign stage with bots of three skill levels (several attempts each, with the game's difficulty
    /// assist after repeated failures) and reports win rates, impossible stages and difficulty spikes.
    ///   dotnet run -c Release --project tools/StageAudit -- [attempts] [outputDir]
    /// Bots never use power-ups, continues or pets, so real players have more tools than these numbers suggest.
    /// </summary>
    internal static class Program
    {
        private sealed class Profile
        {
            public string Name;
            public int BestMovePermille;
            public int ThinkMinMs;
            public int ThinkMaxMs;
        }

        private static readonly Profile[] Profiles =
        {
            new Profile { Name = "casual", BestMovePermille = 300, ThinkMinMs = 2500, ThinkMaxMs = 3500 },
            new Profile { Name = "average", BestMovePermille = 500, ThinkMinMs = 2000, ThinkMaxMs = 3000 },
            new Profile { Name = "expert", BestMovePermille = 1000, ThinkMinMs = 900, ThinkMaxMs = 1200 }
        };

        private sealed class StageReport
        {
            public StageData Stage;
            public double[] WinRate = new double[3];
            public double[] WinRateWithAssist = new double[3];
            public long[] AvgScore = new long[3];
        }

        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--bake")
            {
                return Bake(args.Length > 1 ? args[1] : Path.Combine("src", "CrushRoyale.Core", "Story", "StageTuning.g.cs"));
            }
            int attempts = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 6;
            string outputDir = args.Length > 1 ? args[1] : Path.Combine("docs", "balancing");
            GameBalance balance = GameBalance.CreateDefault();
            var catalog = new StageCatalog(balance);
            int total = balance.Story.TotalStages;
            for (int id = 1; id <= total; id++)
            {
                catalog.Get(id); // warm the cache before parallel access
            }

            var reports = new StageReport[total + 1];
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Parallel.For(1, total + 1, id =>
            {
                StageData stage = catalog.Get(id);
                var report = new StageReport { Stage = stage };
                for (int p = 0; p < Profiles.Length; p++)
                {
                    int wins = 0, winsAssist = 0;
                    long score = 0;
                    for (int a = 0; a < attempts; a++)
                    {
                        ulong seed = (ulong)(id * 7919 + p * 104729 + a * 1299709);
                        StageResult plain = Play(stage, balance, Profiles[p], seed, 0);
                        score += plain.FinalScore;
                        if (plain.Won)
                        {
                            wins++;
                            winsAssist++;
                        }
                        else if (Play(stage, balance, Profiles[p], seed + 17, StoryManager.MaxAssistMoves).Won)
                        {
                            // A player who keeps failing gets up to +6 moves from the difficulty assist.
                            winsAssist++;
                        }
                    }
                    report.WinRate[p] = wins / (double)attempts;
                    report.WinRateWithAssist[p] = winsAssist / (double)attempts;
                    report.AvgScore[p] = score / attempts;
                }
                reports[id] = report;
            });

            Directory.CreateDirectory(outputDir);
            WriteCsv(Path.Combine(outputDir, "stage_audit.csv"), reports, total);
            string summary = Summary(reports, total, attempts, watch.Elapsed);
            File.WriteAllText(Path.Combine(outputDir, "STAGE_AUDIT.md"), summary);
            Console.WriteLine(summary);
            return 0;
        }

        /// <summary>
        /// Measures, for every stage and with goals disabled, what the expert bot reaches using all its moves (score, ice
        /// layers, stones, pieces of the collect colour; average of 4 runs) and writes the table the catalog caps goals with.
        /// </summary>
        private static int Bake(string output)
        {
            GameBalance balance = GameBalance.CreateDefault();
            var catalog = new StageCatalog(balance, applyTuning: false);
            int total = balance.Story.TotalStages;
            for (int id = 1; id <= total; id++)
            {
                catalog.Get(id);
            }
            var score = new int[total + 1];
            // What a mid-skill player actually reaches. The expert's reach alone cannot say whether a target is fair:
            // a board the expert exploits can be a wall for everyone else, which is where the unwinnable stages came
            // from. Targets are capped against this second number.
            var average = new int[total + 1];
            var ice = new int[total + 1];
            var stones = new int[total + 1];
            var collect = new int[total + 1];
            var blight = new int[total + 1];
            Profile expert = Profiles[2];
            Profile mid = Profiles[1];
            // Timed stages are measured at a human pace: an expert still needs time to spot moves against the clock.
            var timedExpert = new Profile { Name = "timed-expert", BestMovePermille = 1000, ThinkMinMs = 1500, ThinkMaxMs = 2000 };
            // Median of an odd number of runs, not the mean: one lucky cascade used to set the bar for a whole stage,
            // which is where the "unwinnable" levels came from (the expert cleared them, nobody else could).
            const int runs = 7;
            Parallel.For(1, total + 1, id =>
            {
                StageData stage = catalog.Get(id);
                var s = new int[runs];
                var avg = new int[runs];
                var i = new int[runs];
                var st = new int[runs];
                var co = new int[runs];
                var bl = new int[runs];
                StageObjective collectGoal = stage.Objectives.FirstOrDefault(o => o.Type == ObjectiveType.CollectColor);
                for (int r = 0; r < runs; r++)
                {
                    SessionConfig config = SessionConfig.ForStage(stage, balance, null, League.Bronze);
                    config.Objectives = new List<StageObjective> { new StageObjective { Type = ObjectiveType.ReachScore, Target = int.MaxValue } };
                    var session = new GameSession(config, balance, "bake");
                    int iceBefore = session.Board.TotalIceLayers;
                    StageResult result = HeadlessRunner.Run(session, new ObjectiveBot((ulong)(id * 31 + r * 977), stage.Timed ? timedExpert : expert, stage));
                    s[r] = (int)result.FinalScore;

                    SessionConfig midConfig = SessionConfig.ForStage(stage, balance, null, League.Bronze);
                    midConfig.Objectives = new List<StageObjective> { new StageObjective { Type = ObjectiveType.ReachScore, Target = int.MaxValue } };
                    var midSession = new GameSession(midConfig, balance, "bake-mid");
                    StageResult midResult = HeadlessRunner.Run(midSession, new ObjectiveBot((ulong)(id * 57 + r * 613), mid, stage));
                    avg[r] = (int)midResult.FinalScore;
                    i[r] = iceBefore - session.Board.TotalIceLayers;
                    st[r] = result.StonesDestroyed;
                    co[r] = collectGoal != null ? result.ClearedByColor[(int)collectGoal.Color] : 0;
                    bl[r] = result.BlightDestroyed;
                }
                score[id] = Median(s);
                average[id] = Median(avg);
                ice[id] = Median(i);
                stones[id] = Median(st);
                collect[id] = Median(co);
                blight[id] = Median(bl);
            });

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated> by tools/StageAudit --bake: what the expert bot reaches on each stage with all its moves.");
            sb.AppendLine("// Regenerate after changing stage generation, board rules or scoring. Do not edit by hand. </auto-generated>");
            sb.AppendLine("namespace CrushRoyale.Core.Story");
            sb.AppendLine("{");
            sb.AppendLine("    internal static class StageTuning");
            sb.AppendLine("    {");
            AppendArray(sb, "Score", score);
            sb.AppendLine();
            AppendArray(sb, "Average", average);
            sb.AppendLine();
            AppendArray(sb, "Ice", ice);
            sb.AppendLine();
            AppendArray(sb, "Stones", stones);
            sb.AppendLine();
            AppendArray(sb, "Collect", collect);
            sb.AppendLine();
            AppendArray(sb, "Blight", blight);
            sb.AppendLine("    }");
            sb.AppendLine("}");
            File.WriteAllText(output, sb.ToString());
            Console.WriteLine("Baked " + total + " stages into " + output);
            return 0;
        }

        /// <summary>Middle value of the runs: immune to the one run where a cascade went wild.</summary>
        private static int Median(int[] values)
        {
            var sorted = (int[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        private static void AppendArray(StringBuilder sb, string name, int[] values)
        {
            sb.AppendLine("        public static readonly int[] " + name + " =");
            sb.AppendLine("        {");
            for (int i = 0; i < values.Length; i += 20)
            {
                sb.Append("            ").Append(string.Join(", ", values.Skip(i).Take(20))).AppendLine(i + 20 < values.Length ? "," : string.Empty);
            }
            sb.AppendLine("        };");
        }

        private static StageResult Play(StageData stage, GameBalance balance, Profile profile, ulong seed, int assistMoves)
        {
            SessionConfig config = SessionConfig.ForStage(stage, balance, null, League.Bronze, assistMoves);
            var session = new GameSession(config, balance, "audit");
            return HeadlessRunner.Run(session, new ObjectiveBot(seed, profile, stage));
        }

        /// <summary>
        /// Plays like a person who reads the goal: on ice and stone stages the best move is the one whose matches sit on
        /// ice or next to stones; otherwise the biggest match. Skill = how often it finds that move instead of a random one.
        /// </summary>
        private sealed class ObjectiveBot : IBotStrategy, IPacedBot
        {
            private readonly CrushRoyale.Core.Common.DeterministicRandom _rng;
            private readonly Profile _profile;
            private readonly bool _ice;
            private readonly bool _stones;

            public ObjectiveBot(ulong seed, Profile profile, StageData stage)
            {
                _rng = new CrushRoyale.Core.Common.DeterministicRandom(seed, 0xB07);
                _profile = profile;
                _ice = stage.Objectives.Any(o => o.Type == ObjectiveType.ClearIce);
                _stones = stage.Objectives.Any(o => o.Type == ObjectiveType.BreakStones);
            }

            public int NextThinkTimeMs() => _rng.NextInt(_profile.ThinkMinMs, _profile.ThinkMaxMs + 1);

            public PlayerAction ChooseAction(GameSession session, int nowMs)
            {
                List<CrushRoyale.Core.Board.Move> moves = session.BoardManager.GetValidMoves();
                if (moves.Count == 0)
                {
                    return null;
                }
                CrushRoyale.Core.Board.Move chosen = moves[_rng.NextInt(moves.Count)];
                if (_rng.ChancePermille(_profile.BestMovePermille))
                {
                    int bestValue = int.MinValue;
                    foreach (CrushRoyale.Core.Board.Move move in moves)
                    {
                        int value = Evaluate(session.Board, move);
                        if (value > bestValue)
                        {
                            bestValue = value;
                            chosen = move;
                        }
                    }
                }
                return PlayerAction.Swap(chosen.From, chosen.To, nowMs);
            }

            private int Evaluate(CrushRoyale.Core.Board.GameBoard board, CrushRoyale.Core.Board.Move move)
            {
                CrushRoyale.Core.Board.GameBoard copy = board.Clone();
                copy.Swap(move.From, move.To);
                int value = 0;
                foreach (CrushRoyale.Core.Board.MatchGroup group in CrushRoyale.Core.Board.MatchFinder.FindMatches(copy))
                {
                    value += group.Cells.Count + (int)group.Shape * 3;
                    foreach (CrushRoyale.Core.Common.Pos cell in group.Cells)
                    {
                        if (_ice && copy.IceAt(cell) > 0)
                        {
                            value += 12;
                        }
                        if (_stones)
                        {
                            foreach (CrushRoyale.Core.Common.Pos n in new[] { new CrushRoyale.Core.Common.Pos(cell.X + 1, cell.Y), new CrushRoyale.Core.Common.Pos(cell.X - 1, cell.Y), new CrushRoyale.Core.Common.Pos(cell.X, cell.Y + 1), new CrushRoyale.Core.Common.Pos(cell.X, cell.Y - 1) })
                            {
                                if (copy.InBounds(n) && copy[n].IsStone)
                                {
                                    value += 15;
                                }
                            }
                        }
                    }
                }
                return value;
            }
        }

        private static void WriteCsv(string path, StageReport[] reports, int total)
        {
            var sb = new StringBuilder("stage,act,chapter,index,kingdom,boss,objectives,moves,time_s,target,difficulty");
            foreach (Profile p in Profiles)
            {
                sb.Append(',').Append(p.Name).Append("_win,").Append(p.Name).Append("_win_assist,").Append(p.Name).Append("_avg_score");
            }
            sb.AppendLine();
            for (int id = 1; id <= total; id++)
            {
                StageReport r = reports[id];
                StageData s = r.Stage;
                string objectives = string.Join(" + ", s.Objectives.Select(o => o.Type + (o.Target > 0 ? ":" + o.Target : string.Empty)));
                sb.Append(id).Append(',').Append(s.Act).Append(',').Append(s.Chapter).Append(',').Append(s.IndexInChapter).Append(',')
                  .Append(s.Kingdom).Append(',').Append(s.BossKind).Append(',').Append(objectives).Append(',').Append(s.MoveLimit).Append(',')
                  .Append(s.TimeLimitMs / 1000).Append(',').Append(s.TargetScore).Append(',').Append(s.DifficultyPermille);
                for (int p = 0; p < Profiles.Length; p++)
                {
                    sb.Append(',').Append(Pct(r.WinRate[p])).Append(',').Append(Pct(r.WinRateWithAssist[p])).Append(',').Append(r.AvgScore[p]);
                }
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }

        private static string Summary(StageReport[] reports, int total, int attempts, TimeSpan elapsed)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Stage audit (bots)");
            sb.AppendLine();
            sb.AppendLine($"{total} stages x 3 bot profiles x {attempts} attempts (+ a retry with the +6 moves difficulty assist after a failure). Run time {elapsed.TotalSeconds:F0} s.");
            sb.AppendLine("Bots use no power-ups, continues or pets: real players have more tools.");
            sb.AppendLine();
            sb.AppendLine("| Stages | Casual | Casual + assist | Average | Average + assist | Expert | Expert + assist |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            int[] bands = { 1, 21, 51, 101, 201, 401, 601, 801, total + 1 };
            for (int b = 0; b + 1 < bands.Length; b++)
            {
                IEnumerable<StageReport> band = reports.Skip(bands[b]).Take(bands[b + 1] - bands[b]);
                sb.Append($"| {bands[b]}-{bands[b + 1] - 1} ");
                for (int p = 0; p < Profiles.Length; p++)
                {
                    sb.Append($"| {Pct(band.Average(r => r.WinRate[p]))}% | {Pct(band.Average(r => r.WinRateWithAssist[p]))}% ");
                }
                sb.AppendLine("|");
            }

            // Sawtooth check: win rates by stage kind (stages 14+ where the tiers apply).
            sb.AppendLine();
            sb.AppendLine("## By stage kind (stages 14-1000, no assist)");
            sb.AppendLine();
            sb.AppendLine("| Kind | Stages | Casual | Average | Expert |");
            sb.AppendLine("|---|---|---|---|---|");
            IEnumerable<StageReport> late = reports.Skip(14).Take(total - 13);
            var kinds = new List<(string Name, Func<StageReport, bool> Match)>
            {
                ("Moves, normal", r => !r.Stage.IsBoss && !r.Stage.Timed && r.Stage.Tier == StageTier.Normal),
                ("Timed, normal", r => r.Stage.Timed && r.Stage.Tier == StageTier.Normal),
                ("Breather (after hard)", r => !r.Stage.IsBoss && r.Stage.Tier == StageTier.Normal && (r.Stage.IndexInChapter == 8 || r.Stage.IndexInChapter == 15 || r.Stage.IndexInChapter == 18)),
                ("Hard", r => r.Stage.Tier == StageTier.Hard),
                ("Super hard", r => r.Stage.Tier == StageTier.SuperHard),
                ("Boss", r => r.Stage.IsBoss)
            };
            foreach ((string name, Func<StageReport, bool> match) in kinds)
            {
                List<StageReport> group = late.Where(match).ToList();
                if (group.Count == 0)
                {
                    continue;
                }
                sb.AppendLine($"| {name} | {group.Count} | {Pct(group.Average(r => r.WinRate[0]))}% | {Pct(group.Average(r => r.WinRate[1]))}% | {Pct(group.Average(r => r.WinRate[2]))}% |");
            }

            List<StageReport> impossible = reports.Skip(1).Where(r => r.WinRateWithAssist[2] == 0).ToList();
            sb.AppendLine();
            sb.AppendLine($"## Stages the expert bot never wins, even with the assist: {impossible.Count}");
            foreach (StageReport r in impossible.Take(60))
            {
                sb.AppendLine($"- Stage {r.Stage.Id} ({r.Stage.BossKind}, {string.Join(" + ", r.Stage.Objectives.Select(o => o.Type))}, {r.Stage.MoveLimit} moves, target {r.Stage.TargetScore}): expert avg score {r.AvgScore[2]}");
            }

            List<StageReport> hardForAverage = reports.Skip(1).Where(r => r.WinRateWithAssist[1] == 0).ToList();
            sb.AppendLine();
            sb.AppendLine($"## Stages an average player never wins, even with the assist: {hardForAverage.Count}");
            sb.AppendLine(string.Join(", ", hardForAverage.Take(150).Select(r => r.Stage.Id)));

            // Spikes: a stage much harder than the average of its 10 neighbours (average player, with assist).
            var spikes = new List<string>();
            for (int id = 1; id <= total; id++)
            {
                double local = Enumerable.Range(Math.Max(1, id - 5), Math.Min(total, id + 5) - Math.Max(1, id - 5) + 1)
                    .Where(n => n != id).Average(n => reports[n].WinRateWithAssist[1]);
                if (local - reports[id].WinRateWithAssist[1] >= 0.5)
                {
                    spikes.Add($"{id} ({Pct(reports[id].WinRateWithAssist[1])}% vs ~{Pct(local)}% around, {reports[id].Stage.BossKind}, {string.Join(" + ", reports[id].Stage.Objectives.Select(o => o.Type))})");
                }
            }
            sb.AppendLine();
            sb.AppendLine($"## Difficulty spikes (average player, 50 points below the neighbouring stages): {spikes.Count}");
            foreach (string spike in spikes.Take(80))
            {
                sb.AppendLine("- " + spike);
            }
            return sb.ToString();
        }

        private static int Pct(double value) => (int)Math.Round(value * 100);
    }
}
