namespace ISRWorldGen.Core.Geology.Landscapes;

/// <summary>
/// Bounded composition of an orogenic province and its subsidiary crest network.
/// Geology determines location; nested morphology only subdivides that support.
/// This is a solid-height construction, with no water, erosion or raster writes.
/// </summary>
internal static class RawMassifComposition
{
    internal const string AlgorithmId = "massif-volume-v1-geology-supported-nested-relief";

    internal static double Sample(int seed, double x, double z, double scale,
        double compression, double inherited, double crests)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) ||
            Math.Abs(x) > 4_000_000_000_000d || Math.Abs(z) > 4_000_000_000_000d ||
            !double.IsFinite(scale) || scale is < .03125 or > 1)
            throw new ArgumentOutOfRangeException(nameof(scale), "Unsupported metric massif coordinates or scale.");
        Unit(compression, nameof(compression)); Unit(inherited, nameof(inherited)); Unit(crests, nameof(crests));
        double support = 1 - (1 - compression) * (1 - inherited);
        if (support == 0 && crests == 0) return 0;
        double macro = RawReliefStructure.Mountain(seed, x, z, 11500 * scale, 61);
        double detail = RawReliefStructure.Mountain(seed, x, z, 3200 * scale, 62);
        return Compose(support, crests, macro, detail);
    }

    internal static double Compose(double geologicalSupport, double crests, double macro, double detail)
    {
        Unit(geologicalSupport, nameof(geologicalSupport)); Unit(crests, nameof(crests));
        Unit(macro, nameof(macro)); Unit(detail, nameof(detail));
        // Fine ruggedness belongs to coarser highland support: it cannot fill a
        // lowland with unrelated mountains. Neither image size nor local raster
        // extrema participate. The broad volume carries most of the altitude;
        // an explicit crest adds at most .22 of the remaining vertical headroom.
        double morphology = macro * (.75 + .25 * detail);
        double volume = .70 * Math.Pow(geologicalSupport, .65) * (.22 + .78 * morphology);
        double crestContribution = .22 * crests * (.75 + .25 * detail);
        double result = 1 - (1 - volume) * (1 - crestContribution);
        // Analytic envelope: volume<=.70 and crestContribution<=.22 -> result<=.766.
        // No output clamp, per-map normalization, or physical erosion is hidden here.
        if (!double.IsFinite(result) || result < 0 || result > .766000000000001)
            throw new InvalidOperationException("Massif composition exceeded its analytic envelope.");
        return result;
    }

    private static void Unit(double value, string name)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, "A dimensionless structural input must be finite in [0,1].");
    }
}
