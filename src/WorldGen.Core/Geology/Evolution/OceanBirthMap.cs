using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>Birth-time interval relative to the start of the main history (t=0).</summary>
public sealed record OceanFormationEvent(int Id, double FirstBirthTimeMyr, double LastBirthTimeMyr,
    string Provenance);

/// <summary>
/// Explicit initial ocean chronology. It binds birth times to actual oceanic
/// carrier quantities and a complete atlas. It does not invent the missing
/// prehistory from current coastlines, plate labels or nearest-ridge distance.
/// Event labels are geological provenance, NOT mechanical plate IDs.
/// </summary>
public sealed class OceanBirthMap
{
    public const string AlgorithmId = "explicit-ocean-birth-records-v1";
    public int Seed { get; }
    public int Side { get; }
    public double ReferenceWidth { get; }
    public double ReferenceLength { get; }
    public string Provenance { get; }
    public string CarrierChecksum { get; }
    public string Checksum { get; }
    public ReadOnlyCollection<double> BirthTimeMyr { get; }
    public ReadOnlyCollection<double> AgeMyr { get; }
    public ReadOnlyCollection<int> SourceEventIds { get; }
    public ReadOnlyCollection<OceanFormationEvent> Events { get; }

    private OceanBirthMap(int seed, TectonicScalePlan scale, int side, double[] ocean,
        double[] birth, int[] labels, OceanFormationEvent[] events, string provenance)
    {
        Seed = seed; Side = side; ReferenceWidth = scale.ReferenceWidth; ReferenceLength = scale.ReferenceLength;
        Provenance = provenance; CarrierChecksum = HashDoubles(ocean);
        BirthTimeMyr = Array.AsReadOnly(birth); SourceEventIds = Array.AsReadOnly(labels);
        Events = Array.AsReadOnly(events);
        AgeMyr = Array.AsReadOnly(birth.Select((t, i) => ocean[i] > 0 ? -t : 0).ToArray());
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            AlgorithmId, seed, side, ReferenceWidth, ReferenceLength, CarrierChecksum, provenance, events })));
        Span<byte> b = stackalloc byte[8];
        foreach (double t in birth) { BinaryPrimitives.WriteDoubleLittleEndian(b, t); hash.AppendData(b); }
        foreach (int label in labels) { BinaryPrimitives.WriteInt64LittleEndian(b, label); hash.AppendData(b); }
        Checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static OceanBirthMap Create(int seed, TectonicScalePlan scale, int side,
        IReadOnlyList<double> oceanicKm, IReadOnlyList<double> birthTimeMyr, IReadOnlyList<int> sourceEventIds,
        IReadOnlyList<OceanFormationEvent> events, string provenance)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(oceanicKm);
        ArgumentNullException.ThrowIfNull(birthTimeMyr); ArgumentNullException.ThrowIfNull(sourceEventIds);
        ArgumentNullException.ThrowIfNull(events);
        if (side < 32 || side > 512 || (side & (side - 1)) != 0 || oceanicKm.Count != side * side
            || birthTimeMyr.Count != side * side || sourceEventIds.Count != side * side
            || string.IsNullOrWhiteSpace(provenance) || events.Count > 4096)
            throw new ArgumentException("Invalid ocean chronology geometry, provenance or event budget.");
        OceanFormationEvent[] catalog = events.ToArray();
        if (catalog.Any(e => e is null || e.Id < 0 || !double.IsFinite(e.FirstBirthTimeMyr)
            || !double.IsFinite(e.LastBirthTimeMyr) || e.FirstBirthTimeMyr > e.LastBirthTimeMyr
            || e.LastBirthTimeMyr > 0 || string.IsNullOrWhiteSpace(e.Provenance))
            || catalog.Select(e => e.Id).Distinct().Count() != catalog.Length)
            throw new ArgumentException("Invalid or duplicate formation event.");
        Array.Sort(catalog, (a, b) => a.Id.CompareTo(b.Id));
        var lookup = catalog.ToDictionary(e => e.Id);
        double[] o = oceanicKm.ToArray(), birth = birthTimeMyr.ToArray(); int[] labels = sourceEventIds.ToArray();
        for (int i = 0; i < o.Length; i++)
        {
            if (!double.IsFinite(o[i]) || o[i] < 0 || !double.IsFinite(birth[i]) || birth[i] > 0)
                throw new ArgumentException($"Invalid ocean carrier or birth time at cell {i}.");
            if (o[i] == 0)
            {
                if (birth[i] != 0 || labels[i] != -1)
                    throw new ArgumentException($"An absent ocean carrier cannot have a birth record: cell {i}.");
            }
            else if (!lookup.TryGetValue(labels[i], out var source)
                || birth[i] < source.FirstBirthTimeMyr || birth[i] > source.LastBirthTimeMyr)
                throw new ArgumentException($"Missing or out-of-event ocean birth at cell {i}; uniform-age fallback forbidden.");
        }
        return new(seed, scale, side, o, birth, labels, catalog, provenance);
    }

    /// <summary>
    /// Build a complete initial ocean chronology from EXPLICIT reconstructed
    /// spreading episodes. Unknown or conflicting histories are refused, not
    /// assigned 50 Myr or a nearest-boundary age. This can also generate analytic
    /// fixtures; it does not infer episodes from a finished heightmap.
    /// Periodic event images must be explicitly supplied in an unwrapped frame.
    /// </summary>
    public static OceanBirthMap Reconstruct(int seed, TectonicScalePlan scale, int side,
        IReadOnlyList<double> oceanicKm, IReadOnlyList<SpreadingEpisode> episodes, string provenance)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(oceanicKm);
        ArgumentNullException.ThrowIfNull(episodes);
        if (side < 32 || side > 512 || (side & (side - 1)) != 0 || oceanicKm.Count != side * side
            || episodes.Count == 0 || (long)episodes.Count * side * side > 16777216
            || string.IsNullOrWhiteSpace(provenance)
            || oceanicKm.Any(v => !double.IsFinite(v) || v < 0))
            throw new ArgumentException("Invalid reconstruction geometry or explicit episode budget.");
        SpreadingEpisode[] sources = episodes.ToArray();
        if (sources.Any(e => e is null)) throw new ArgumentException("Null spreading episode.");
        // Validate event geometries before making the potentially large raster.
        foreach (var source in sources) SpreadingKinematics.TryResolveBirth(source, source.Ax, source.Az, out _);
        sources = sources.OrderBy(e => e.SourceEventId).ThenBy(e => e.Ax).ThenBy(e => e.Az)
            .ThenBy(e => e.Bx).ThenBy(e => e.Bz).ThenBy(e => e.RidgeVelocityX).ThenBy(e => e.RidgeVelocityZ)
            .ThenBy(e => e.MaterialVelocityX).ThenBy(e => e.MaterialVelocityZ)
            .ThenBy(e => e.FirstBirthTimeMyr).ThenBy(e => e.LastBirthTimeMyr).ToArray();
        var catalog = sources.GroupBy(e => e.SourceEventId).OrderBy(g => g.Key).Select(g =>
        {
            var first = g.First();
            if (g.Any(e => e.FirstBirthTimeMyr != first.FirstBirthTimeMyr || e.LastBirthTimeMyr != first.LastBirthTimeMyr))
                throw new ArgumentException("One formation-event ID cannot have conflicting active intervals.");
            return new OceanFormationEvent(g.Key, first.FirstBirthTimeMyr, first.LastBirthTimeMyr, provenance);
        }).ToArray();
        var birth = new double[side * side]; var labels = Enumerable.Repeat(-1, side * side).ToArray();
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x; if (oceanicKm[i] == 0) continue;
            double px = (x + .5) * scale.ReferenceWidth / side, pz = (z + .5) * scale.ReferenceLength / side;
            OceanBirthWitness? selected = null;
            foreach (var source in sources)
            {
                if (!SpreadingKinematics.TryResolveBirth(source, px, pz, out var witness)) continue;
                if (selected is { } previous && (previous.SourceEventId != witness.SourceEventId || previous.BirthTimeMyr != witness.BirthTimeMyr))
                    throw new ArgumentException($"Conflicting ocean histories at cell {i}; resolve their chronology explicitly.");
                selected = witness;
            }
            if (selected is not { } accepted)
                throw new ArgumentException($"Unresolved ocean history at cell {i}; no uniform-age fallback.");
            birth[i] = accepted.BirthTimeMyr; labels[i] = accepted.SourceEventId;
        }
        // Include the actual motion parameters, not just a human-readable label.
        string evidence = provenance + "|" + SpreadingKinematics.AlgorithmId + "|" + JsonSerializer.Serialize(sources);
        return Create(seed, scale, side, oceanicKm, birth, labels, catalog, evidence);
    }

    internal void RequireCompatible(int seed, TectonicScalePlan scale, int side, IReadOnlyList<double> ocean)
    {
        if (Seed != seed || Side != side || Math.Abs(ReferenceWidth - scale.ReferenceWidth) > 1e-8 || Math.Abs(ReferenceLength - scale.ReferenceLength) > 1e-8
            || HashDoubles(ocean) != CarrierChecksum)
            throw new ArgumentException("Ocean chronology belongs to different materials, seed, sampling or full-atlas aspect.");
    }

    internal double[] CreateAgeMoments(IReadOnlyList<double> ocean)
    {
        if (ocean.Count != Side * Side || HashDoubles(ocean) != CarrierChecksum)
            throw new ArgumentException("Ocean-age moments require the bound carrier field.");
        var result = new double[ocean.Count];
        for (int i = 0; i < result.Length; i++)
        {
            result[i] = ocean[i] * AgeMyr[i];
            if (!double.IsFinite(result[i]) || (ocean[i] > 0 && AgeMyr[i] > 0 && result[i] == 0))
                throw new ArithmeticException("Unrepresentable ocean birth-age moment; no silent zero.");
        }
        return result;
    }

    private static string HashDoubles(IReadOnlyList<double> values)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> b = stackalloc byte[8];
        foreach (double value in values) { BinaryPrimitives.WriteDoubleLittleEndian(b, value); hash.AppendData(b); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
