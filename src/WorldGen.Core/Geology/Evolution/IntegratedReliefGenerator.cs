using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>One explicit entry point from a seed and full-world dimensions to
/// signed solid elevations. Never changes the native saved-world generator.
/// The optional evolving load is a prior, not a solved mantle history.</summary>
public static class IntegratedReliefGenerator
{
    public const string AlgorithmId = "integrated-full-atlas-pressure-cooling-driving-v1";

    public static IntegratedReliefResult Generate(int seed, long widthBlocks = 1_000_000,
        long lengthBlocks = 1_000_000, int side = 512, double durationMyr = 96,
        bool evolvingDriving = true, double maximumTurnRadians = 1.5)
    {
        var scale = new TectonicScalePlan(widthBlocks, lengthBlocks);
        var settings = new TectonicEvolutionSettings(side: side, duration: durationMyr);
        if (!double.IsFinite(maximumTurnRadians) || maximumTurnRadians < 0 || maximumTurnRadians > Math.PI)
            throw new ArgumentOutOfRangeException(nameof(maximumTurnRadians));
        // The initial material volume is paired with the existing atlas. The
        // same initial geometry is used by both stationary/evolving controls.
        var initial = TectonicHistory.Generate(seed, scale, new(side: side, duration: 0));
        var assemblage = ContinentalAssemblage.Generate(seed, scale, side,
            CrustTransport.Sum(initial.Initial.ContinentalKm) / (side * side));
        PlateDrivingSchedule? driving = evolvingDriving && durationMyr > 0
            ? PlateDrivingSchedule.SpatialPrior(seed, scale, initial.Plates, durationMyr, maximumTurnRadians) : null;
        var world = LithostaticReliefWorld.Generate(seed, scale, settings, assemblage,
            new LithostaticReliefOptions(), Math.Min(128, side), driving);
        var samples = world.MechanicalHistory.Solves.Select(s => new DrivingSample(s.Time,
            Array.AsReadOnly(driving?.At(s.Time, initial.Plates) ?? initial.Plates.ToArray()))).ToArray();
        return new(world, scale, settings, driving, Array.AsReadOnly(samples));
    }
}

public sealed record DrivingSample(double TimeMyr, ReadOnlyCollection<TectonicPlate> PreferredMotions);
public sealed record IntegratedReliefResult(LithostaticReliefWorld World, TectonicScalePlan Scale,
    TectonicEvolutionSettings Settings, PlateDrivingSchedule? Driving, ReadOnlyCollection<DrivingSample> LoadHistory);
