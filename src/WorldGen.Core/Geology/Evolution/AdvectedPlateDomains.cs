using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Advected in-plane plate coordinates. Voronoi sites initialize the partition
/// ONCE; their subsequent geometric motion cannot overwrite the carrier.
/// The positive reference-area measures use the same donor-cell faces as crust.
/// They are coordinates, NOT a ledger of surviving crust or a rigid slab model:
/// basal crustal flow may move relative to them and new basalt inherits local
/// coordinates. Prescribed plate velocities remain an external model input.
/// </summary>
public sealed class AdvectedPlateDomains
{
    public const string AlgorithmId = "advected-plate-coordinates-metric-velocity-coherence-2w-v2";
    private const double OwnerResolution = 1e-12;
    private readonly TectonicPlate[] plates;
    private readonly double[][] carrier;
    private readonly double[] totals;
    private readonly int[] owners;
    private readonly double transitionWidth;
    private readonly double[] velocityX, velocityZ;
    public int Side { get; }
    public double Width { get; }
    public double Length { get; }
    public int PlateCount => plates.Length;
    public ReadOnlyCollection<double> Inventory { get; }

    private AdvectedPlateDomains(TectonicPlate[] plates, double[][] carrier,
        int side, double width, double length, double transitionWidth)
    {
        this.plates = plates; this.carrier = carrier; this.transitionWidth = transitionWidth; Side = side; Width = width; Length = length;
        int count = side * side;
        totals = new double[count]; owners = new int[count];
        for (int i = 0; i < count; i++)
        {
            double maximum = 0;
            for (int k = 0; k < plates.Length; k++)
            {
                double q = carrier[k][i];
                if (!double.IsFinite(q) || q < 0) throw new ArithmeticException("Invalid plate carrier.");
                totals[i] += q; maximum = Math.Max(maximum, q);
            }
            if (!double.IsFinite(totals[i]) || totals[i] <= 0)
                throw new ArithmeticException("Uncovered plate coordinates; no nearest-site fallback.");
            // Find the global maximum first. A sequential tolerant comparison
            // would be non-transitive and depend on the ordering of candidates.
            for (int k = 0; k < plates.Length; k++)
                if ((maximum - carrier[k][i]) / totals[i] <= OwnerResolution) { owners[i] = k; break; }
        }
        Inventory = Array.AsReadOnly(carrier.Select(CrustTransport.Sum).ToArray());
        var rawX = new double[count]; var rawZ = new double[count];
        for (int i = 0; i < count; i++)
        {
            for (int k = 0; k < plates.Length; k++)
            { rawX[i] += carrier[k][i] * plates[k].Vx; rawZ[i] += carrier[k][i] * plates[k].Vz; }
            rawX[i] /= totals[i]; rawZ[i] /= totals[i];
        }
        // Local carrier feedback alone collapses converging fronts to the grid
        // scale and overthickens crust (full 512-square regression). Retain a
        // resolved deformation zone on BOTH sides of a material contact. This
        // nonlocal closure filters forcing, never the height or the conserved q.
        double coupling = Math.Min(2 * transitionWidth, Math.Min(width, length));
        velocityX = PlateVelocityCoherence.Apply(rawX, side, width / side, length / side, coupling);
        velocityZ = PlateVelocityCoherence.Apply(rawZ, side, width / side, length / side, coupling);
    }

    public static AdvectedPlateDomains FromVoronoi(IReadOnlyList<TectonicPlate> source,
        int side, double width, double length, double transitionWidth)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count is < 2 or > 32 || side is < 2 or > 512 ||
            !double.IsFinite(width) || !double.IsFinite(length) || width <= 0 || length <= 0 ||
            width > 1_000_000 || length > 1_000_000 ||
            !double.IsFinite(transitionWidth) || transitionWidth < 1 || transitionWidth > Math.Min(width, length))
            throw new ArgumentException("Invalid bounded carrier geometry.");
        TectonicPlate[] plates = source.OrderBy(p => p.Id).ToArray();
        for (int k = 0; k < plates.Length; k++)
        {
            var p = plates[k];
            if (p.Id != k || new[] { p.X, p.Z, p.Vx, p.Vz }.Any(v => !double.IsFinite(v)) ||
                p.X < 0 || p.X >= width || p.Z < 0 || p.Z >= length || Math.Abs(p.Vx) > 4000 || Math.Abs(p.Vz) > 4000)
                throw new ArgumentException("Plate IDs must be unique 0..N-1 with finite bounded initial states.");
            for (int j = 0; j < k; j++)
                if (p.X == plates[j].X && p.Z == plates[j].Z) throw new ArgumentException("Coincident initial sites.");
        }
        int count = side * side;
        double[][] q = plates.Select(_ => new double[count]).ToArray();
        var distances = new double[plates.Length];
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            int i = z * side + x; double px = (x + .5) * width / side, pz = (z + .5) * length / side;
            double nearest = double.PositiveInfinity;
            for (int k = 0; k < plates.Length; k++)
            {
                double a = Delta(px - plates[k].X, width), b = Delta(pz - plates[k].Z, length);
                distances[k] = Math.Sqrt(a * a + b * b); nearest = Math.Min(nearest, distances[k]);
            }
            double total = 0;
            for (int k = 0; k < plates.Length; k++) { q[k][i] = Math.Exp(-(distances[k] - nearest) / transitionWidth); total += q[k][i]; }
            for (int k = 0; k < plates.Length; k++) q[k][i] /= total;
        }
        return new AdvectedPlateDomains(plates, q, side, width, length, transitionWidth);
    }

    /// <summary>Immutable step. No clocks, moved sites, relabelling or per-cell renormalization of the conserved measures.</summary>
    public AdvectedPlateDomains Advect(double[] eastVelocity, double[] southVelocity, double dt)
    {
        double[][] moved = carrier.Select(q => CrustTransport.Advect(q, eastVelocity, southVelocity,
            Side, Width / Side, Length / Side, dt)).ToArray();
        return new AdvectedPlateDomains(plates, moved, Side, Width, Length, transitionWidth);
    }

    public double Fraction(int plate, int cell)
    {
        CheckCell(cell);
        if (plate < 0 || plate >= plates.Length) throw new ArgumentOutOfRangeException(nameof(plate));
        return carrier[plate][cell] / totals[cell];
    }
    public double Density(int cell) { CheckCell(cell); return totals[cell]; }
    public int Owner(int cell) { CheckCell(cell); return owners[cell]; }
    public double Confidence(int cell) { CheckCell(cell); return Fraction(owners[cell], cell); }
    public (double X, double Z) Velocity(int cell)
    {
        CheckCell(cell); return (velocityX[cell], velocityZ[cell]);
    }

    /// <summary>
    /// Positive closure at an east/south face. The normal follows the transported
    /// coordinate contrast, not the vector between obsolete site positions.
    /// Central tangential differences avoid imposing the raster axis as normal.
    /// </summary>
    public double ClosingSpeedAtFace(int cell, int axis)
    {
        CheckCell(cell);
        if (axis is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(axis));
        int x = cell % Side, z = cell / Side;
        int j = Index(x + (axis == 0 ? 1 : 0), z + (axis == 1 ? 1 : 0));
        int a = owners[cell], b = owners[j]; if (a == b) return 0;
        double dx = Width / Side, dz = Length / Side;
        double nx, nz;
        if (axis == 0)
        {
            nx = (Contrast(j) - Contrast(cell)) / dx;
            nz = (Contrast(Index(x, z + 1)) + Contrast(Index(x + 1, z + 1))
                - Contrast(Index(x, z - 1)) - Contrast(Index(x + 1, z - 1))) / (4 * dz);
        }
        else
        {
            nz = (Contrast(j) - Contrast(cell)) / dz;
            nx = (Contrast(Index(x + 1, z)) + Contrast(Index(x + 1, z + 1))
                - Contrast(Index(x - 1, z)) - Contrast(Index(x - 1, z + 1))) / (4 * dx);
        }
        double norm = Math.Sqrt(nx * nx + nz * nz);
        // An unresolved contrast is not evidence for a converging contact.
        if (norm * Math.Min(dx, dz) <= OwnerResolution) return 0;
        return ((plates[a].Vx - plates[b].Vx) * nx + (plates[a].Vz - plates[b].Vz) * nz) / norm;
        double Contrast(int i) => (carrier[b][i] - carrier[a][i]) / totals[i];
    }

    private int Index(int x, int z) => ((z % Side + Side) % Side) * Side + (x % Side + Side) % Side;
    private void CheckCell(int cell) { if (cell < 0 || cell >= totals.Length) throw new ArgumentOutOfRangeException(nameof(cell)); }
    private static double Delta(double x, double period) => x - Math.Floor(x / period + .5) * period;
}
