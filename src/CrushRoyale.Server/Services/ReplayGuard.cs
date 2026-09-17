using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Replay;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>
/// Checks that a submitted replay belongs to the match the server issued (player, mode, seed, stage, loadout,
/// league, assist, authorized continues) before spending CPU on re-simulation.
/// </summary>
public static class ReplayGuard
{
    public static ErrorCode CheckHeader(ReplayData replay, MatchRow match, Guid playerId)
    {
        if (!replay.IsFinished)
        {
            return ErrorCode.ReplayInvalid;
        }
        if (!string.Equals(replay.PlayerId, playerId.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return ErrorCode.ReplayMismatch;
        }
        if (replay.Mode != match.Mode || replay.Seed != match.Seed || replay.StageId != match.StageId)
        {
            return ErrorCode.ReplayMismatch;
        }
        if (replay.HighestLeague != match.Config.HighestLeague || replay.AssistExtraMoves != match.Config.AssistExtraMoves
            || replay.StartBoosters != match.Config.StartBoosters)
        {
            return ErrorCode.ReplayMismatch;
        }
        if (!SameLoadout(replay.Loadout, match.Config.Loadout))
        {
            return ErrorCode.ReplayMismatch;
        }
        if (replay.ContinuesUsed > match.Config.ContinuesAuthorized)
        {
            return ErrorCode.ReplayMismatch;
        }
        return ErrorCode.None;
    }

    public static bool SameLoadout(IReadOnlyCollection<LoadoutEntry> a, IReadOnlyCollection<LoadoutEntry> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }
        return a.OrderBy(x => x.Type).Zip(b.OrderBy(x => x.Type)).All(p => p.First.Type == p.Second.Type && p.First.Quantity == p.Second.Quantity);
    }

    /// <summary>Header mismatch: flagged for review, never auto-punished (could be a client bug).</summary>
    public static void FlagHeaderMismatch(PlayerWorkspace ws, MatchRow match, ErrorCode error) =>
        ws.AntiCheat.FlagSuspiciousActivity(ws.IdString, FlagReason.ReplayMismatch, CheatSeverity.Suspicious, "Replay header rejected: " + error, match.Id);

    /// <summary>Spends the power-ups a validated replay used; shortfalls are flagged (possible parallel sessions).</summary>
    public static void ConsumePowerUps(PlayerWorkspace ws, IReadOnlyDictionary<Core.Config.PowerUpType, int> used, string matchId)
    {
        foreach (KeyValuePair<Core.Config.PowerUpType, int> kv in used)
        {
            int have = ws.Inventory.Count(kv.Key);
            int spend = Math.Min(have, kv.Value);
            if (spend > 0)
            {
                ws.Inventory.Consume(kv.Key, spend);
            }
            if (spend < kv.Value)
            {
                ws.AntiCheat.FlagSuspiciousActivity(ws.IdString, FlagReason.InventoryMismatch, CheatSeverity.Suspicious,
                    kv.Key + ": used " + kv.Value + ", owned " + have, matchId);
            }
        }
    }
}
