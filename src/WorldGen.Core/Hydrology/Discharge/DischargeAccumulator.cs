using System.Collections.ObjectModel;
using ISRWorldGen.Core.Climate.WaterBudget;
using ISRWorldGen.Core.Foundation;
using ISRWorldGen.Core.Hydrology.Depressions;

namespace ISRWorldGen.Core.Hydrology.Discharge;

/// <summary>Explicit reach-scale terms in L³/Ymod. Storage is signed: positive retains water, negative releases it.</summary>
public readonly record struct DischargeAdjustment(long CellId, double LossModelVolumePerYear, double StorageChangeModelVolumePerYear);

/// <summary>Frozen thresholds used only to classify the accumulated, conserved result.</summary>
public sealed class DischargeClassificationSettings
{
    public DischargeClassificationSettings(double streamMinimumFlowModelVolumePerYear, double riverMinimumFlowModelVolumePerYear, double riverMinimumDrainageAreaModelSquareLength)
    {
        if (!double.IsFinite(streamMinimumFlowModelVolumePerYear) || streamMinimumFlowModelVolumePerYear < 0d ||
            !double.IsFinite(riverMinimumFlowModelVolumePerYear) || riverMinimumFlowModelVolumePerYear < streamMinimumFlowModelVolumePerYear ||
            !double.IsFinite(riverMinimumDrainageAreaModelSquareLength) || riverMinimumDrainageAreaModelSquareLength < 0d)
            throw new ArgumentOutOfRangeException(nameof(streamMinimumFlowModelVolumePerYear), "Classification thresholds must be finite, non-negative, and ordered.");
        StreamMinimumFlowModelVolumePerYear = streamMinimumFlowModelVolumePerYear;
        RiverMinimumFlowModelVolumePerYear = riverMinimumFlowModelVolumePerYear;
        RiverMinimumDrainageAreaModelSquareLength = riverMinimumDrainageAreaModelSquareLength;
    }

    public double StreamMinimumFlowModelVolumePerYear { get; }
    public double RiverMinimumFlowModelVolumePerYear { get; }
    public double RiverMinimumDrainageAreaModelSquareLength { get; }
}

public enum DischargeReachClass : byte { Dry = 0, Rill = 1, Stream = 2, River = 3 }

/// <summary>Immutable per-node accounting. Incoming transfers are listed separately from locally retained recharge.</summary>
public sealed record DischargeReach(
    long CellId,
    long? ReceiverId,
    double LocalRunoffModelVolumePerYear,
    double LocalRetainedRechargeModelVolumePerYear,
    double IncomingTransferModelVolumePerYear,
    double UpstreamDischargeModelVolumePerYear,
    double LossModelVolumePerYear,
    double StorageChangeModelVolumePerYear,
    double DischargeModelVolumePerYear,
    double DrainageAreaModelSquareLength,
    DischargeReachClass Classification,
    double ResidualModelVolumePerYear);

public sealed record DischargeTerminal(long CellId, DrainageTerminalKind Kind, double DischargeModelVolumePerYear);

/// <summary>Global conservation evidence for a fully routed basin snapshot.</summary>
public sealed record DischargeBalance(
    double LocalWaterModelVolumePerYear,
    double TerminalDischargeModelVolumePerYear,
    double LossModelVolumePerYear,
    double StorageChangeModelVolumePerYear,
    double ResidualModelVolumePerYear,
    double ReferenceFlowModelVolumePerYear);

/// <summary>Immutable hand-off for L05-C/L07; arrays are canonically ordered by stable cell ID.</summary>
public sealed class DischargeSnapshot
{
    internal DischargeSnapshot(IEnumerable<DischargeReach> reaches, IEnumerable<DischargeTerminal> terminals, DischargeBalance balance)
    {
        Reaches = Array.AsReadOnly(reaches.OrderBy(reach => reach.CellId).ToArray());
        Terminals = Array.AsReadOnly(terminals.OrderBy(terminal => terminal.CellId).ToArray());
        Balance = balance;
    }

    public const int AlgorithmVersion = 1;
    public ReadOnlyCollection<DischargeReach> Reaches { get; }
    public ReadOnlyCollection<DischargeTerminal> Terminals { get; }
    public DischargeBalance Balance { get; }
}

/// <summary>
/// Deterministic upstream-to-downstream discharge accumulation. A groundwater transfer is
/// subtracted from its source recharge exactly once and added at its destination exactly once.
/// </summary>
public static class DischargeAccumulator
{
    public const double AbsoluteResidualToleranceModelVolumePerYear = 1e-9d;
    public const double RelativeResidualTolerance = 1e-6d;

    public static DischargeSnapshot Accumulate(
        IWaterBudgetSnapshotSource waterBudget,
        DrainageTopology topology,
        IEnumerable<DischargeAdjustment> adjustments,
        DischargeClassificationSettings classification)
    {
        ArgumentNullException.ThrowIfNull(waterBudget);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(adjustments);
        ArgumentNullException.ThrowIfNull(classification);
        if (waterBudget.AlgorithmVersion != WaterBudgetSnapshot.AlgorithmVersion)
            throw new ArgumentException("The water-budget snapshot uses an unsupported algorithm version.", nameof(waterBudget));

        WaterBudgetCell[] cells = waterBudget.Cells.OrderBy(cell => cell.CellId).ToArray();
        RoutedCell[] routed = topology.Cells.OrderBy(cell => cell.Id).ToArray();
        ValidateCells(cells, routed);
        Dictionary<long, WaterBudgetCell> budgets = cells.ToDictionary(cell => cell.CellId);
        Dictionary<StableId, WaterBudgetCell> reservoirs = cells.ToDictionary(cell => cell.ReservoirId);
        Dictionary<long, DischargeAdjustment> byAdjustment = ValidateAdjustments(adjustments, budgets);
        TransferTotals transferTotals = ValidateAndAggregateTransfers(waterBudget.Transfers, reservoirs);

        var states = new Dictionary<long, MutableReach>(cells.Length);
        foreach (WaterBudgetCell cell in cells)
        {
            DischargeAdjustment adjustment = byAdjustment.GetValueOrDefault(cell.CellId);
            double runoff = MultiplyFinite(cell.RunoffModelLengthPerYear, cell.AreaModelSquareLength, nameof(waterBudget));
            double recharge = cell.RechargeModelVolumePerYear;
            double retainedRecharge = SubtractFinite(recharge, transferTotals.OutgoingByReservoir.GetValueOrDefault(cell.ReservoirId), nameof(waterBudget));
            double incomingTransfer = transferTotals.IncomingByReservoir.GetValueOrDefault(cell.ReservoirId);
            states.Add(cell.CellId, new MutableReach(routed.Single(route => route.Id == cell.CellId), runoff, retainedRecharge, incomingTransfer, adjustment));
        }

        Dictionary<long, List<long>> upstream = cells.ToDictionary(cell => cell.CellId, _ => new List<long>());
        foreach (MutableReach state in states.Values)
            if (state.Route.ReceiverId is long receiver) upstream[receiver].Add(state.Route.Id);
        foreach (List<long> ids in upstream.Values) ids.Sort();

        var pending = new Dictionary<long, int>(upstream.Count);
        var ready = new PriorityQueue<long, long>();
        foreach ((long id, List<long> ids) in upstream.OrderBy(pair => pair.Key))
        {
            pending.Add(id, ids.Count);
            if (ids.Count == 0) ready.Enqueue(id, id);
        }

        var reaches = new List<DischargeReach>(cells.Length);
        while (ready.TryDequeue(out long id, out _))
        {
            MutableReach state = states[id];
            double upstreamFlow = SumFinite(upstream[id].Select(source => states[source].Outflow), nameof(topology));
            double inflow = SumFinite([state.LocalRunoff, state.RetainedRecharge, state.IncomingTransfer, upstreamFlow], nameof(topology));
            double outflow = SubtractFinite(SubtractFinite(inflow, state.Adjustment.LossModelVolumePerYear, nameof(adjustments)), state.Adjustment.StorageChangeModelVolumePerYear, nameof(adjustments));
            if (outflow < 0d) throw new ArgumentOutOfRangeException(nameof(adjustments), "Loss and storage cannot exceed the water available at a drainage node.");
            double residual = inflow - outflow - state.Adjustment.LossModelVolumePerYear - state.Adjustment.StorageChangeModelVolumePerYear;
            EnsureBalanced(residual, Math.Max(inflow, Math.Abs(outflow)), nameof(adjustments));
            state.Outflow = outflow;
            state.DrainageArea = AddFinite(budgets[id].AreaModelSquareLength, SumFinite(upstream[id].Select(source => states[source].DrainageArea), nameof(topology)), nameof(topology));
            reaches.Add(new DischargeReach(id, state.Route.ReceiverId, state.LocalRunoff, state.RetainedRecharge, state.IncomingTransfer, upstreamFlow,
                state.Adjustment.LossModelVolumePerYear, state.Adjustment.StorageChangeModelVolumePerYear, outflow, state.DrainageArea,
                Classify(outflow, state.DrainageArea, classification), residual));
            if (state.Route.ReceiverId is long receiver && --pending[receiver] == 0) ready.Enqueue(receiver, receiver);
        }
        if (reaches.Count != cells.Length) throw new ArgumentException("The routing graph must be acyclic for discharge accumulation.", nameof(topology));

        Dictionary<long, DrainageConnectivity> connectivity = topology.Connectivity.ToDictionary(item => item.CellId);
        DischargeTerminal[] terminals = states.Values.Where(state => state.Route.ReceiverId is null).OrderBy(state => state.Route.Id)
            .Select(state => new DischargeTerminal(state.Route.Id, connectivity[state.Route.Id].TerminalKind, state.Outflow)).ToArray();
        double local = SumFinite(states.Values.Select(state => SumFinite([state.LocalRunoff, state.RetainedRecharge, state.IncomingTransfer], nameof(waterBudget))), nameof(waterBudget));
        double terminal = SumFinite(terminals.Select(item => item.DischargeModelVolumePerYear), nameof(topology));
        double losses = SumFinite(states.Values.Select(state => state.Adjustment.LossModelVolumePerYear), nameof(adjustments));
        double storage = SumFinite(states.Values.Select(state => state.Adjustment.StorageChangeModelVolumePerYear), nameof(adjustments));
        double globalResidual = local - terminal - losses - storage;
        double reference = Math.Max(local, Math.Max(terminal + losses, Math.Abs(storage)));
        EnsureBalanced(globalResidual, reference, nameof(waterBudget));
        return new DischargeSnapshot(reaches, terminals, new DischargeBalance(local, terminal, losses, storage, globalResidual, reference));
    }

    private static void ValidateCells(IReadOnlyList<WaterBudgetCell> budgets, IReadOnlyList<RoutedCell> routes)
    {
        if (budgets.Count == 0 || budgets.Select(cell => cell.CellId).Distinct().Count() != budgets.Count || budgets.Select(cell => cell.ReservoirId).Distinct().Count() != budgets.Count ||
            routes.Select(route => route.Id).Distinct().Count() != routes.Count || !budgets.Select(cell => cell.CellId).SequenceEqual(routes.Select(route => route.Id)))
            throw new ArgumentException("Water-budget and drainage cell IDs must be unique and exactly identical.");
        foreach (WaterBudgetCell cell in budgets)
            if (!double.IsFinite(cell.AreaModelSquareLength) || cell.AreaModelSquareLength <= 0d || !double.IsFinite(cell.RunoffModelLengthPerYear) || cell.RunoffModelLengthPerYear < 0d ||
                !double.IsFinite(cell.RechargeModelVolumePerYear) || cell.RechargeModelVolumePerYear < 0d)
                throw new ArgumentOutOfRangeException(nameof(budgets), "Water-budget flows and areas must be finite and non-negative.");
        foreach (RoutedCell route in routes)
            if (route.ReceiverId is long receiver && !routes.Any(candidate => candidate.Id == receiver))
                throw new ArgumentException("Every routed receiver must be a known cell.", nameof(routes));
    }

    private static Dictionary<long, DischargeAdjustment> ValidateAdjustments(IEnumerable<DischargeAdjustment> adjustments, IReadOnlyDictionary<long, WaterBudgetCell> budgets)
    {
        DischargeAdjustment[] values = adjustments.OrderBy(value => value.CellId).ToArray();
        if (values.Select(value => value.CellId).Distinct().Count() != values.Length || values.Any(value => !budgets.ContainsKey(value.CellId)))
            throw new ArgumentException("Adjustments must have unique known cell IDs.", nameof(adjustments));
        if (values.Any(value => !double.IsFinite(value.LossModelVolumePerYear) || value.LossModelVolumePerYear < 0d || !double.IsFinite(value.StorageChangeModelVolumePerYear)))
            throw new ArgumentOutOfRangeException(nameof(adjustments), "Loss must be finite non-negative and storage change finite.");
        return values.ToDictionary(value => value.CellId);
    }

    private static TransferTotals ValidateAndAggregateTransfers(IEnumerable<GroundwaterTransfer> transfers, IReadOnlyDictionary<StableId, WaterBudgetCell> reservoirs)
    {
        GroundwaterTransfer[] values = transfers.OrderBy(value => value.TransferId.High).ThenBy(value => value.TransferId.Low).ToArray();
        if (values.Select(value => value.TransferId).Distinct().Count() != values.Length) throw new ArgumentException("Transfer IDs must be unique.", nameof(transfers));
        var outgoing = new Dictionary<StableId, double>(); var incoming = new Dictionary<StableId, double>();
        foreach (GroundwaterTransfer transfer in values)
        {
            if (!Enum.IsDefined(transfer.Kind) || transfer.SourceReservoirId == transfer.DestinationReservoirId || !reservoirs.ContainsKey(transfer.SourceReservoirId) || !reservoirs.ContainsKey(transfer.DestinationReservoirId) || !double.IsFinite(transfer.FlowModelVolumePerYear) || transfer.FlowModelVolumePerYear < 0d)
                throw new ArgumentOutOfRangeException(nameof(transfers), "Transfers must be finite, known, distinct-reservoir movements with a known kind.");
            outgoing[transfer.SourceReservoirId] = AddFinite(outgoing.GetValueOrDefault(transfer.SourceReservoirId), transfer.FlowModelVolumePerYear, nameof(transfers));
            incoming[transfer.DestinationReservoirId] = AddFinite(incoming.GetValueOrDefault(transfer.DestinationReservoirId), transfer.FlowModelVolumePerYear, nameof(transfers));
        }
        foreach ((StableId reservoir, double total) in outgoing)
            if (total > reservoirs[reservoir].RechargeModelVolumePerYear + Tolerance(reservoirs[reservoir].RechargeModelVolumePerYear))
                throw new ArgumentException("Transfers cannot exceed their source recharge; a resurgence is never new local water.", nameof(transfers));
        return new TransferTotals(outgoing, incoming);
    }

    private static DischargeReachClass Classify(double flow, double area, DischargeClassificationSettings settings) => flow == 0d ? DischargeReachClass.Dry :
        flow < settings.StreamMinimumFlowModelVolumePerYear ? DischargeReachClass.Rill :
        flow < settings.RiverMinimumFlowModelVolumePerYear || area < settings.RiverMinimumDrainageAreaModelSquareLength ? DischargeReachClass.Stream : DischargeReachClass.River;
    private static void EnsureBalanced(double residual, double reference, string parameter)
    { if (!double.IsFinite(residual) || !double.IsFinite(reference) || Math.Abs(residual) > Tolerance(reference)) throw new OverflowException($"The discharge balance for '{parameter}' is non-finite or outside conservation tolerance."); }
    private static double Tolerance(double reference) => AbsoluteResidualToleranceModelVolumePerYear + (RelativeResidualTolerance * reference);
    private static double AddFinite(double left, double right, string parameter) { double value = left + right; if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "Accumulated flow exceeds the finite numeric domain."); return value; }
    private static double SubtractFinite(double left, double right, string parameter) { double value = left - right; if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "Discharge arithmetic exceeds the finite numeric domain."); return value; }
    private static double MultiplyFinite(double left, double right, string parameter) { double value = left * right; if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "Water-budget volume exceeds the finite numeric domain."); return value; }
    private static double SumFinite(IEnumerable<double> values, string parameter) { double sum = 0d; foreach (double value in values) { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(parameter, "Flow terms must be finite."); sum = AddFinite(sum, value, parameter); } return sum; }
    private sealed class MutableReach(RoutedCell route, double localRunoff, double retainedRecharge, double incomingTransfer, DischargeAdjustment adjustment)
    { public RoutedCell Route { get; } = route; public double LocalRunoff { get; } = localRunoff; public double RetainedRecharge { get; } = retainedRecharge; public double IncomingTransfer { get; } = incomingTransfer; public DischargeAdjustment Adjustment { get; } = adjustment; public double Outflow { get; set; } public double DrainageArea { get; set; } }
    private sealed record TransferTotals(IReadOnlyDictionary<StableId, double> OutgoingByReservoir, IReadOnlyDictionary<StableId, double> IncomingByReservoir);
}
