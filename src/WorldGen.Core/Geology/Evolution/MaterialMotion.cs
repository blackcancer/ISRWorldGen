using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// One resolved material interface and its prescribed velocity contrast.
/// The normal is computed at the current interface, not between moving seeds.
/// Unresolved normals are reported; they are never replaced by a seed vector.
/// </summary>
public readonly record struct MaterialContact(int PlateA, int PlateB, double NormalX, double NormalZ,
    double ClosingSpeed, double TangentialSpeed);

public sealed class MaterialMotion
{
    private readonly int side;
    private readonly double dx, dz;
    private readonly double[][] support;
    public ReadOnlyCollection<double> X { get; }
    public ReadOnlyCollection<double> Z { get; }
    public ReadOnlyCollection<int> Owners { get; }
    public ReadOnlyCollection<double> DominantFraction { get; }

    internal MaterialMotion(int side, double dx, double dz, double[] x, double[] z, int[] owners,
        double[] purity, double[][] support)
    {
        this.side = side; this.dx = dx; this.dz = dz; this.support = support;
        X = Array.AsReadOnly(x); Z = Array.AsReadOnly(z); Owners = Array.AsReadOnly(owners); DominantFraction = Array.AsReadOnly(purity);
    }

    public (double[] East, double[] South, double[] Divergence) Faces()
    {
        int count = side * side;
        double[] east = new double[count], south = new double[count], divergence = new double[count];
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x, e = z * side + (x + 1) % side, s = ((z + 1) % side) * side + x;
            east[i] = .5 * (X[i] + X[e]); south[i] = .5 * (Z[i] + Z[s]);
        }
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x, w = z * side + (x + side - 1) % side, n = ((z + side - 1) % side) * side + x;
            divergence[i] = (east[i] - east[w]) / dx + (south[i] - south[n]) / dz;
        }
        return (east, south, divergence);
    }

    public bool TryContact(int cell, bool eastFace, IReadOnlyList<TectonicPlate> plates, out MaterialContact contact)
    {
        ArgumentNullException.ThrowIfNull(plates);
        if (cell < 0 || cell >= side * side || plates.Count != support.Length ||
            plates.Where((p, i) => p.Id != i || !double.IsFinite(p.Vx) || !double.IsFinite(p.Vz)).Any())
            throw new ArgumentException("Invalid material-contact input.");
        int x = cell % side, z = cell / side;
        int neighbor = eastFace ? z * side + (x + 1) % side : ((z + 1) % side) * side + x;
        int a = Owners[cell], b = Owners[neighbor]; contact = default;
        if (a == b) return false;
        double Phi(int xx, int zz) { int i = ((zz + side) % side) * side + (xx + side) % side; return support[b][i] - support[a][i]; }
        double nx, nz;
        if (eastFace)
        {
            nx = (Phi(x + 1, z) - Phi(x, z)) / dx;
            nz = (Phi(x, z + 1) + Phi(x + 1, z + 1) - Phi(x, z - 1) - Phi(x + 1, z - 1)) / (4 * dz);
        }
        else
        {
            nz = (Phi(x, z + 1) - Phi(x, z)) / dz;
            nx = (Phi(x + 1, z) + Phi(x + 1, z + 1) - Phi(x - 1, z) - Phi(x - 1, z + 1)) / (4 * dx);
        }
        double norm = Math.Sqrt(nx * nx + nz * nz);
        // Gradient threshold is dimensionless over one cell. A normal which
        // opposes this ordered face is unresolved at this grid resolution.
        if (!(norm * Math.Min(dx, dz) > 1e-12) || (eastFace ? nx : nz) <= 0) return false;
        nx /= norm; nz /= norm;
        double ux = plates[a].Vx - plates[b].Vx, uz = plates[a].Vz - plates[b].Vz;
        contact = new MaterialContact(a, b, nx, nz, ux * nx + uz * nz, Math.Abs(-ux * nz + uz * nx));
        return true;
    }
}
