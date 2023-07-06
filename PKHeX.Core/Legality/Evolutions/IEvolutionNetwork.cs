using System;
using System.Collections.Generic;

namespace PKHeX.Core;

public interface IEvolutionNetwork
{
    IEvolutionForward Forward { get; }
    IEvolutionReverse Reverse { get; }
}

/// <summary>
/// Base abstraction for <see cref="EvolutionTree"/>
/// </summary>
public abstract class EvolutionNetwork : IEvolutionNetwork
{
    public IEvolutionForward Forward { get; }
    public IEvolutionReverse Reverse { get; }

    protected EvolutionNetwork(IEvolutionForward forward, IEvolutionReverse reverse)
    {
        Forward = forward;
        Reverse = reverse;
    }

    /// <summary>
    /// Gets all species the <see cref="species"/>-<see cref="form"/> can evolve to &amp; from, yielded in order of increasing evolution stage.
    /// </summary>
    /// <param name="species">Species ID</param>
    /// <param name="form">Form ID</param>
    /// <returns>Enumerable of species IDs (with the Form IDs included, left shifted by 11).</returns>
    public IEnumerable<(ushort Species, byte Form)> GetEvolutionsAndPreEvolutions(ushort species, byte form)
    {
        foreach (var s in Reverse.GetPreEvolutions(species, form))
            yield return s;
        yield return (species, form);
        foreach (var s in Forward.GetEvolutions(species, form))
            yield return s;
    }

    public bool IsSpeciesDerivedFrom(ushort species, byte form, int otherSpecies, int otherForm, bool ignoreForm = true)
    {
        var evos = GetEvolutionsAndPreEvolutions(species, form);
        foreach (var (s, f) in evos)
        {
            if (s != otherSpecies)
                continue;
            if (ignoreForm)
                return true;
            return f == otherForm;
        }
        return false;
    }

    public (ushort Species, byte Form) GetBaseSpeciesForm(ushort species, byte form)
    {
        var chain = Reverse.GetPreEvolutions(species, form);
        foreach (var evo in chain)
            return evo;
        return (species, form);
    }

    public int Devolve(Span<EvoCriteria> result, ushort species, byte form, PKM pk, byte levelMin, byte levelMax, ushort stopSpecies,
        bool skipChecks)
    {
        return Reverse.Devolve(result, species, form, pk, levelMin, levelMax, stopSpecies, skipChecks);
    }
}
