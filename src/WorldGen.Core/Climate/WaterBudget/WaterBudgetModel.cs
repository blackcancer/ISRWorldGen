using System.Collections.ObjectModel;
using ISRWorldGen.Core.Climate.Precipitation;
using ISRWorldGen.Core.Climate.Temperature;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Geology.Materials;

namespace ISRWorldGen.Core.Climate.WaterBudget;

/// <summary>Frozen annual model-depth settings for the local water partition.</summary>
public sealed class WaterBudgetSettings
{
    public WaterBudgetSettings(double potentialEvapotranspirationFraction, double soilRetentionFraction, double soilStorageCapacityModelLength)
    {
        if (!IsUnit(potentialEvapotranspirationFraction) || !IsUnit(soilRetentionFraction) ||
            !double.IsFinite(soilStorageCapacityModelLength) || soilStorageCapacityModelLength <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(potentialEvapotranspirationFraction),
                "Fractions must be finite in [0,1] and soil capacity must be positive finite model length.");
        }

        PotentialEvapotranspirationFraction = potentialEvapotranspirationFraction;
        SoilRetentionFraction = soilRetentionFraction;
        SoilStorageCapacityModelLength = soilStorageCapacityModelLength;
    }

    public double PotentialEvapotranspirationFraction { get; }
    public double SoilRetentionFraction { get; }
    public double SoilStorageCapacityModelLength { get; }

    private static bool IsUnit(double value) => double.IsFinite(value) && value is >= 0d and <= 1d;
}

/// <summary>Immutable inputs owned by this partitioning pass; precipitation and temperature remain upstream snapshots.</summary>
public readonly record struct WaterBudgetInput(
    long CellId,
    StableId ReservoirId,
    double AreaModelSquareLength,
    double InitialSoilMoistureNormalized,
    double PotentialEvapotranspirationModelLengthPerYear,
    MaterialProperties Material,
    TemperatureEvaluation Temperature);

/// <summary>
/// Annual local balance in L/Ymod (except area in L² and normalized soil moisture).
/// Groundwater emergence is deliberately absent: it is a separate reservoir transfer,
/// never new precipitation in this equation.
/// </summary>
public readonly record struct WaterBudgetCell(
    long CellId,
    StableId ReservoirId,
    double PrecipitationModelLengthPerYear,
    double ActualEvapotranspirationModelLengthPerYear,
    double RunoffModelLengthPerYear,
    double RechargeModelLengthPerYear,
    double StorageChangeModelLengthPerYear,
    double SoilMoistureNormalized,
    double AreaModelSquareLength)
{
    public double ResidualModelLengthPerYear => PrecipitationModelLengthPerYear - ActualEvapotranspirationModelLengthPerYear -
        RunoffModelLengthPerYear - RechargeModelLengthPerYear - StorageChangeModelLengthPerYear;

    public double ReferenceFlowModelLengthPerYear => Math.Max(PrecipitationModelLengthPerYear,
        Math.Max(ActualEvapotranspirationModelLengthPerYear + RunoffModelLengthPerYear + RechargeModelLengthPerYear,
            Math.Abs(StorageChangeModelLengthPerYear)));

    public double RechargeModelVolumePerYear => RechargeModelLengthPerYear * AreaModelSquareLength;
}

public enum GroundwaterTransferKind : byte { Loss = 0, Resurgence = 1 }

/// <summary>Reservoir-to-reservoir volume transfer. Its stable ID is shared by hydrology and caverns.</summary>
public readonly record struct GroundwaterTransfer(
    StableId TransferId,
    StableId SourceReservoirId,
    StableId DestinationReservoirId,
    double FlowModelVolumePerYear,
    GroundwaterTransferKind Kind);

/// <summary>Immutable, ordered hand-off for hydrology/caverns. No consumer is implemented by this lot.</summary>
public interface IWaterBudgetSnapshotSource
{
    int AlgorithmVersion { get; }
    int Iteration { get; }
    IReadOnlyList<WaterBudgetCell> Cells { get; }
    IReadOnlyList<GroundwaterTransfer> Transfers { get; }
}

public sealed class WaterBudgetSnapshot : IWaterBudgetSnapshotSource
{
    internal WaterBudgetSnapshot(int iteration, IEnumerable<WaterBudgetCell> cells, IEnumerable<GroundwaterTransfer> transfers)
    {
        Iteration = iteration;
        Cells = Array.AsReadOnly(cells.OrderBy(cell => cell.CellId).ToArray());
        Transfers = Array.AsReadOnly(transfers.OrderBy(transfer => transfer.TransferId.High).ThenBy(transfer => transfer.TransferId.Low).ToArray());
    }

    public const int AlgorithmVersion = 1;
    int IWaterBudgetSnapshotSource.AlgorithmVersion => AlgorithmVersion;
    public int Iteration { get; }
    public ReadOnlyCollection<WaterBudgetCell> Cells { get; }
    IReadOnlyList<WaterBudgetCell> IWaterBudgetSnapshotSource.Cells => Cells;
    public ReadOnlyCollection<GroundwaterTransfer> Transfers { get; }
    IReadOnlyList<GroundwaterTransfer> IWaterBudgetSnapshotSource.Transfers => Transfers;
}

public static class WaterBudgetSolver
{
    public const double AbsoluteResidualToleranceModelLengthPerYear = 1e-9d;
    public const double RelativeResidualTolerance = 1e-6d;

    public static WaterBudgetSnapshot Solve(
        IEnumerable<WaterBudgetInput> source,
        IPrecipitationFieldSource precipitation,
        WaterBudgetSettings settings,
        IEnumerable<GroundwaterTransfer> transfers,
        int iteration)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(precipitation);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(transfers);
        if (iteration < 0 || precipitation.AlgorithmVersion != PrecipitationSnapshot.AlgorithmVersion)
            throw new ArgumentOutOfRangeException(nameof(iteration), "Iterations must be non-negative and precipitation must use the supported algorithm.");

        WaterBudgetInput[] inputs = source.ToArray();
        if (inputs.Length == 0 || inputs.Select(input => input.CellId).Distinct().Count() != inputs.Length ||
            inputs.Select(input => input.ReservoirId).Distinct().Count() != inputs.Length)
            throw new ArgumentException("Water-budget cell and reservoir IDs must be unique and non-empty.", nameof(source));

        var cells = new List<WaterBudgetCell>(inputs.Length);
        foreach (WaterBudgetInput input in inputs.OrderBy(value => value.CellId))
        {
            ValidateInput(input);
            if (!precipitation.TryGetField(input.CellId, out PrecipitationField field) ||
                !double.IsFinite(field.PrecipitationModelLengthPerYear) || field.PrecipitationModelLengthPerYear < 0d)
                throw new ArgumentException("Every budget input requires one finite non-negative upstream precipitation field.", nameof(precipitation));

            double precipitationDepth = field.PrecipitationModelLengthPerYear;
            double actualEt = Math.Min(precipitationDepth, input.PotentialEvapotranspirationModelLengthPerYear * settings.PotentialEvapotranspirationFraction);
            double afterEt = precipitationDepth - actualEt;
            double desiredStorage = afterEt * settings.SoilRetentionFraction;
            double availableStorage = (1d - input.InitialSoilMoistureNormalized) * settings.SoilStorageCapacityModelLength;
            double storageChange = Math.Min(desiredStorage, availableStorage);
            double remaining = afterEt - storageChange;
            double recharge = remaining * input.Material.PermeabilityNormalized;
            double runoff = remaining - recharge;
            double soil = input.InitialSoilMoistureNormalized + (storageChange / settings.SoilStorageCapacityModelLength);
            var cell = new WaterBudgetCell(input.CellId, input.ReservoirId, precipitationDepth, actualEt, runoff, recharge, storageChange, soil, input.AreaModelSquareLength);
            EnsureBalanced(cell);
            cells.Add(cell);
        }

        GroundwaterTransfer[] orderedTransfers = ValidateTransfers(transfers, cells);
        return new WaterBudgetSnapshot(iteration, cells, orderedTransfers);
    }

    private static void ValidateInput(WaterBudgetInput input)
    {
        if (!double.IsFinite(input.AreaModelSquareLength) || input.AreaModelSquareLength <= 0d ||
            !double.IsFinite(input.InitialSoilMoistureNormalized) || input.InitialSoilMoistureNormalized is < 0d or > 1d ||
            !double.IsFinite(input.PotentialEvapotranspirationModelLengthPerYear) || input.PotentialEvapotranspirationModelLengthPerYear < 0d ||
            input.Temperature.AlgorithmVersion != TemperatureField.AlgorithmVersion || !double.IsFinite(input.Temperature.Sample.SurfaceCelsius))
            throw new ArgumentOutOfRangeException(nameof(input), "Area, soil state, potential ET and upstream temperature must be finite and qualified.");
    }

    private static GroundwaterTransfer[] ValidateTransfers(IEnumerable<GroundwaterTransfer> source, IReadOnlyList<WaterBudgetCell> cells)
    {
        // Canonicalize before every validation and aggregation: floating-point sums
        // must not depend on the caller's enumeration order or scheduling.
        GroundwaterTransfer[] values = source
            .OrderBy(transfer => transfer.TransferId.High)
            .ThenBy(transfer => transfer.TransferId.Low)
            .ToArray();
        if (values.Select(transfer => transfer.TransferId).Distinct().Count() != values.Length)
            throw new ArgumentException("Groundwater transfer IDs must be unique.", nameof(source));
        HashSet<StableId> reservoirs = cells.Select(cell => cell.ReservoirId).ToHashSet();
        foreach (GroundwaterTransfer transfer in values)
        {
            if (!Enum.IsDefined(transfer.Kind) || transfer.SourceReservoirId == transfer.DestinationReservoirId ||
                !reservoirs.Contains(transfer.SourceReservoirId) || !reservoirs.Contains(transfer.DestinationReservoirId) ||
                !double.IsFinite(transfer.FlowModelVolumePerYear) || transfer.FlowModelVolumePerYear < 0d)
                throw new ArgumentOutOfRangeException(nameof(source), "Transfers must be finite, link distinct known reservoirs, and use a known kind.");
        }

        foreach (IGrouping<StableId, GroundwaterTransfer> outgoing in values.GroupBy(transfer => transfer.SourceReservoirId))
        {
            double rechargeVolume = cells.Single(cell => cell.ReservoirId == outgoing.Key).RechargeModelVolumePerYear;
            double transferred = outgoing.Sum(transfer => transfer.FlowModelVolumePerYear);
            if (!double.IsFinite(transferred) || transferred > rechargeVolume + TransferTolerance(rechargeVolume))
                throw new ArgumentException("Outgoing groundwater cannot exceed locally counted recharge; a resurgence is a transfer, not new water.", nameof(source));
        }
        return values;
    }

    private static void EnsureBalanced(WaterBudgetCell cell)
    {
        if (!double.IsFinite(cell.ActualEvapotranspirationModelLengthPerYear) || !double.IsFinite(cell.RunoffModelLengthPerYear) ||
            !double.IsFinite(cell.RechargeModelLengthPerYear) || !double.IsFinite(cell.StorageChangeModelLengthPerYear) ||
            !double.IsFinite(cell.SoilMoistureNormalized) || cell.ActualEvapotranspirationModelLengthPerYear < 0d ||
            cell.RunoffModelLengthPerYear < 0d || cell.RechargeModelLengthPerYear < 0d || cell.SoilMoistureNormalized is < 0d or > 1d ||
            !double.IsFinite(cell.RechargeModelVolumePerYear) ||
            Math.Abs(cell.ResidualModelLengthPerYear) > TransferTolerance(cell.ReferenceFlowModelLengthPerYear))
            throw new OverflowException("The calculated water budget is non-finite, outside bounds, or fails its strict conservation tolerance.");
    }

    private static double TransferTolerance(double reference) => AbsoluteResidualToleranceModelLengthPerYear + (RelativeResidualTolerance * reference);
}

public enum WaterBudgetCouplingStatus : byte { Converged = 0, NonConverged = 1, DegradedPublished = 2 }

public sealed class WaterBudgetCouplingSettings
{
    public WaterBudgetCouplingSettings(int maximumIterations, double convergenceTolerance, bool allowDegradedPublication)
    {
        if (maximumIterations <= 0 || !double.IsFinite(convergenceTolerance) || convergenceTolerance < 0d)
            throw new ArgumentOutOfRangeException(nameof(maximumIterations), "Iterations must be positive and tolerance finite non-negative.");
        MaximumIterations = maximumIterations;
        ConvergenceTolerance = convergenceTolerance;
        AllowDegradedPublication = allowDegradedPublication;
    }
    public int MaximumIterations { get; }
    public double ConvergenceTolerance { get; }
    public bool AllowDegradedPublication { get; }
}

public readonly record struct WaterBudgetCouplingStep(WaterBudgetSnapshot Snapshot, double MaximumAbsoluteChange);

public sealed class WaterBudgetCouplingResult
{
    internal WaterBudgetCouplingResult(WaterBudgetCouplingStatus status, int iterationsExecuted, double finalMaximumAbsoluteChange, WaterBudgetSnapshot? publishedSnapshot)
    { Status = status; IterationsExecuted = iterationsExecuted; FinalMaximumAbsoluteChange = finalMaximumAbsoluteChange; PublishedSnapshot = publishedSnapshot; }
    public WaterBudgetCouplingStatus Status { get; }
    public int IterationsExecuted { get; }
    public double FinalMaximumAbsoluteChange { get; }
    public WaterBudgetSnapshot? PublishedSnapshot { get; }
    public bool IsPublished => PublishedSnapshot is not null;
}

/// <summary>Bounded preparatory coupling; it never mutates a published geographic snapshot.</summary>
public static class WaterBudgetCouplingSolver
{
    public static WaterBudgetCouplingResult Run(WaterBudgetCouplingSettings settings, Func<int, WaterBudgetCouplingStep> evaluateIteration)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(evaluateIteration);
        WaterBudgetCouplingStep last = default;
        for (int iteration = 0; iteration < settings.MaximumIterations; iteration++)
        {
            WaterBudgetCouplingStep step = evaluateIteration(iteration);
            if (step.Snapshot is null || step.Snapshot.Iteration != iteration || !double.IsFinite(step.MaximumAbsoluteChange) || step.MaximumAbsoluteChange < 0d)
                throw new ArgumentOutOfRangeException(nameof(evaluateIteration), "Each coupling step must return its matching immutable iteration snapshot and finite non-negative diagnostic.");
            last = step;
            if (step.MaximumAbsoluteChange <= settings.ConvergenceTolerance)
                return new WaterBudgetCouplingResult(WaterBudgetCouplingStatus.Converged, iteration + 1, step.MaximumAbsoluteChange, step.Snapshot);
        }
        return settings.AllowDegradedPublication
            ? new WaterBudgetCouplingResult(WaterBudgetCouplingStatus.DegradedPublished, settings.MaximumIterations, last.MaximumAbsoluteChange, last.Snapshot)
            : new WaterBudgetCouplingResult(WaterBudgetCouplingStatus.NonConverged, settings.MaximumIterations, last.MaximumAbsoluteChange, null);
    }
}
