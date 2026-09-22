using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Prescribed motion of an existing origin, in reference units / model Myr.</summary>
public readonly record struct OriginMotion(int OriginId, double Vx, double Vz);
public sealed record PrehistoryPhase(string Label, double Duration,
    IReadOnlyList<OriginMotion>? Motions = null);

public sealed record OceanPrehistoryCoverage(double TimeMyr, double OceanicVolumeKm3,
    double InheritedVolumeKm3, double FormedSinceStartVolumeKm3,
    double? InheritedCarrierFraction, bool HasUniformAgePriorWithoutBirthEvidence,
    string InitialChronologyPolicy, string Status,
    string Semantics = "All oceanic MATERIAL, including mixed/emerged columns; not ocean-water area. No height test or Earth calibration.");

public sealed record PrehistoryPhaseReceipt(string Label, double StartTimeMyr, double EndTimeMyr,
    int CumulativeSteps, string ParentHistoryChecksum, string InitialMaterialChecksum,
    string FinalMaterialChecksum, string HistoryChecksum, double CreatedOceanicVolumeKm3,
    double RecycledOceanicVolumeKm3, ReadOnlyCollection<OriginMotion> PrescribedMotions,
    OceanPrehistoryCoverage Coverage);

public sealed record OceanPrehistoryResult(MaterialBoundHistory FinalHistory,
    ReadOnlyCollection<PrehistoryPhaseReceipt> Phases, string Checksum);

/// <summary>
/// Forward, material-preserving phase driver and provenance accounting. It
/// deliberately does not infer a whole prehistory from today's coast or assign
/// old ocean a random birth date. Unknown initial provenance remains explicit,
/// even after mixing has changed its MEAN age. No automatic geographic PASS.
/// Checkpoints live in memory: a final aggregate raster is insufficient to
/// recover the retained per-origin C/O/age/inherited quantities.
/// </summary>
public static class OceanPrehistory
{
    public const string AlgorithmId = "forward-material-phases-with-inherited-coverage-v1";

    internal static TectonicPlate[] ApplyMotions(IReadOnlyList<TectonicPlate> plates,
        IReadOnlyList<OriginMotion>? motions)
    {
        ArgumentNullException.ThrowIfNull(plates);
        TectonicPlate[] result = plates.ToArray();
        if (result.Length is < 1 or > 16 || result.Where((p, i) => p.Id != i).Any())
            throw new ArgumentException("A complete stable origin table is required.");
        if (motions is null) return result;
        OriginMotion[] supplied = motions.ToArray();
        if (supplied.Length != result.Length || supplied.Select(p => p.OriginId).Distinct().Count() != result.Length ||
            supplied.Any(p => p.OriginId < 0 || p.OriginId >= result.Length || !double.IsFinite(p.Vx) || !double.IsFinite(p.Vz)
                || Math.Sqrt(p.Vx * p.Vx + p.Vz * p.Vz) > 4000))
            throw new ArgumentException("Each existing origin needs exactly one finite motion within the 4000-unit speed budget.");
        foreach (OriginMotion p in supplied.OrderBy(p => p.OriginId))
            result[p.OriginId] = result[p.OriginId] with { Vx = p.Vx, Vz = p.Vz };
        return result;
    }

    public static OceanPrehistoryCoverage Describe(MaterialBoundHistory history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var f = history.Final;
        double dxKm = history.ReferenceWidth / f.Side * MaterialBoundHistory.ReferenceKmPerUnit;
        double dzKm = history.ReferenceLength / f.Side * MaterialBoundHistory.ReferenceKmPerUnit;
        double area = dxKm * dzKm;
        double ocean = CrustTransport.Sum(f.OceanicKm), inherited = CrustTransport.Sum(f.InheritedOceanicKm);
        double tolerance = 1e-8 + 2e-12 * Math.Abs(ocean); // same inventory error budget, no new relaxed gate
        if (!double.IsFinite(ocean) || !double.IsFinite(inherited) || ocean < 0 || inherited < 0 || inherited > ocean + tolerance)
            throw new ArithmeticException("Invalid inherited ocean inventory.");
        bool uniformPrior = history.InitialOceanBirthMapChecksum is null;
        string policy = uniformPrior ? "INITIAL_UNIFORM_AGE_ASSUMPTION" : "SUPPLIED_INITIAL_BIRTH_RECORDS_NOT_EARTH_VALIDATION";
        string status = ocean == 0 ? "NO_OCEANIC_CARRIER" : uniformPrior && inherited > 0
            ? "INCOMPLETE_INHERITED_FORMATION_HISTORY" : "RECORDED_NUMERICAL_CHRONOLOGY_NOT_GEOGRAPHIC_ACCEPTANCE";
        // Do not silently force a value into [0,1]. A tiny negative born amount
        // due to round-off is retained in the report, alongside the inventory
        // tolerance; it cannot conceal surviving inherited material.
        return new(f.Time, ocean * area, inherited * area, (ocean - inherited) * area,
            ocean > 0 ? inherited / ocean : null, uniformPrior && inherited > 0, policy, status);
    }

    /// <summary>
    /// Execute explicitly supplied phases of the SAME material history. Unknown
    /// forcing histories are not invented from plate IDs. The template supplies
    /// parameters, not a hidden first phase: its Duration must be zero.
    /// Bounds apply to the entire run (max 32 phases, 200 model Myr, 4096 steps).
    /// Each receipt connects the exact previous final and next initial state.
    /// </summary>
    public static OceanPrehistoryResult Run(int seed, TectonicScalePlan scale,
        TectonicEvolutionSettings template, IReadOnlyList<PrehistoryPhase> phases,
        ContinentalAssemblage? assemblage = null, OceanBirthMap? initialBirths = null)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(template); ArgumentNullException.ThrowIfNull(phases);
        if (template.Duration != 0 || phases.Count is < 1 or > 32)
            throw new ArgumentException("Use a zero-duration initialization and 1..32 explicit phases.");
        // Freeze and validate the entire forcing schedule before any evolution.
        PrehistoryPhase[] schedule = phases.Select(p => p is null
            ? throw new ArgumentException("Null phase.")
            : new PrehistoryPhase(p.Label, p.Duration, p.Motions is null ? null : Array.AsReadOnly(p.Motions.ToArray()))).ToArray();
        if (schedule.Any(p => string.IsNullOrWhiteSpace(p.Label) || !double.IsFinite(p.Duration) || p.Duration <= 0 || p.Duration > 100)
            || schedule.Sum(p => p.Duration) > MaterialBoundHistory.MaximumPrehistoryTime)
            throw new ArgumentException("Invalid phase duration, label or cumulative time budget.");
        // A synthetic stable table suffices for validating ID coverage and
        // velocity bounds; it is not used as the world's plate geometry.
        var validationTable = Enumerable.Range(0, template.PlateCount).Select(i => new TectonicPlate(i, 0, 0, 0, 0)).ToArray();
        foreach (var phase in schedule) ApplyMotions(validationTable, phase.Motions);
        MaterialBoundHistory current = MaterialBoundHistory.BeginPrehistory(seed, scale, template, assemblage, initialBirths);
        double initialContinental = current.Ledger[0].ContinentalVolume;
        double initialOceanic = current.Ledger[0].OceanicVolume, created = 0, recycled = 0;
        var receipts = new List<PrehistoryPhaseReceipt>();
        foreach (var phase in schedule)
        {
            string parent = current.Checksum, expectedInitial = current.FinalMaterialChecksum;
            double start = current.Final.Time;
            current = current.ContinuePrehistory(scale, phase.Duration, phase.Motions);
            if (current.InitialMaterialChecksum != expectedInitial || current.ContinuationParentChecksum != parent || current.Initial.Time != start)
                throw new ArithmeticException("A phase lost its material/time lineage.");
            TectonicLedger end = current.Ledger[^1];
            created += end.CreatedOceanicVolume; recycled += end.RecycledOceanicVolume;
            CrustTransport.RequireBalance(initialContinental, end.ContinentalVolume, "cross-phase continental volume");
            CrustTransport.RequireBalance(initialOceanic + created - recycled, end.OceanicVolume, "cross-phase oceanic volume");
            receipts.Add(new(phase.Label, start, end.Time, current.CompletedPrehistorySteps, parent,
                expectedInitial, current.FinalMaterialChecksum, current.Checksum, end.CreatedOceanicVolume,
                end.RecycledOceanicVolume, Array.AsReadOnly(current.Plates.Select(p => new OriginMotion(p.Id, p.Vx, p.Vz)).ToArray()), Describe(current)));
        }
        string canonical = JsonSerializer.Serialize(new { AlgorithmId, seed, scale.ReferenceWidth, scale.ReferenceLength,
            template, initialBirths = initialBirths?.Checksum, assemblage = assemblage?.Checksum, receipts });
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(current, receipts.AsReadOnly(), hash);
    }
}
