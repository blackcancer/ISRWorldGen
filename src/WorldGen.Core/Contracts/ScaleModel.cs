namespace ISRWorldGen.Core.Contracts;

/// <summary>
/// Explicit conversion model between named geological units, named altitude-model units, and blocks.
/// Factors are immutable, finite and strictly positive. Integral conversion uses midpoint-to-even rounding.
/// </summary>
public sealed record ScaleModel
{
    public ScaleModel(
        string geologicalUnitName,
        double blocksPerGeologicalUnit,
        string altitudeUnitName,
        double blocksPerAltitudeUnit)
    {
        GeologicalUnitName = CanonicalText.Require(geologicalUnitName, nameof(geologicalUnitName));
        AltitudeUnitName = CanonicalText.Require(altitudeUnitName, nameof(altitudeUnitName));
        BlocksPerGeologicalUnit = RequirePositiveFinite(
            blocksPerGeologicalUnit,
            nameof(blocksPerGeologicalUnit));
        BlocksPerAltitudeUnit = RequirePositiveFinite(
            blocksPerAltitudeUnit,
            nameof(blocksPerAltitudeUnit));
    }

    public string GeologicalUnitName { get; }

    public double BlocksPerGeologicalUnit { get; }

    public string AltitudeUnitName { get; }

    public double BlocksPerAltitudeUnit { get; }

    public double GeologicalUnitsToBlocks(double geologicalUnits) =>
        MultiplyFinite(geologicalUnits, BlocksPerGeologicalUnit, nameof(geologicalUnits));

    public double BlocksToGeologicalUnits(double blocks) =>
        DivideFinite(blocks, BlocksPerGeologicalUnit, nameof(blocks));

    public double AltitudeUnitsToBlocks(double altitudeUnits) =>
        MultiplyFinite(altitudeUnits, BlocksPerAltitudeUnit, nameof(altitudeUnits));

    public double BlocksToAltitudeUnits(double blocks) =>
        DivideFinite(blocks, BlocksPerAltitudeUnit, nameof(blocks));

    public long GeologicalUnitsToWholeBlocksChecked(double geologicalUnits) =>
        checked((long)Math.Round(
            GeologicalUnitsToBlocks(geologicalUnits),
            MidpointRounding.ToEven));

    private static double RequirePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Scale factors must be finite and positive.");
        }

        return value;
    }

    private static double MultiplyFinite(double value, double factor, string parameterName)
    {
        RequireFiniteInput(value, parameterName);
        double result = value * factor;
        return double.IsFinite(result)
            ? result
            : throw new OverflowException("The converted block value is not finite.");
    }

    private static double DivideFinite(double value, double factor, string parameterName)
    {
        RequireFiniteInput(value, parameterName);
        double result = value / factor;
        return double.IsFinite(result)
            ? result
            : throw new OverflowException("The converted model-unit value is not finite.");
    }

    private static void RequireFiniteInput(double value, string parameterName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Conversion input must be finite.");
        }
    }
}
