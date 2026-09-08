using ISRWorldGen.Core.Foundation;

namespace ISRWorldGen.Core.Climate.Temperature;

/// <summary>
/// A horizontal latitude axis expressed in model coordinates. Its scale is explicitly model-length units per degree.
/// </summary>
public sealed class LatitudeAxis
{
    public LatitudeAxis(WorldBlockPosition equatorOrigin, double axisX, double axisZ, double modelLengthPerDegree)
    {
        if (!double.IsFinite(axisX) || !double.IsFinite(axisZ) ||
            (!double.IsFinite(modelLengthPerDegree)) || modelLengthPerDegree <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(modelLengthPerDegree),
                "Latitude axis components and model-length-per-degree must be finite; the scale must be positive.");
        }

        // Scale before squaring so finite subnormal and very large inputs do not
        // underflow to zero or overflow during normalization.
        double scale = Math.Max(Math.Abs(axisX), Math.Abs(axisZ));
        if (scale == 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(axisX), "Latitude axis must have non-zero finite length.");
        }

        double scaledX = axisX / scale;
        double scaledZ = axisZ / scale;
        double normalizedMagnitude = Math.Sqrt((scaledX * scaledX) + (scaledZ * scaledZ));

        EquatorOrigin = equatorOrigin;
        AxisX = scaledX / normalizedMagnitude;
        AxisZ = scaledZ / normalizedMagnitude;
        ModelLengthPerDegree = modelLengthPerDegree;
    }

    public WorldBlockPosition EquatorOrigin { get; }

    public double AxisX { get; }

    public double AxisZ { get; }

    public double ModelLengthPerDegree { get; }

    public bool IsNorthSouth => AxisX == 0d && Math.Abs(AxisZ) == 1d;

    public double GetLatitudeDegrees(WorldBlockPosition position)
    {
        double deltaX = position.X - (double)EquatorOrigin.X;
        double deltaZ = position.Z - (double)EquatorOrigin.Z;
        double latitude = ((deltaX * AxisX) + (deltaZ * AxisZ)) / ModelLengthPerDegree;
        if (!double.IsFinite(latitude) || latitude is < -90d or > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position,
                "Position yields a latitude outside the qualified [-90, 90] degree model domain.");
        }

        return latitude;
    }
}

/// <summary>All temperatures are degrees Celsius model; altitude is model length relative to declared sea level.</summary>
public sealed class TemperatureSettings
{
    public TemperatureSettings(
        double equatorialReferenceCelsius,
        double polewardCoolingCelsiusPerDegree,
        double altitudeLapseCelsiusPerModelLength,
        double inlandReferenceOffsetCelsius)
    {
        if (!double.IsFinite(equatorialReferenceCelsius) || !double.IsFinite(polewardCoolingCelsiusPerDegree) ||
            !double.IsFinite(altitudeLapseCelsiusPerModelLength) || !double.IsFinite(inlandReferenceOffsetCelsius) ||
            polewardCoolingCelsiusPerDegree < 0d || altitudeLapseCelsiusPerModelLength < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(equatorialReferenceCelsius),
                "Temperature settings must be finite; cooling and lapse rates cannot be negative.");
        }

        EquatorialReferenceCelsius = equatorialReferenceCelsius;
        PolewardCoolingCelsiusPerDegree = polewardCoolingCelsiusPerDegree;
        AltitudeLapseCelsiusPerModelLength = altitudeLapseCelsiusPerModelLength;
        InlandReferenceOffsetCelsius = inlandReferenceOffsetCelsius;
    }

    public double EquatorialReferenceCelsius { get; }
    public double PolewardCoolingCelsiusPerDegree { get; }
    public double AltitudeLapseCelsiusPerModelLength { get; }
    public double InlandReferenceOffsetCelsius { get; }
}

public readonly record struct TemperatureInput(
    WorldBlockPosition Position,
    double AltitudeAboveSeaModelLength,
    double ContinentalityNormalized);

/// <summary>Reference and surface values remain distinct so an adapter cannot apply altitude twice.</summary>
public readonly record struct TemperatureSample(
    double LatitudeDegrees,
    double ReferenceCelsius,
    double SurfaceCelsius,
    double AltitudeCorrectionCelsius);

/// <summary>
/// Immutable, self-describing Core result for future precipitation, water-budget, and native-map consumers.
/// It carries the exact coordinate and frozen model parameters that produced the temperatures.
/// </summary>
public readonly record struct TemperatureEvaluation(
    int AlgorithmVersion,
    LatitudeAxis LatitudeAxis,
    TemperatureSettings Settings,
    TemperatureInput Input,
    TemperatureSample Sample);

public enum NativeAltitudeCorrection
{
    EngineAppliesAltitude = 0,
    CoreAppliesAltitude = 1
}

public readonly record struct NativeTemperatureExport(double Celsius, NativeAltitudeCorrection AltitudeCorrection);

public static class TemperatureField
{
    /// <summary>Increment only through an explicit integration/migration decision.</summary>
    public const int AlgorithmVersion = 1;

    public static TemperatureSample Calculate(LatitudeAxis latitudeAxis, TemperatureSettings settings, TemperatureInput input)
        => Evaluate(latitudeAxis, settings, input).Sample;

    public static TemperatureEvaluation Evaluate(LatitudeAxis latitudeAxis, TemperatureSettings settings, TemperatureInput input)
    {
        ArgumentNullException.ThrowIfNull(latitudeAxis);
        ArgumentNullException.ThrowIfNull(settings);
        if (!double.IsFinite(input.AltitudeAboveSeaModelLength) ||
            !double.IsFinite(input.ContinentalityNormalized) || input.ContinentalityNormalized is < 0d or > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(input),
                "Altitude must be finite and continentality must be finite in [0,1].");
        }

        double latitude = latitudeAxis.GetLatitudeDegrees(input.Position);
        double reference = settings.EquatorialReferenceCelsius -
            (Math.Abs(latitude) * settings.PolewardCoolingCelsiusPerDegree) +
            (input.ContinentalityNormalized * settings.InlandReferenceOffsetCelsius);
        double altitudeCorrection = input.AltitudeAboveSeaModelLength * settings.AltitudeLapseCelsiusPerModelLength;
        double surface = reference - altitudeCorrection;
        if (!double.IsFinite(reference) || !double.IsFinite(surface))
        {
            throw new OverflowException("Temperature calculation exceeded the finite Celsius model domain.");
        }

        var sample = new TemperatureSample(latitude, reference, surface, altitudeCorrection);
        return new TemperatureEvaluation(AlgorithmVersion, latitudeAxis, settings, input, sample);
    }

    public static NativeTemperatureExport ExportToNative(TemperatureSample sample, NativeAltitudeCorrection correction)
    {
        if (!double.IsFinite(sample.ReferenceCelsius) || !double.IsFinite(sample.SurfaceCelsius) ||
            !double.IsFinite(sample.AltitudeCorrectionCelsius) || !double.IsFinite(sample.LatitudeDegrees) ||
            sample.LatitudeDegrees is < -90d or > 90d ||
            sample.SurfaceCelsius != sample.ReferenceCelsius - sample.AltitudeCorrectionCelsius)
        {
            throw new ArgumentOutOfRangeException(nameof(sample),
                "Temperature sample must be finite, qualified and contain one explicit altitude correction.");
        }

        return correction switch
        {
            NativeAltitudeCorrection.EngineAppliesAltitude => new NativeTemperatureExport(sample.ReferenceCelsius, correction),
            NativeAltitudeCorrection.CoreAppliesAltitude => new NativeTemperatureExport(sample.SurfaceCelsius, correction),
            _ => throw new ArgumentOutOfRangeException(nameof(correction), correction,
                "Native altitude correction must be an explicitly supported mode.")
        };
    }

    public static void EnsureNativeSeasonSupport(LatitudeAxis latitudeAxis)
    {
        ArgumentNullException.ThrowIfNull(latitudeAxis);
        if (!latitudeAxis.IsNorthSouth)
        {
            throw new NotSupportedException(
                "The native seasonal integration supports only a North-South latitude axis; oblique axes remain Core-only.");
        }
    }
}
