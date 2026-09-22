namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Explicit candidate entrypoint. No default world is replaced.</summary>
public static class ViscoplasticWorld
{
    public static StrainWeakeningResult Generate(int seed, TectonicScalePlan scale,
        TectonicEvolutionSettings settings, ContinentalAssemblage assemblage, StrainWeakeningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Yield is null) throw new ArgumentException("Yield model or its matched disabled control is required.");
        return MaterialBoundHistory.GenerateWithWeakening(seed, scale, settings, assemblage, options);
    }
}
