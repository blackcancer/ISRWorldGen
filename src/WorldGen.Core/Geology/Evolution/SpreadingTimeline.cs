using System.Collections.ObjectModel;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>One phase in an unwrapped frame. Endpoints are at StartTimeMyr.
/// A dormant phase transports existing floor but creates no oceanic material.</summary>
public sealed record SpreadingPhase(int SourceEventId, double StartTimeMyr, double EndTimeMyr,
    double Ax, double Az, double Bx, double Bz, double RidgeVelocityX, double RidgeVelocityZ,
    double MaterialVelocityX, double MaterialVelocityZ, bool CreatesOcean = true);

/// <summary>
/// Exact piecewise TRANSLATIONAL reconstruction of one material branch up to t=0.
/// Includes all displacement AFTER birth, even after ridge extinction or a velocity
/// change. Not a rigid-rotation model, an automatic tectonic reconstruction, or an
/// age guessed from the current distance to a ridge. Geometry must be unwrapped.
/// </summary>
public sealed class SpreadingTimeline
{
    public const string AlgorithmId = "dated-piecewise-translation-birth-reconstruction-v2-resolved-endpoints";
    public string Name { get; }
    public ReadOnlyCollection<SpreadingPhase> Phases { get; }
    private readonly SpreadingEpisode[] episodes;
    internal IReadOnlyList<SpreadingEpisode> Episodes => episodes;

    public SpreadingTimeline(string name, IReadOnlyList<SpreadingPhase> phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        if (string.IsNullOrWhiteSpace(name) || phases.Count is < 1 or > 128)
            throw new ArgumentException("Invalid timeline identity or phase budget.");
        var ordered = phases.ToArray();
        if (ordered.Any(p => p is null)) throw new ArgumentException("Null motion phase.");
        Array.Sort(ordered, (a, b) => a.StartTimeMyr.CompareTo(b.StartTimeMyr));
        var ids = new HashSet<int>();
        for (int k = 0; k < ordered.Length; k++)
        {
            var p = ordered[k];
            if (new[] { p.StartTimeMyr, p.EndTimeMyr, p.Ax, p.Az, p.Bx, p.Bz,
                p.RidgeVelocityX, p.RidgeVelocityZ, p.MaterialVelocityX, p.MaterialVelocityZ }.Any(v => !double.IsFinite(v))
                || p.StartTimeMyr >= p.EndTimeMyr || p.EndTimeMyr > 0
                || (k > 0 && ordered[k - 1].EndTimeMyr != p.StartTimeMyr)
                || (p.CreatesOcean ? p.SourceEventId < 0 || !ids.Add(p.SourceEventId) : p.SourceEventId != -1))
                throw new ArgumentException("Timeline has a gap, overlap, invalid event or nonfinite phase.");
        }
        if (ordered[^1].EndTimeMyr != 0) throw new ArgumentException("Motion after the last phase must reach observation t=0.");
        var built = new List<SpreadingEpisode>();
        double futureX = 0, futureZ = 0;
        for (int k = ordered.Length - 1; k >= 0; k--)
        {
            var p = ordered[k]; double dt = p.EndTimeMyr - p.StartTimeMyr;
            if (p.CreatesOcean)
            {
                double ox = p.RidgeVelocityX * dt + futureX + (p.MaterialVelocityX - p.RidgeVelocityX) * p.EndTimeMyr;
                double oz = p.RidgeVelocityZ * dt + futureZ + (p.MaterialVelocityZ - p.RidgeVelocityZ) * p.EndTimeMyr;
                var e = new SpreadingEpisode(p.SourceEventId, p.Ax + ox, p.Az + oz, p.Bx + ox, p.Bz + oz,
                    p.RidgeVelocityX, p.RidgeVelocityZ, p.MaterialVelocityX, p.MaterialVelocityZ,
                    p.StartTimeMyr, p.EndTimeMyr);
                // Validate once, independently of whether a parcel will use it.
                SpreadingKinematics.TryResolveBirth(e, e.Ax, e.Az, out _);
                built.Add(e);
            }
            futureX += p.MaterialVelocityX * dt; futureZ += p.MaterialVelocityZ * dt;
            if (!double.IsFinite(futureX) || !double.IsFinite(futureZ)) throw new ArithmeticException("Motion history overflow.");
        }
        episodes = built.OrderBy(e => e.FirstBirthTimeMyr).ToArray();
        Name = name; Phases = Array.AsReadOnly(ordered);
    }

    public bool TryResolveBirth(double x, double z, out OceanBirthWitness witness)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) throw new ArgumentException("Invalid observation point.");
        OceanBirthWitness? selected = null;
        SpreadingEpisode? selectedEpisode = null;
        foreach (var e in episodes)
        {
            if (!SpreadingKinematics.TryResolveBirth(e, x, z, out var w)) continue;
            if (selected is { } previous && (previous.SourceEventId != w.SourceEventId || previous.BirthTimeMyr != w.BirthTimeMyr))
            {
                // Only a shared temporal junction with the SAME birth instant
                // belongs to the younger phase. An isolated extinction endpoint
                // remains valid; do not create a one-cell hole by dropping it.
                bool sharedJunction = previous.BirthTimeMyr == w.BirthTimeMyr
                    && selectedEpisode!.LastBirthTimeMyr == w.BirthTimeMyr
                    && e.FirstBirthTimeMyr == w.BirthTimeMyr;
                if (!sharedJunction) throw new ArgumentException("Multiple birth histories reach the same parcel; resolve topology explicitly.");
            }
            selected = w; selectedEpisode = e;
        }
        witness = selected.GetValueOrDefault(); return selected.HasValue;
    }

    public static OceanBirthMap Reconstruct(int seed, TectonicScalePlan scale, int side,
        IReadOnlyList<double> oceanicKm, IReadOnlyList<SpreadingTimeline> timelines, string provenance)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(oceanicKm);
        ArgumentNullException.ThrowIfNull(timelines);
        if (side is < 32 or > 512 || (side & (side - 1)) != 0 || oceanicKm.Count != side * side
            || timelines.Count is < 1 or > 128 || string.IsNullOrWhiteSpace(provenance)
            || oceanicKm.Any(v => !double.IsFinite(v) || v < 0) || timelines.Any(t => t is null))
            throw new ArgumentException("Invalid full-atlas timeline reconstruction inputs.");
        var ordered = timelines.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        if (ordered.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != ordered.Length
            || (long)ordered.Sum(t => t.episodes.Length) * side * side > 16777216)
            throw new ArgumentException("Duplicate branch identity or exceeded reconstruction budget.");
        var catalog = ordered.SelectMany(t => t.episodes).GroupBy(e => e.SourceEventId).OrderBy(g => g.Key).Select(g =>
        {
            var e = g.First();
            if (g.Any(v => v.FirstBirthTimeMyr != e.FirstBirthTimeMyr || v.LastBirthTimeMyr != e.LastBirthTimeMyr))
                throw new ArgumentException("One birth event has conflicting intervals.");
            return new OceanFormationEvent(e.SourceEventId, e.FirstBirthTimeMyr, e.LastBirthTimeMyr, provenance);
        }).ToArray();
        double[] birth = new double[oceanicKm.Count]; int[] labels = Enumerable.Repeat(-1, birth.Length).ToArray();
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x; if (oceanicKm[i] == 0) continue;
            double px = (x + .5) * scale.ReferenceWidth / side, pz = (z + .5) * scale.ReferenceLength / side;
            OceanBirthWitness? selected = null;
            foreach (var t in ordered)
            {
                if (!t.TryResolveBirth(px, pz, out var w)) continue;
                if (selected is { } prior && (prior.SourceEventId != w.SourceEventId || prior.BirthTimeMyr != w.BirthTimeMyr))
                    throw new ArgumentException($"Conflicting chronology at atlas cell {i}.");
                selected = w;
            }
            if (selected is not { } found) throw new ArgumentException($"Unresolved chronology at atlas cell {i}; no age fallback.");
            birth[i] = found.BirthTimeMyr; labels[i] = found.SourceEventId;
        }
        string evidence = provenance + "|" + AlgorithmId + "|" + JsonSerializer.Serialize(ordered.Select(t => new { t.Name, t.Phases }));
        return OceanBirthMap.Create(seed, scale, side, oceanicKm, birth, labels, catalog, evidence);
    }
}
