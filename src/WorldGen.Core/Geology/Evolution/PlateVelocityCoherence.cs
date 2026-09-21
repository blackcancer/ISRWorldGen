namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Finite-width coupling of imposed plate velocities. Three separable metric
/// box averages approximate a Gaussian of the declared length (its variance
/// per axis is length squared before cell discretization). This is a reduced
/// nonlocal kinematic closure, NOT a force-balanced lithosphere calculation.
/// Only velocity is averaged: material quantities and heights are never filtered.
/// Positive normalized weights preserve uniform motion and velocity bounds.
/// </summary>
public static class PlateVelocityCoherence
{
    public static double[] Apply(IReadOnlyList<double> source, int side, double dx, double dz, double length)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (side is < 2 or > 512 || source.Count != side * side ||
            !double.IsFinite(dx) || !double.IsFinite(dz) || dx <= 0 || dz <= 0 ||
            !double.IsFinite(length) || length < 0 || length > Math.Min(dx, dz) * side ||
            source.Any(v => !double.IsFinite(v))) throw new ArgumentException("Invalid metric velocity coupling.");
        double[] values = source.ToArray();
        for (int pass = 0; pass < 3; pass++)
        {
            values = Average(values, side, length / dx, false);
            values = Average(values, side, length / dz, true);
        }
        return values;
    }

    private static double[] Average(double[] values, int n, double halfWidthCells, bool vertical)
    {
        if (halfWidthCells <= .5) return values;
        int radius = (int)Math.Floor(halfWidthCells - .5);
        double edge = halfWidthCells - radius - .5, totalWeight = 2 * halfWidthCells;
        var output = new double[values.Length];
        for (int line = 0; line < n; line++)
        {
            double sum = 0;
            for (int k = -radius; k <= radius; k++) sum += values[Index(k)];
            for (int pos = 0; pos < n; pos++)
            {
                output[Index(pos)] = (sum + edge * (values[Index(pos - radius - 1)] + values[Index(pos + radius + 1)])) / totalWeight;
                sum += values[Index(pos + radius + 1)] - values[Index(pos - radius)];
            }
            int Index(int position)
            {
                int p = (position % n + n) % n;
                return vertical ? p * n + line : line * n + p;
            }
        }
        return output;
    }
}
