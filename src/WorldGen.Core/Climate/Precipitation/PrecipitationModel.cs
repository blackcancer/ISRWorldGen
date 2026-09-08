using System.Collections.ObjectModel;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;

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
        double orographicCondensationPerModelLength)
    {
        if (!double.IsFinite(oceanEvaporationModelLengthPerYear) || oceanEvaporationModelLengthPerYear < 0d ||
            !IsUnit(baseCondensationFraction) || !double.IsFinite(orographicCondensationPerModelLength) || orographicCondensationPerModelLength < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(oceanEvaporationModelLengthPerYear),
                "Settings must be finite; fractions are in [0,1] and rates are non-negative.");
        }

        OceanEvaporationModelLengthPerYear = oceanEvaporationModelLengthPerYear;
        BaseCondensationFraction = baseCondensationFraction;
        OrographicCondensationPerModelLength = orographicCondensationPerModelLength;
    }

    public double OceanEvaporationModelLengthPerYear { get; }
    public double BaseCondensationFraction { get; }
    public double OrographicCondensationPerModelLength { get; }

    private static bool IsUnit(double value) => double.IsFinite(value) && value is >= 0d and <= 1d;
}

/// <summary>
/// Immutable input cell. The temperature snapshot stays separate from precipitation;
/// material and water-budget partitioning belong to the L04-C consumer.
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
        TemperatureEvaluation temperature)
    {
        if (!double.IsFinite(elevationModelLength) || temperature.AlgorithmVersion != TemperatureField.AlgorithmVersion ||
            !double.IsFinite(temperature.Sample.SurfaceCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(elevationModelLength),
                "Elevation and the upstream temperature evaluation must be finite and use the supported algorithm version.");
        }

        Id = id; GridX = gridX; GridZ = gridZ; Position = position; ElevationModelLength = elevationModelLength;
        IsOcean = isOcean; Temperature = temperature;
    }

    public long Id { get; }
    public int GridX { get; }
    public int GridZ { get; }
    public WorldBlockPosition Position { get; }
    public double ElevationModelLength { get; }
    public bool IsOcean { get; }
    public TemperatureEvaluation Temperature { get; }
}

/// <summary>Atmospheric humidity and precipitation are distinct annual model-depth fields (L/Ymod).</summary>
public readonly record struct PrecipitationField(
    long CellId,
    double AtmosphericMoistureModelLengthPerYear,
    double PrecipitationModelLengthPerYear);

/// <summary>Read-only hand-off consumed by L04-C before it computes ET, soil, runoff, and recharge.</summary>
public interface IPrecipitationFieldSource
{
    int AlgorithmVersion { get; }
    IReadOnlyList<PrecipitationField> Fields { get; }
    bool TryGetField(long cellId, out PrecipitationField field);
}

/// <summary>
/// Immutable publishable output for L04-C. It deliberately does not claim an ET or
/// storage budget: those transfers remain the following lot's responsibility.
/// </summary>
public sealed class PrecipitationSnapshot : IPrecipitationFieldSource
{
    internal PrecipitationSnapshot(IEnumerable<PrecipitationField> fields, WindVector wind, MoistureBoundaryCondition boundary)
    {
        Fields = Array.AsReadOnly(fields.OrderBy(field => field.CellId).ToArray());
        Wind = wind;
        Boundary = boundary;
    }

    public const int AlgorithmVersion = 1;
    int IPrecipitationFieldSource.AlgorithmVersion => AlgorithmVersion;
    public ReadOnlyCollection<PrecipitationField> Fields { get; }
    IReadOnlyList<PrecipitationField> IPrecipitationFieldSource.Fields => Fields;
    public WindVector Wind { get; }
    public MoistureBoundaryCondition Boundary { get; }

    public bool TryGetField(long cellId, out PrecipitationField field)
    {
        foreach (PrecipitationField candidate in Fields)
        {
            if (candidate.CellId == cellId) { field = candidate; return true; }
        }
        field = default;
        return false;
    }
}

public static class PrecipitationSolver
{
    public static PrecipitationSnapshot Solve(
        IEnumerable<PrecipitationCell> source,
        WindVector wind,
        MoistureBoundaryCondition boundary,
        double globalBoundaryHumidityModelLengthPerYear,
        IEnumerable<long> globalMoisturePortCellIds,
        PrecipitationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(globalMoisturePortCellIds);
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
        long[] suppliedPorts = globalMoisturePortCellIds.ToArray();
        var ports = suppliedPorts.ToHashSet();
        if (ports.Count != suppliedPorts.Length) throw new ArgumentException("Global moisture port IDs must be unique.", nameof(globalMoisturePortCellIds));
        if (boundary == MoistureBoundaryCondition.Closed && ports.Count != 0)
            throw new ArgumentException("Closed boundaries cannot declare global moisture ports.", nameof(globalMoisturePortCellIds));
        if (boundary == MoistureBoundaryCondition.OpenGlobalOcean && ports.Count == 0)
            throw new ArgumentException("An open global ocean requires at least one explicit moisture port.", nameof(globalMoisturePortCellIds));
        int minX = cells.Min(cell => cell.GridX), maxX = cells.Max(cell => cell.GridX);
        int minZ = cells.Min(cell => cell.GridZ), maxZ = cells.Max(cell => cell.GridZ);
        foreach (long portId in ports)
        {
            PrecipitationCell? port = cells.SingleOrDefault(cell => cell.Id == portId);
            if (port is null || !port.IsOcean ||
                TryGetUpwind(grid, port, wind, out _) ||
                (wind.X > 0 && port.GridX != minX) || (wind.X < 0 && port.GridX != maxX) ||
                (wind.Z > 0 && port.GridZ != minZ) || (wind.Z < 0 && port.GridZ != maxZ))
            {
                throw new ArgumentException("Global moisture ports must be declared ocean cells on the windward world boundary.", nameof(globalMoisturePortCellIds));
            }
        }
        var remaining = new Dictionary<long, double>();
        var fields = new List<PrecipitationField>(cells.Length);
        foreach (PrecipitationCell cell in cells.OrderBy(cell => (long)cell.GridX * wind.X + (long)cell.GridZ * wind.Z).ThenBy(cell => cell.Id))
        {
            bool hasUpwind = TryGetUpwind(grid, cell, wind, out PrecipitationCell? upwind);
            double carried = hasUpwind ? remaining[upwind!.Id] : ports.Contains(cell.Id) ? globalBoundaryHumidityModelLengthPerYear : 0d;
            double supplied = carried + (cell.IsOcean ? settings.OceanEvaporationModelLengthPerYear : 0d);
            double rise = hasUpwind ? Math.Max(0d, cell.ElevationModelLength - upwind!.ElevationModelLength) : 0d;
            double condensationFraction = Math.Min(1d, settings.BaseCondensationFraction + (rise * settings.OrographicCondensationPerModelLength));
            double precipitation = supplied * condensationFraction;
            double atmospheric = supplied - precipitation;
            if (!double.IsFinite(atmospheric) || !double.IsFinite(precipitation))
                throw new OverflowException("Precipitation calculation exceeded the supported finite model domain.");
            remaining.Add(cell.Id, atmospheric);
            fields.Add(new PrecipitationField(cell.Id, atmospheric, precipitation));
        }

        return new PrecipitationSnapshot(fields, wind, boundary);
    }

    private static bool TryGetUpwind(
        IReadOnlyDictionary<(int X, int Z), PrecipitationCell> grid,
        PrecipitationCell cell,
        WindVector wind,
        out PrecipitationCell? upwind)
    {
        if ((wind.X > 0 && cell.GridX == int.MinValue) || (wind.X < 0 && cell.GridX == int.MaxValue) ||
            (wind.Z > 0 && cell.GridZ == int.MinValue) || (wind.Z < 0 && cell.GridZ == int.MaxValue))
        {
            upwind = null;
            return false;
        }

        return grid.TryGetValue((cell.GridX - wind.X, cell.GridZ - wind.Z), out upwind);
    }
}
