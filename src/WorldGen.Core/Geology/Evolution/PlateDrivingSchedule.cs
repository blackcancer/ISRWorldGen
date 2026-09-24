using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// A declared, smoothly changing basal-driving history, not a reconstruction of
/// Earth's mantle or a change of material ownership. At each mechanical solve
/// these preferred motions are balanced against viscous/plastic resistance and
/// the actual column-pressure gradient. Rotation here changes the imposed load
/// DIRECTION; it does not rigidly rotate an already generated heightmap.
/// </summary>
public sealed class PlateDrivingSchedule
{
    public const string AlgorithmId = "continuous-basal-driving-turns-v1";
    public double DurationMyr { get; }
    public ReadOnlyCollection<double> TurnsRadians { get; }
    public PlateDrivingSchedule(double durationMyr, IReadOnlyList<double> turnsRadians)
    {
        ArgumentNullException.ThrowIfNull(turnsRadians);
        if (!double.IsFinite(durationMyr) || durationMyr <= 0 || durationMyr > 100 ||
            turnsRadians.Count < 2 || turnsRadians.Count > 16 ||
            turnsRadians.Any(x => !double.IsFinite(x) || Math.Abs(x) > Math.PI))
            throw new ArgumentException("Invalid bounded tectonic loading history.");
        DurationMyr = durationMyr;
        TurnsRadians = Array.AsReadOnly(turnsRadians.ToArray());
    }
    public TectonicPlate[] At(double timeMyr, IReadOnlyList<TectonicPlate> initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (!double.IsFinite(timeMyr) || timeMyr < 0 || timeMyr > DurationMyr ||
            initial.Count != TurnsRadians.Count || initial.Where((p,i) => p.Id != i ||
                !double.IsFinite(p.Vx) || !double.IsFinite(p.Vz)).Any())
            throw new ArgumentException("Driving history and mechanical origins disagree.");
        double t = timeMyr / DurationMyr, blend = t * t * (3 - 2 * t);
        var result = initial.ToArray();
        for (int p = 0; p < result.Length; p++)
        {
            double angle = blend * TurnsRadians[p];
            if (angle == 0) continue; // Preserve the exact stationary control.
            double c = Math.Cos(angle), s = Math.Sin(angle);
            var v = initial[p];
            result[p] = v with { Vx = c * v.Vx - s * v.Vz, Vz = s * v.Vx + c * v.Vz };
        }
        return result;
    }
    /// <summary>Explicit synthetic forcing prior with spatially correlated turns.
    /// It is independent of output raster resolution and does not paint relief.
    /// The seed determines the starting phase, not random forces every time step.</summary>
    public static PlateDrivingSchedule SpatialPrior(int seed, TectonicScalePlan scale,
        IReadOnlyList<TectonicPlate> plates, double durationMyr, double maximumTurnRadians = 1.5)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(plates);
        if (!double.IsFinite(maximumTurnRadians) || maximumTurnRadians < 0 || maximumTurnRadians > Math.PI)
            throw new ArgumentOutOfRangeException(nameof(maximumTurnRadians));
        double phase = ((uint)seed / 4294967296d) * Math.Tau;
        return new(durationMyr, plates.Select(p => maximumTurnRadians * Math.Sin(
            Math.Tau * (p.X / scale.ReferenceWidth + .5 * p.Z / scale.ReferenceLength) + phase)).ToArray());
    }
}
