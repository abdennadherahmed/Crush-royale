using System.Security.Cryptography;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Pets;
using CrushRoyale.Server.Infrastructure;

namespace CrushRoyale.Server.Services;

/// <summary>Pets: summons paid in orbes (server-side randomness, odds shown in the client), equip and awakening.</summary>
public sealed class PetService
{
    private readonly PlayerOperations _ops;

    public PetService(PlayerOperations ops)
    {
        _ops = ops;
    }

    public Task<PetSummonResponse> SummonAsync(Guid userId, PetSummonRequest request, CancellationToken ct)
    {
        int count = request?.Count ?? 0;
        if (count != 1 && count != 10)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "count must be 1 or 10.");
        }

        return _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            PetBalance balance = ws.Balance.Pets;
            int cost = count == 10 ? balance.Summon10CostOrbes : balance.SummonCostOrbes;
            if (!ws.Wallet.CanAfford(Currency.Orbes, cost))
            {
                throw new ApiException(ErrorCode.NotEnoughOrbes);
            }
            string reference = "pets:" + ws.Pets.State.TotalPulls;
            ws.Wallet.Debit(Currency.Orbes, cost, TransactionReason.PetSummon, reference).ThrowIfFailed();

            var rng = new DeterministicRandom((ulong)RandomNumberGenerator.GetInt32(int.MaxValue) << 31 | (ulong)RandomNumberGenerator.GetInt32(int.MaxValue));
            List<SummonPull> pulls = ws.Pets.Summon(count, rng);
            return new PetSummonResponse
            {
                Pulls = pulls.Select(p => new PetPullDto { Pet = p.Pet.ToString(), WholePet = p.WholePet, Duplicate = p.Duplicate, Fragments = p.Fragments }).ToList(),
                Pets = Mappers.Pets(ws),
                Wallet = Mappers.Wallet(ws)
            };
        }, ct);
    }

    public Task<PetActionResponse> EquipAsync(Guid userId, PetRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            PetType pet = Mappers.ParseEnum<PetType>(request?.Pet, "pet");
            ErrorCode error = ws.Pets.Equip(pet);
            if (error != ErrorCode.None)
            {
                throw new ApiException(error, "Pet not owned.");
            }
            return new PetActionResponse { Pets = Mappers.Pets(ws), Wallet = Mappers.Wallet(ws) };
        }, ct);

    public Task<PetActionResponse> UnlockAsync(Guid userId, PetRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            PetType pet = Mappers.ParseEnum<PetType>(request?.Pet, "pet");
            ErrorCode error = ws.Pets.Unlock(pet);
            if (error != ErrorCode.None)
            {
                throw new ApiException(error, error == ErrorCode.NotEnoughItems ? "Not enough fragments." : "Pet cannot be unlocked.");
            }
            return new PetActionResponse { Pets = Mappers.Pets(ws), Wallet = Mappers.Wallet(ws) };
        }, ct);

    public Task<PetActionResponse> AwakenAsync(Guid userId, PetRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            PetType pet = Mappers.ParseEnum<PetType>(request?.Pet, "pet");
            PetCollection pets = ws.Pets;
            if (!pets.CanAwaken(pet, out int fragments, out int coins))
            {
                throw new ApiException(pets.Owns(pet) ? ErrorCode.LimitReached : ErrorCode.NotFound);
            }
            if (pets.Get(pet).Fragments < fragments)
            {
                throw new ApiException(ErrorCode.NotEnoughItems, "Not enough fragments.");
            }
            if (!ws.Wallet.CanAfford(Currency.Coins, coins))
            {
                throw new ApiException(ErrorCode.NotEnoughCoins);
            }
            ws.Wallet.Debit(Currency.Coins, coins, TransactionReason.PetAwaken, "pet-awaken:" + pet + ":" + pets.Get(pet).Level).ThrowIfFailed();
            ErrorCode error = pets.Awaken(pet);
            if (error != ErrorCode.None)
            {
                throw new ApiException(error);
            }
            return new PetActionResponse { Pets = Mappers.Pets(ws), Wallet = Mappers.Wallet(ws) };
        }, ct);
}
