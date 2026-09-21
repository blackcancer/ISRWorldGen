using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Climate.Precipitation;

/// <summary>A fraction of a model year, not four simultaneous sources of water.</summary>
public readonly record struct WeightedWind(int X, int Z, double Weight);

public sealed record DistancePrecipitationSettings
{
    public DistancePrecipitationSettings(double oceanEvaporationAt20C, double rainoutLength,
        double orographicRiseScale, double coolingScaleCelsius)
    {
        if (!double.IsFinite(oceanEvaporationAt20C) || oceanEvaporationAt20C < 0d ||
            !Positive(rainoutLength) || !Positive(orographicRiseScale) || !Positive(coolingScaleCelsius))
            throw new ArgumentOutOfRangeException(nameof(rainoutLength), "Transport scales must be finite and positive; evaporation non-negative.");
        OceanEvaporationAt20C = oceanEvaporationAt20C;
        RainoutLength = rainoutLength;
        OrographicRiseScale = orographicRiseScale;
        CoolingScaleCelsius = coolingScaleCelsius;
    }
    public double OceanEvaporationAt20C { get; }
    public double RainoutLength { get; }
    public double OrographicRiseScale { get; }
    public double CoolingScaleCelsius { get; }
    private static bool Positive(double value) => double.IsFinite(value) && value > 0d;
}

public sealed record AtmosphericTransportBalance(double OceanEvaporationVolumePerYear,
    double PrecipitationVolumePerYear, double ExportedVolumePerYear, double ResidualVolumePerYear);

/// <summary>
/// C02 field schema is unchanged. AlgorithmId identifies the new finite-volume
/// solver separately from the compatible IPrecipitationFieldSource schema version.
/// AtmosphericMoisture is outgoing flux / RainoutLength, an equivalent L/Ymod,
/// not the flux itself. Only precipitation is passed to the soil-water budget.
/// </summary>
public sealed class DistancePrecipitationSnapshot : IPrecipitationFieldSource
{
    private readonly PrecipitationSnapshot fields;
    internal DistancePrecipitationSnapshot(IEnumerable<PrecipitationField> values,
        IEnumerable<WeightedWind> winds, AtmosphericTransportBalance balance)
    {
        fields = new PrecipitationSnapshot(values, new WindVector(1, 0), MoistureBoundaryCondition.Closed);
        Winds = Array.AsReadOnly(winds.ToArray());
        Balance = balance;
    }
    public string AlgorithmId => DistancePrecipitationSolver.AlgorithmId;
    public int AlgorithmVersion => PrecipitationSnapshot.AlgorithmVersion;
    public IReadOnlyList<PrecipitationField> Fields => fields.Fields;
    public ReadOnlyCollection<WeightedWind> Winds { get; }
    public AtmosphericTransportBalance Balance { get; }
    public bool TryGetField(long cellId, out PrecipitationField value) => fields.TryGetField(cellId, out value);
}

/// <summary>
/// Conservative annual transport on a complete regular world raster. For each
/// cardinal wind: dF/ds = E - kF, where F is L²/Ymod per unit crosswind length,
/// E is local ocean evaporation in L/Ymod, and k is 1/L. A constant-cell exact
/// solution avoids a rainout percentage tied to grid resolution. World inflow is
/// explicitly zero; exported vapour is counted, never wrapped or reinjected.
/// This is a reduced model, not an atmospheric circulation simulation.
/// </summary>
public static class DistancePrecipitationSolver
{
    public const string AlgorithmId = "distance-precipitation-v1-finite-volume";

    public static DistancePrecipitationSnapshot Solve(IEnumerable<PrecipitationCell> source,
        long cellWidth, long cellLength, IEnumerable<WeightedWind> windSource, DistancePrecipitationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(windSource);
        ArgumentNullException.ThrowIfNull(settings);
        if (cellWidth <= 0 || cellLength <= 0) throw new ArgumentOutOfRangeException(nameof(cellWidth));
        PrecipitationCell[] cells = source.OrderBy(cell => cell?.Id).ToArray()!;
        if (cells.Length == 0 || cells.Any(cell => cell is null) || cells.Select(cell => cell.Id).Distinct().Count() != cells.Length)
            throw new ArgumentException("Nonempty cells with unique IDs are required.", nameof(source));
        if (cells.Select(cell => (cell.GridX, cell.GridZ)).Distinct().Count() != cells.Length)
            throw new ArgumentException("Grid positions must be unique.", nameof(source));
        int minX = cells.Min(cell => cell.GridX), maxX = cells.Max(cell => cell.GridX);
        int minZ = cells.Min(cell => cell.GridZ), maxZ = cells.Max(cell => cell.GridZ);
        decimal expectedCount = ((decimal)maxX - minX + 1) * ((decimal)maxZ - minZ + 1);
        if (expectedCount != cells.Length)
            throw new ArgumentException("A complete rectangular world raster is required; holes are not moisture boundaries.", nameof(source));
        PrecipitationCell anchor = cells[0];
        foreach (PrecipitationCell cell in cells)
        {
            if ((decimal)cell.Position.X - anchor.Position.X != ((decimal)cell.GridX - anchor.GridX) * cellWidth ||
                (decimal)cell.Position.Z - anchor.Position.Z != ((decimal)cell.GridZ - anchor.GridZ) * cellLength)
                throw new ArgumentException("Metric positions do not match the declared cell dimensions.", nameof(source));
            if (cell.Temperature.Sample.SurfaceCelsius is < -100 or > 100)
                throw new ArgumentOutOfRangeException(nameof(source), "Temperature lies outside the qualified reduced-model domain [-100,100] C.");
        }
        WeightedWind[] winds = windSource.OrderBy(wind => wind.X).ThenBy(wind => wind.Z).ToArray();
        if (winds.Length is < 1 or > 4 || winds.Any(wind => Math.Abs((long)wind.X) + Math.Abs((long)wind.Z) != 1 ||
                !double.IsFinite(wind.Weight) || wind.Weight <= 0d) ||
            winds.Select(wind => (wind.X, wind.Z)).Distinct().Count() != winds.Length ||
            Math.Abs(winds.Sum(wind => wind.Weight) - 1d) > 1e-12)
            throw new ArgumentException("Unique cardinal winds must have positive annual weights summing to one.", nameof(windSource));

        var grid = cells.ToDictionary(cell => ((long)cell.GridX, (long)cell.GridZ));
        var precipitation = cells.ToDictionary(cell => cell.Id, _ => 0d);
        var moisture = cells.ToDictionary(cell => cell.Id, _ => 0d);
        double area = (double)cellWidth * cellLength;
        double totalEvaporation = 0d, totalExport = 0d;
        foreach (WeightedWind wind in winds)
        {
            double ds = wind.X != 0 ? cellWidth : cellLength;
            double crossWidth = wind.X != 0 ? cellLength : cellWidth;
            var flux = new Dictionary<long, double>(cells.Length);
            foreach (PrecipitationCell cell in cells.OrderBy(cell => (long)cell.GridX * wind.X + (long)cell.GridZ * wind.Z).ThenBy(cell => cell.Id))
            {
                bool hasUpwind = grid.TryGetValue(((long)cell.GridX - wind.X, (long)cell.GridZ - wind.Z), out PrecipitationCell? upwind);
                double incoming = hasUpwind ? flux[upwind!.Id] : 0d;
                // Temperature-dependent ocean source. Coefficient .06 / C is a
                // declared reduced-model sensitivity, not a calibrated climate law.
                double evaporation = cell.IsOcean
                    ? settings.OceanEvaporationAt20C * Math.Exp(.06d * (cell.Temperature.Sample.SurfaceCelsius - 20d)) : 0d;
                double rise = hasUpwind ? Math.Max(0d, cell.ElevationModelLength - upwind!.ElevationModelLength) : 0d;
                double cooling = hasUpwind ? Math.Max(0d, upwind!.Temperature.Sample.SurfaceCelsius - cell.Temperature.Sample.SurfaceCelsius) : 0d;
                double opticalDepth = ds / settings.RainoutLength + rise / settings.OrographicRiseScale + cooling / settings.CoolingScaleCelsius;
                if (!double.IsFinite(opticalDepth) || opticalDepth <= 0d)
                    throw new ArgumentOutOfRangeException(nameof(settings), "Unrepresentable transport optical depth.");
                double loss, meanTransmission, generatedRain;
                if (opticalDepth < 1e-4)
                {
                    // Stable series for 1-(1-exp(-h))/h; avoids cancellation on
                    // refined grids and preserves zero supply exactly.
                    double h = opticalDepth;
                    generatedRain = h * (.5d + h * (-1d / 6 + h * (1d / 24 - h / 120)));
                    meanTransmission = 1d - generatedRain;
                    loss = h * meanTransmission;
                }
                else
                {
                    loss = 1d - Math.Exp(-opticalDepth);
                    meanTransmission = loss / opticalDepth;
                    generatedRain = 1d - meanTransmission;
                }
                double rain = incoming * loss / ds + evaporation * generatedRain;
                double outgoing = incoming * Math.Exp(-opticalDepth) + evaporation * ds * meanTransmission;
                if (!double.IsFinite(rain) || !double.IsFinite(outgoing) || rain < 0d || outgoing < 0d)
                    throw new OverflowException("Atmospheric transport exceeded its finite model domain.");
                flux.Add(cell.Id, outgoing);
                precipitation[cell.Id] += wind.Weight * rain;
                moisture[cell.Id] += wind.Weight * outgoing / settings.RainoutLength;
                totalEvaporation += wind.Weight * evaporation * area;
                if (!grid.ContainsKey(((long)cell.GridX + wind.X, (long)cell.GridZ + wind.Z)))
                    totalExport += wind.Weight * outgoing * crossWidth;
            }
        }
        double totalRain = cells.Sum(cell => precipitation[cell.Id] * area);
        double residual = totalEvaporation - totalRain - totalExport;
        if (!double.IsFinite(residual) || Math.Abs(residual) > 1e-7 + 1e-10 * totalEvaporation)
            throw new InvalidOperationException("Atmospheric evaporation, precipitation and exported vapour do not conserve water.");
        return new DistancePrecipitationSnapshot(cells.Select(cell => new PrecipitationField(cell.Id, moisture[cell.Id], precipitation[cell.Id])),
            winds, new AtmosphericTransportBalance(totalEvaporation, totalRain, totalExport, residual));
    }
}
