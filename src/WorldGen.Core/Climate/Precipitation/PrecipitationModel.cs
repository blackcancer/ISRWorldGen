using System.Collections.ObjectModel;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Core.Climate.Precipitation;

/// <summary>One of the four supported, deterministic grid wind directions.</summary>
public readonly record struct WindVector
{
    public WindVector(int x, int z)
    {
        if ((x == 0 && z == 0) || x is < -1 or > 1 || z is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(x), "Wind components must be -1, 0, or 1 and cannot both be zero.");
        X = x;
        Z = z;
    }

    public int X { get; }
    public int Z { get; }
}

/// <summary>
/// Explicit global boundary condition. Closed means a technical raster edge is never
/// a moisture source; open injection is reserved for a declared world boundary.
/// </summary>
public enum MoistureBoundaryCondition { Closed = 0, OpenGlobalOcean = 1 }

/// <summary>Frozen units for the bounded, annual-model-depth precipitation pass.</summary>
public sealed class PrecipitationSettings
{
    public PrecipitationSettings(
        double oceanEvaporationModelLengthPerYear,
        double baseCondensationFraction,
        double orographicCondensationPerModelLength,
        double rechargeFractionOfInfiltration,
        double soilMoistureLengthAtSaturation)
    {
        if (!double.IsFinite(oceanEvaporationModelLengthPerYear) || oceanEvaporationModelLengthPerYear < 0d ||
            !IsUnit(baseCondensationFraction) || !double.IsFinite(orographicCondensationPerModelLength) || orographicCondensationPerModelLength < 0d ||
            !IsUnit(rechargeFractionOfInfiltration) || !double.IsFinite(soilMoistureLengthAtSaturation) || soilMoistureLengthAtSaturation <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(oceanEvaporationModelLengthPerYear),
                "Settings must be finite; fractions are in [0,1], rates are non-negative, and soil saturation depth is positive.");
        }

        OceanEvaporationModelLengthPerYear = oceanEvaporationModelLengthPerYear;
        BaseCondensationFraction = baseCondensationFraction;
        OrographicCondensationPerModelLength = orographicCondensationPerModelLength;
        RechargeFractionOfInfiltration = rechargeFractionOfInfiltration;
        SoilMoistureLengthAtSaturation = soilMoistureLengthAtSaturation;
    }

    public double OceanEvaporationModelLengthPerYear { get; }
    public double BaseCondensationFraction { get; }
    public double OrographicCondensationPerModelLength { get; }
    public double RechargeFractionOfInfiltration { get; }
    public double SoilMoistureLengthAtSaturation { get; }

    private static bool IsUnit(double value) => double.IsFinite(value) && value is >= 0d and <= 1d;
}

/// <summary>
/// Immutable input cell. Temperature and material are retained as distinct upstream
/// snapshots: this pass consumes neither an implicit altitude correction nor game assets.
/// </summary>
public sealed record PrecipitationCell
{
    public PrecipitationCell(
        long id,
        int gridX,
        int gridZ,
        WorldBlockPosition position,
        double elevationModelLength,
        bool isOcean,
        TemperatureEvaluation temperature,
        MaterialSample material)
    {
        if (!double.IsFinite(elevationModelLength) || temperature.AlgorithmVersion != TemperatureField.AlgorithmVersion ||
            !double.IsFinite(temperature.Sample.SurfaceCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(elevationModelLength),
                "Elevation and the upstream temperature evaluation must be finite and use the supported algorithm version.");
        }

        Id = id; GridX = gridX; GridZ = gridZ; Position = position; ElevationModelLength = elevationModelLength;
        IsOcean = isOcean; Temperature = temperature; Material = material;
    }

    public long Id { get; }
    public int GridX { get; }
    public int GridZ { get; }
    public WorldBlockPosition Position { get; }
    public double ElevationModelLength { get; }
    public bool IsOcean { get; }
    public TemperatureEvaluation Temperature { get; }
    public MaterialSample Material { get; }
}

/// <summary>Separate precipitation and land-water fields; all rates are L/Ymod except normalized soil moisture.</summary>
public readonly record struct PrecipitationField(
    long CellId,
    double AtmosphericMoistureModelLengthPerYear,
    double PrecipitationModelLengthPerYear,
    double SoilMoistureNormalized,
    double SurfaceRunoffModelLengthPerYear,
    double GroundwaterRechargeModelLengthPerYear);

/// <summary>
/// Immutable publishable output for L04-C. It deliberately does not claim an ET or
/// storage budget: those transfers remain the following lot's responsibility.
/// </summary>
public sealed class PrecipitationSnapshot
{
    internal PrecipitationSnapshot(IEnumerable<PrecipitationField> fields, WindVector wind, MoistureBoundaryCondition boundary)
    {
        Fields = Array.AsReadOnly(fields.OrderBy(field => field.CellId).ToArray());
        Wind = wind;
        Boundary = boundary;
    }

    public const int AlgorithmVersion = 1;
    public ReadOnlyCollection<PrecipitationField> Fields { get; }
    public WindVector Wind { get; }
    public MoistureBoundaryCondition Boundary { get; }
}

public static class PrecipitationSolver
{
    public static PrecipitationSnapshot Solve(
        IEnumerable<PrecipitationCell> source,
        WindVector wind,
        MoistureBoundaryCondition boundary,
        double globalBoundaryHumidityModelLengthPerYear,
        PrecipitationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);
        if ((wind.X == 0 && wind.Z == 0) || wind.X is < -1 or > 1 || wind.Z is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(wind), "Wind must be an explicitly supported non-zero grid direction.");
        if (!Enum.IsDefined(boundary) || !double.IsFinite(globalBoundaryHumidityModelLengthPerYear) || globalBoundaryHumidityModelLengthPerYear < 0d)
            throw new ArgumentOutOfRangeException(nameof(globalBoundaryHumidityModelLengthPerYear));
        if (boundary == MoistureBoundaryCondition.Closed && globalBoundaryHumidityModelLengthPerYear != 0d)
            throw new ArgumentException("A closed technical boundary cannot inject global humidity.", nameof(globalBoundaryHumidityModelLengthPerYear));

        PrecipitationCell[] cells = source.ToArray();
        if (cells.Length == 0 || cells.Any(cell => cell is null)) throw new ArgumentException("At least one non-null cell is required.", nameof(source));
        if (cells.Select(cell => cell.Id).Distinct().Count() != cells.Length || cells.Select(cell => (cell.GridX, cell.GridZ)).Distinct().Count() != cells.Length)
            throw new ArgumentException("Cell IDs and grid coordinates must both be unique.", nameof(source));

        Dictionary<(int X, int Z), PrecipitationCell> grid = cells.ToDictionary(cell => (cell.GridX, cell.GridZ));
        var remaining = new Dictionary<long, double>();
        var fields = new List<PrecipitationField>(cells.Length);
        foreach (PrecipitationCell cell in cells.OrderBy(cell => (long)cell.GridX * wind.X + (long)cell.GridZ * wind.Z).ThenBy(cell => cell.Id))
        {
            bool hasUpwind = grid.TryGetValue((cell.GridX - wind.X, cell.GridZ - wind.Z), out PrecipitationCell? upwind);
            double carried = hasUpwind ? remaining[upwind!.Id] : boundary == MoistureBoundaryCondition.OpenGlobalOcean ? globalBoundaryHumidityModelLengthPerYear : 0d;
            double supplied = carried + (cell.IsOcean ? settings.OceanEvaporationModelLengthPerYear : 0d);
            double rise = hasUpwind ? Math.Max(0d, cell.ElevationModelLength - upwind!.ElevationModelLength) : 0d;
            double condensationFraction = Math.Min(1d, settings.BaseCondensationFraction + (rise * settings.OrographicCondensationPerModelLength));
            double precipitation = supplied * condensationFraction;
            double atmospheric = supplied - precipitation;
            double permeability = cell.Material.Properties.PermeabilityNormalized;
            double recharge = precipitation * permeability * settings.RechargeFractionOfInfiltration;
            double runoff = precipitation - recharge;
            double soil = Math.Clamp((precipitation / settings.SoilMoistureLengthAtSaturation) * (0.25d + (0.75d * permeability)), 0d, 1d);
            if (!double.IsFinite(atmospheric) || !double.IsFinite(precipitation) || !double.IsFinite(recharge) || !double.IsFinite(runoff) || !double.IsFinite(soil))
                throw new OverflowException("Precipitation calculation exceeded the supported finite model domain.");
            remaining.Add(cell.Id, atmospheric);
            fields.Add(new PrecipitationField(cell.Id, atmospheric, precipitation, soil, runoff, recharge));
        }

        return new PrecipitationSnapshot(fields, wind, boundary);
    }
}
