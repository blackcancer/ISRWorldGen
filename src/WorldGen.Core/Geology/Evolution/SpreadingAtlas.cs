using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

public sealed record SpreadingAtlasReceipt(int OceanCells, long TestedTimelines, int IndexedImages,
    int IndexSide, string BoundaryPolicy, string Status);
public sealed record SpreadingAtlasResult(OceanBirthMap Births, SpreadingAtlasReceipt Receipt);

/// <summary>
/// Broad-phase spatial index around the EXACT translational birth inversions.
/// Historical swept segments, NOT present-day coasts or nearest plate edges,
/// determine ages. Explicit periodic copies permit a complete reference atlas.
/// No change to material amounts, heights, cooling or the default generator.
/// A covered prescribed history is a hypothesis, never geological acceptance.
/// </summary>
public static class SpreadingAtlas
{
    public const string AlgorithmId = "indexed-periodic-spreading-timelines-v1";
    // Exact segment inversions can differ at shared endpoints by floating-point
    // round-off. This is a fixed witness-agreement resolution, not an age edit.
    // Different event IDs ALWAYS conflict. The canonical first witness is kept.
    public const double DuplicateBirthToleranceMyr = 1e-9;
    private const int Bins = 32;
    private const int MaximumImages = 32768;
    private const int MaximumBinEntries = 1048576;
    private const long MaximumQueries = 67108864;
    private readonly record struct Image(int Timeline, long X, long Z);

    public static SpreadingAtlasResult Reconstruct(int seed, TectonicScalePlan scale, int side,
        IReadOnlyList<double> oceanicKm, IReadOnlyList<SpreadingTimeline> timelines,
        string provenance, bool periodic = true)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(oceanicKm);
        ArgumentNullException.ThrowIfNull(timelines);
        if (side is < 32 or > 512 || (side & (side - 1)) != 0 || oceanicKm.Count != side * side
            || timelines.Count is < 1 or > 1024 || string.IsNullOrWhiteSpace(provenance)
            || timelines.Any(t => t is null) || oceanicKm.Any(v => !double.IsFinite(v) || v < 0))
            throw new ArgumentException("Invalid bounded atlas reconstruction.");
        var ordered = timelines.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray();
        if (ordered.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != ordered.Length
            || ordered.Sum(t => t.Phases.Count) > 16384)
            throw new ArgumentException("Duplicate timelines or excessive phase budget.");

        var catalog = ordered.SelectMany(t => t.Phases).Where(p => p.CreatesOcean)
            .GroupBy(p => p.SourceEventId).OrderBy(g => g.Key).Select(g =>
            {
                var first = g.First();
                if (g.Any(p => p.StartTimeMyr != first.StartTimeMyr || p.EndTimeMyr != first.EndTimeMyr))
                    throw new ArgumentException("A source-event ID has incompatible birth intervals.");
                return new OceanFormationEvent(g.Key, first.StartTimeMyr, first.EndTimeMyr, provenance);
            }).ToArray();
        if (catalog.Length > 4096) throw new ArgumentException("Formation-event budget exceeded.");
        var bins = Enumerable.Range(0, Bins * Bins).Select(_ => new List<Image>()).ToArray();
        int imageCount = 0, entries = 0;
        double width = scale.ReferenceWidth, length = scale.ReferenceLength;
        for (int k = 0; k < ordered.Length; k++)
        {
            var bounds = Bounds(ordered[k]);
            if (bounds is null) continue; // A wholly dormant timeline creates no floor.
            var (xmin, xmax, zmin, zmax) = bounds.Value;
            // Padding is ONLY a conservative broad-phase envelope. The old exact
            // solver still decides membership, endpoints and ambiguous histories.
            double pad = 1e-10 * Math.Max(width, length);
            xmin -= pad; xmax += pad; zmin -= pad; zmax += pad;
            if (!double.IsFinite(xmin + xmax + zmin + zmax)
                || Math.Max(Math.Abs(xmin), Math.Abs(xmax)) > 1024 * width
                || Math.Max(Math.Abs(zmin), Math.Abs(zmax)) > 1024 * length)
                throw new ArgumentException("Unwrapped history exceeds the explicit image budget.");
            long ix0 = periodic ? (long)Math.Ceiling(xmin / width - 1) : 0;
            long ix1 = periodic ? (long)Math.Floor(xmax / width) : 0;
            long iz0 = periodic ? (long)Math.Ceiling(zmin / length - 1) : 0;
            long iz1 = periodic ? (long)Math.Floor(zmax / length) : 0;
            if ((ix1 - ix0 + 1) * (iz1 - iz0 + 1) > MaximumImages)
                throw new ArgumentException("Too many periodic images; no truncated reconstruction.");
            for (long iz = iz0; iz <= iz1; iz++) for (long ix = ix0; ix <= ix1; ix++)
            {
                double a = Math.Max(0, xmin - ix * width), b = Math.Min(width, xmax - ix * width);
                double c = Math.Max(0, zmin - iz * length), d = Math.Min(length, zmax - iz * length);
                if (a > b || c > d) continue;
                if (++imageCount > MaximumImages) throw new ArgumentException("Atlas image budget exceeded.");
                var image = new Image(k, ix, iz);
                int bx0 = Bin(a, width), bx1 = Bin(b, width), bz0 = Bin(c, length), bz1 = Bin(d, length);
                for (int bz = bz0; bz <= bz1; bz++) for (int bx = bx0; bx <= bx1; bx++)
                {
                    if (++entries > MaximumBinEntries) throw new ArgumentException("Atlas index budget exceeded.");
                    bins[bz * Bins + bx].Add(image);
                }
            }
        }
        double[] birth = new double[side * side];
        int[] labels = Enumerable.Repeat(-1, birth.Length).ToArray();
        long queries = 0; int oceanCells = 0;
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x;
            if (oceanicKm[i] == 0) continue;
            oceanCells++;
            double px = (x + .5) * width / side, pz = (z + .5) * length / side;
            OceanBirthWitness? accepted = null;
            foreach (var image in bins[Bin(pz, length) * Bins + Bin(px, width)])
            {
                if (++queries > MaximumQueries) throw new ArgumentException("Atlas query budget exceeded.");
                if (!ordered[image.Timeline].TryResolveBirth(px + image.X * width,
                    pz + image.Z * length, out var witness)) continue;
                if (accepted is { } prior &&
                    (prior.SourceEventId != witness.SourceEventId || Math.Abs(prior.BirthTimeMyr - witness.BirthTimeMyr) > DuplicateBirthToleranceMyr))
                    throw new ArgumentException($"Conflicting dated material histories at cell {i}; resolve topology, not age averaging.");
                accepted ??= witness;
            }
            if (accepted is not { } found)
                throw new ArgumentException($"Unresolved ocean formation at cell {i}; no uniform or nearest-ridge age fallback.");
            birth[i] = found.BirthTimeMyr; labels[i] = found.SourceEventId;
        }
        string boundary = periodic ? "EXPLICIT_PERIODIC_IMAGES" : "NON_PERIODIC";
        string canonical = JsonSerializer.Serialize(new { AlgorithmId, boundary,
            timelines = ordered.Select(t => new { t.Name, t.Phases }) });
        // Bind the complete geometry/chronology without making a huge JSON
        // description part of every downstream canonical history string.
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var map = OceanBirthMap.Create(seed, scale, side, oceanicKm, birth, labels, catalog,
            provenance + "|" + AlgorithmId + "|" + boundary + "|input-sha256=" + digest);
        return new(map, new(oceanCells, queries, imageCount, Bins, boundary,
            "PRESCRIBED_HISTORY_COVERED_NOT_AUTOMATIC_GEOLOGICAL_RECONSTRUCTION"));
    }

    private static int Bin(double value, double extent) => Math.Clamp((int)Math.Floor(value * Bins / extent), 0, Bins - 1);

    // Forward images of BOTH temporal and BOTH spatial ends of every active
    // phase. A constant translational phase sweeps a parallelogram; all points
    // lie within these extrema. Dormant phases still contribute later motion.
    private static (double Xmin, double Xmax, double Zmin, double Zmax)? Bounds(SpreadingTimeline timeline)
    {
        double xmin = double.PositiveInfinity, xmax = double.NegativeInfinity;
        double zmin = double.PositiveInfinity, zmax = double.NegativeInfinity;
        double futureX = 0, futureZ = 0; bool any = false;
        for (int k = timeline.Phases.Count - 1; k >= 0; k--)
        {
            var p = timeline.Phases[k]; double dt = p.EndTimeMyr - p.StartTimeMyr;
            if (p.CreatesOcean)
            {
                Add(p.Ax + p.MaterialVelocityX * dt + futureX, p.Az + p.MaterialVelocityZ * dt + futureZ);
                Add(p.Bx + p.MaterialVelocityX * dt + futureX, p.Bz + p.MaterialVelocityZ * dt + futureZ);
                Add(p.Ax + p.RidgeVelocityX * dt + futureX, p.Az + p.RidgeVelocityZ * dt + futureZ);
                Add(p.Bx + p.RidgeVelocityX * dt + futureX, p.Bz + p.RidgeVelocityZ * dt + futureZ);
            }
            futureX += p.MaterialVelocityX * dt; futureZ += p.MaterialVelocityZ * dt;
            if (!double.IsFinite(futureX) || !double.IsFinite(futureZ)) throw new ArithmeticException("Footprint motion overflow.");
        }
        return any ? (xmin, xmax, zmin, zmax) : null;
        void Add(double x, double z)
        {
            if (!double.IsFinite(x) || !double.IsFinite(z)) throw new ArithmeticException("Footprint overflow.");
            any = true; xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x);
            zmin = Math.Min(zmin, z); zmax = Math.Max(zmax, z);
        }
    }
}
