using System.Security.Cryptography;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Server.Infrastructure;

namespace CrushRoyale.Server.Services;

/// <summary>Victory chests: start the timer, open when ready or pay orbes to open now.</summary>
public sealed class ChestService
{
    private readonly PlayerOperations _ops;

    public ChestService(PlayerOperations ops)
    {
        _ops = ops;
    }

    public Task<ChestsDto> UnlockAsync(Guid userId, int slot, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ErrorCode error = ws.Chests.StartUnlock(slot, ws.NowMs);
            if (error != ErrorCode.None)
            {
                throw new ApiException(error);
            }
            return Mappers.Chests(ws);
        }, ct);

    /// <summary>Takes the free chest into a slot. It is on its own clock, so it needs no win and no payment.</summary>
    public Task<ChestsDto> TakeFreeAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ws.Chests.TakeFreeChest(ws.NowMs).ValueOrThrow();
            return Mappers.Chests(ws);
        }, ct);

    public Task<ChestOpenResponse> OpenAsync(Guid userId, int slot, ChestOpenRequest? request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ChestSystem chests = ws.Chests;
            long now = ws.NowMs;
            ChestSlotStatus status = chests.Status(slot, now);
            if (status == ChestSlotStatus.Empty)
            {
                throw new ApiException(ErrorCode.NotFound);
            }

            int cost = chests.SkipCostOrbes(slot, now);
            if (cost > 0)
            {
                if (request?.UseOrbes != true)
                {
                    throw new ApiException(ErrorCode.LimitReached, "Chest not ready.");
                }
                if (!ws.Wallet.CanAfford(Currency.Orbes, cost))
                {
                    throw new ApiException(ErrorCode.NotEnoughOrbes);
                }
                ws.Wallet.Debit(Currency.Orbes, cost, TransactionReason.ShopPurchase, "chest-skip:" + chests.State.ChestsOpened).ThrowIfFailed();
            }

            var rng = new DeterministicRandom((ulong)RandomNumberGenerator.GetInt32(int.MaxValue) << 31 | (ulong)RandomNumberGenerator.GetInt32(int.MaxValue));
            ChestContent content = chests.Open(slot, now, cost, rng, ws.State.Pvp.HighestLeague).ValueOrThrow();
            string reference = "chest:" + chests.State.ChestsOpened;
            ws.GrantReward(content.Reward, TransactionReason.QuestReward, reference, reference);
            if (content.PetFragments > 0 && content.FragmentsPet != PetType.None)
            {
                ws.Pets.Get(content.FragmentsPet).Fragments += content.PetFragments;
            }

            return new ChestOpenResponse
            {
                Type = content.Type.ToString(),
                Reward = Mappers.Reward(content.Reward),
                FragmentsPet = content.FragmentsPet == PetType.None ? null : content.FragmentsPet.ToString(),
                PetFragments = content.PetFragments,
                Chests = Mappers.Chests(ws),
                Pets = Mappers.Pets(ws),
                Wallet = Mappers.Wallet(ws),
                Inventory = Mappers.Inventory(ws)
            };
        }, ct);
}
