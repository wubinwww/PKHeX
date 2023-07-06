using System;
using System.Buffers;

namespace PKHeX.Core;

/// <summary>
/// Encapsulates logic for HOME's Move Relearner feature.
/// </summary>
/// <remarks>
/// If the Entity knew a move at any point in its history, it can be relearned if the current format can learn it.
/// </remarks>
public class LearnGroupHOME : ILearnGroup
{
    public static readonly LearnGroupHOME Instance = new();
    public ushort MaxMoveID => 0;

    public ILearnGroup? GetPrevious(PKM pk, EvolutionHistory history, IEncounterTemplate enc, LearnOption option) => null;
    public bool HasVisited(PKM pk, EvolutionHistory history) => pk is IHomeTrack { HasTracker: true } || !ParseSettings.IgnoreTransferIfNoTracker;

    public bool Check(Span<MoveResult> result, ReadOnlySpan<ushort> current, PKM pk, EvolutionHistory history,
        IEncounterTemplate enc, MoveSourceType types = MoveSourceType.All, LearnOption option = LearnOption.HOME)
    {
        var context = pk.Context;
        if (context == EntityContext.None)
            return false;

        var local = GetCurrent(context);
        var evos = history.Get(context);
        if (history.HasVisitedGen9 && pk is not PK9)
        {
            var instance = LearnGroup9.Instance;
            instance.Check(result, current, pk, history, enc, types, option);
            if (CleanPurge(result, current, pk, types, local, evos))
                return true;
        }
        if (history.HasVisitedSWSH && pk is not PK8)
        {
            var instance = LearnGroup8.Instance;
            instance.Check(result, current, pk, history, enc, types, option);
            if (CleanPurge(result, current, pk, types, local, evos))
                return true;
        }
        if (history.HasVisitedPLA && pk is not PA8)
        {
            var instance = LearnGroup8a.Instance;
            instance.Check(result, current, pk, history, enc, types, option);
            if (CleanPurge(result, current, pk, types, local, evos))
                return true;
        }
        if (history.HasVisitedBDSP && pk is not PB8)
        {
            var instance = LearnGroup8b.Instance;
            instance.Check(result, current, pk, history, enc, types, option);
            if (CleanPurge(result, current, pk, types, local, evos))
                return true;
        }

        if (TryAddSpecialCaseMoves(pk.Species, result, current))
            return true;

        if (history.HasVisitedLGPE)
        {
            var instance = LearnGroup7b.Instance;
            instance.Check(result, current, pk, history, enc, types, option);
            if (CleanPurge(result, current, pk, types, local, evos))
                return true;
        }
        else if (history.HasVisitedGen7)
        {
            ILearnGroup instance = LearnGroup7.Instance;
            while (true)
            {
                instance.Check(result, current, pk, history, enc, types, option);
                if (CleanPurge(result, current, pk, types, local, evos))
                    return true;
                var prev = instance.GetPrevious(pk, history, enc, option);
                if (prev is null)
                    break;
                instance = prev;
            }
        }
        return false;
    }

    /// <summary>
    /// Scan the results and remove any that are not valid for the game <see cref="local"/> game.
    /// </summary>
    /// <returns>True if all results are valid.</returns>
    private static bool CleanPurge(Span<MoveResult> result, ReadOnlySpan<ushort> current, PKM pk, MoveSourceType types, IHomeSource local, ReadOnlySpan<EvoCriteria> evos)
    {
        // The logic used to update the results did not check if the move was actually able to be learned in the local game.
        // Double check the results and remove any that are not valid for the local game.
        // SW/SH will continue to iterate downwards to previous groups after HOME is checked, so we can exactly check via Environment.
        for (int i = 0; i < result.Length; i++)
        {
            ref var r = ref result[i];
            if (!r.Valid || r.Generation == 0)
                continue;

            if (r.Info.Environment == local.Environment)
                continue;

            // Check if any evolution in the local context can learn the move via HOME instruction. If none can, the move is invalid.
            var move = current[i];
            if (move == 0)
                continue;

            bool valid = false;
            foreach (var evo in evos)
            {
                var chk = local.GetCanLearnHOME(pk, evo, move, types);
                if (chk.Method != LearnMethod.None)
                    valid = true;
            }
            if (!valid)
                r = default;
        }

        return MoveResult.AllParsed(result);
    }

    /// <summary>
    /// Get the current HOME source for the given context.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    private static IHomeSource GetCurrent(EntityContext context) => context switch
    {
        EntityContext.Gen8 => LearnSource8SWSH.Instance,
        EntityContext.Gen8a => LearnSource8LA.Instance,
        EntityContext.Gen8b => LearnSource8BDSP.Instance,
        EntityContext.Gen9 => LearnSource9SV.Instance,
        _ => throw new ArgumentOutOfRangeException(nameof(context), context, null),
    };

    public void GetAllMoves(Span<bool> result, PKM pk, EvolutionHistory history, IEncounterTemplate enc,
        MoveSourceType types = MoveSourceType.All, LearnOption option = LearnOption.HOME)
    {
        option = LearnOption.HOME;
        var local = GetCurrent(pk.Context);
        var evos = history.Get(pk.Context);

        // Check all adjacent games
        if (history.HasVisitedGen9 && pk is not PK9)
            RentLoopGetAll(LearnGroup9. Instance, result, pk, history, enc, types, option, evos, local);
        if (history.HasVisitedSWSH && pk is not PK8)
            RentLoopGetAll(LearnGroup8. Instance, result, pk, history, enc, types, option, evos, local);
        if (history.HasVisitedPLA && pk is not PA8)
            RentLoopGetAll(LearnGroup8a.Instance, result, pk, history, enc, types, option, evos, local);
        if (history.HasVisitedBDSP && pk is not PB8)
            RentLoopGetAll(LearnGroup8b.Instance, result, pk, history, enc, types, option, evos, local);

        AddSpecialCaseMoves(pk.Species, result);

        // Looking backwards before HOME
        if (history.HasVisitedLGPE)
        {
            RentLoopGetAll(LearnGroup7b.Instance, result, pk, history, enc, types, option, evos, local);
        }
        else if (history.HasVisitedGen7)
        {
            ILearnGroup instance = LearnGroup7.Instance;
            while (true)
            {
                RentLoopGetAll(instance, result, pk, history, enc, types, option, evos, local);
                var prev = instance.GetPrevious(pk, history, enc, option);
                if (prev is null)
                    break;
                instance = prev;
            }
        }
    }

    private static void RentLoopGetAll<T>(T instance, Span<bool> result, PKM pk, EvolutionHistory history,
        IEncounterTemplate enc,
        MoveSourceType types, LearnOption option, ReadOnlySpan<EvoCriteria> evos, IHomeSource local) where T : ILearnGroup
    {
        var length = instance.MaxMoveID;
        var rent = ArrayPool<bool>.Shared.Rent(length);
        var temp = rent.AsSpan(0, length);
        instance.GetAllMoves(temp, pk, history, enc, types, option);
        LoopMerge(result, pk, evos, types, local, temp);
        temp.Clear();
        ArrayPool<bool>.Shared.Return(rent);
    }

    /// <summary>
    /// For each move that is possible to learn in another game, check if it is possible to learn in the current game.
    /// </summary>
    /// <param name="result">Resulting array of moves that are possible to learn in the current game.</param>
    /// <param name="pk">Entity to check.</param>
    /// <param name="evos">Evolutions to check.</param>
    /// <param name="types">Move source types to check.</param>
    /// <param name="dest">Destination game to check.</param>
    /// <param name="temp">Temporary array of moves that are possible to learn in the checked game.</param>
    private static void LoopMerge(Span<bool> result, PKM pk, ReadOnlySpan<EvoCriteria> evos, MoveSourceType types, IHomeSource dest, Span<bool> temp)
    {
        var length = Math.Min(result.Length, temp.Length);
        for (ushort move = 0; move < length; move++)
        {
            if (!temp[move])
                continue; // not possible to learn in other game
            if (result[move])
                continue; // already possible to learn in current game

            foreach (var evo in evos)
            {
                var chk = dest.GetCanLearnHOME(pk, evo, move, types);
                if (chk.Method == LearnMethod.None)
                    continue;
                result[move] = true;
                break;
            }
        }
    }

    private static bool TryAddSpecialCaseMoves(ushort species, Span<MoveResult> result, ReadOnlySpan<ushort> current)
    {
        if (IsPikachuLine(species))
        {
            var index = current.IndexOf((ushort)Move.VoltTackle);
            if (index == -1)
                return false;
            ref var move = ref result[index];
            if (move.Valid)
                return false;
            move = new MoveResult(LearnMethod.Shared, LearnEnvironment.HOME);
            return MoveResult.AllValid(result);
        }
        return false;
    }

    private static void AddSpecialCaseMoves(ushort species, Span<bool> result)
    {
        if (IsPikachuLine(species))
            result[(int)Move.VoltTackle] = true;
    }

    private static bool IsPikachuLine(ushort species) => species is (int)Species.Raichu or (int)Species.Pikachu or (int)Species.Pichu;
}
