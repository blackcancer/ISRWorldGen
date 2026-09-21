using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Per-origin extensive continental/oceanic columns and ocean-age/inherited
/// moments. Positive donor-cell fluxes move every constituent together. Nothing
/// is reassigned by a new Voronoi partition. This is not a rigid-body solver.
/// Instances own their arrays; public reads return copies or scalar values.
/// </summary>
public sealed class MaterialPlateCohorts
{
    public const string AlgorithmId = "material-origin-donor-cohorts-v2-metric-support";
    public int Side { get; }
    public int PlateCount => fields.Length / 4;
    private readonly double[][] fields; // plate-major: C, O, O*age, inherited O
    private int Count => Side * Side;

    private MaterialPlateCohorts(int side, double[][] owned)
    {
        Side = side; fields = owned;
        ValidateState();
    }

    public static MaterialPlateCohorts Create(int side, int plateCount, int[] owners,
        double[] continental, double[] oceanic, double[] ageMoment, double[] inherited)
    {
        if (side is < 2 or > 512 || plateCount is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(side), "Candidate budget: side<=512, plates<=16.");
        ArgumentNullException.ThrowIfNull(owners);
        double[][] inputs = [continental, oceanic, ageMoment, inherited];
        if (owners.Length != side * side || owners.Any(p => p < 0 || p >= plateCount))
            throw new ArgumentException("Invalid initial material origin labels.");
        foreach (double[] input in inputs)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.Length != owners.Length || input.Any(v => !double.IsFinite(v) || v < 0))
                throw new ArgumentException("Invalid initial extensive field.");
        }
        var owned = Enumerable.Range(0, plateCount * 4).Select(_ => new double[owners.Length]).ToArray();
        for (int i = 0; i < owners.Length; i++)
            for (int k = 0; k < 4; k++) owned[4 * owners[i] + k][i] = inputs[k][i];
        return new MaterialPlateCohorts(side, owned);
    }

    public double Value(int plate, int constituent, int cell)
    {
        if (plate < 0 || plate >= PlateCount || constituent is < 0 or > 3 || cell < 0 || cell >= Count)
            throw new ArgumentOutOfRangeException(nameof(plate));
        return fields[4 * plate + constituent][cell];
    }

    public double[][] Aggregate()
    {
        var result = Enumerable.Range(0, 4).Select(_ => new double[Count]).ToArray();
        for (int p = 0; p < PlateCount; p++)
            for (int k = 0; k < 4; k++)
                for (int i = 0; i < Count; i++) result[k][i] += fields[4 * p + k][i];
        return result;
    }

    public string ComputeChecksum()
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> buffer = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, Side);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], PlateCount); hash.AppendData(buffer);
        foreach (double[] field in fields) foreach (double value in field)
        { BinaryPrimitives.WriteDoubleLittleEndian(buffer, value); hash.AppendData(buffer); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public double[] ContinentalInventories() => Enumerable.Range(0, PlateCount)
        .Select(p => CrustTransport.Sum(fields[4 * p])).ToArray();

    public MaterialPlateCohorts Advect(double[] east, double[] south, double dx, double dz, double dt)
    {
        // The unchanged, previously qualified first-order operator is deliberate.
        // There is no ratio reconstruction, MUSCL limiter or height smoothing.
        var next = new double[fields.Length][];
        for (int k = 0; k < fields.Length; k++) next[k] = CrustTransport.Advect(fields[k], east, south, Side, dx, dz, dt);
        return new MaterialPlateCohorts(Side, next);
    }

    public MaterialPlateCohorts Age(double dt)
    {
        if (!double.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
        double[][] next = Copy();
        for (int p = 0; p < PlateCount; p++)
            for (int i = 0; i < Count; i++) next[4 * p + 2][i] += next[4 * p + 1][i] * dt;
        return new MaterialPlateCohorts(Side, next);
    }

    public MaterialPlateCohorts ExchangeOcean(double[] born, int[] lowerPlate, double[] removed)
    {
        ArgumentNullException.ThrowIfNull(born); ArgumentNullException.ThrowIfNull(lowerPlate); ArgumentNullException.ThrowIfNull(removed);
        if (born.Length != Count || lowerPlate.Length != Count || removed.Length != Count)
            throw new ArgumentException("Invalid ocean-exchange geometry.");
        double[][] next = Copy();
        for (int i = 0; i < Count; i++)
        {
            if (!double.IsFinite(born[i]) || born[i] < 0 || !double.IsFinite(removed[i]) || removed[i] < 0 ||
                lowerPlate[i] < -1 || lowerPlate[i] >= PlateCount || (born[i] > 0 && removed[i] > 0))
                throw new ArgumentException("Invalid or simultaneous source and sink.");
            double mass = 0;
            for (int p = 0; p < PlateCount; p++) mass += fields[4 * p][i] + fields[4 * p + 1][i];
            if (born[i] > 0)
            {
                if (!(mass > 0)) throw new ArgumentException("New basalt needs a material recipient, not a geographic fallback.");
                for (int p = 0; p < PlateCount; p++)
                    next[4 * p + 1][i] += born[i] * ((fields[4 * p][i] + fields[4 * p + 1][i]) / mass);
                // Newborn age moment and inherited cohort remain zero.
            }
            if (removed[i] > 0)
            {
                int p = lowerPlate[i];
                if (p < 0 || removed[i] > fields[4 * p + 1][i])
                    throw new ArgumentException("Cannot recycle another plate or more oceanic material than exists.");
                double remaining = 1 - removed[i] / fields[4 * p + 1][i];
                next[4 * p + 1][i] = fields[4 * p + 1][i] - removed[i];
                next[4 * p + 2][i] *= remaining; next[4 * p + 3][i] *= remaining;
            }
        }
        return new MaterialPlateCohorts(Side, next);
    }

    public MaterialPlateCohorts RelaxContinental(double dx, double dz, double dt, double mobility)
    {
        double[][] total = Aggregate();
        // Reuse the baseline operator for its validation and reference total.
        double[] expected = CrustTransport.RelaxThickCrust(total[0], Side, dx, dz, dt, mobility);
        double ax = mobility * dt / (dx * dx), az = mobility * dt / (dz * dz);
        double[] east = new double[Count], south = new double[Count];
        for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
            {
                int i = z * Side + x, e = z * Side + (x + 1) % Side, s = ((z + 1) % Side) * Side + x;
                double fe = ax * (Math.Max(total[0][i] - 45, 0) - Math.Max(total[0][e] - 45, 0));
                double fs = az * (Math.Max(total[0][i] - 45, 0) - Math.Max(total[0][s] - 45, 0));
                // Face coefficients are fractions of the DONOR continental
                // column. The very same fraction moves every origin cohort.
                east[i] = fe == 0 ? 0 : fe / total[0][fe > 0 ? i : e];
                south[i] = fs == 0 ? 0 : fs / total[0][fs > 0 ? i : s];
            }
        double[][] next = Copy();
        for (int p = 0; p < PlateCount; p++)
            next[4 * p] = CrustTransport.Advect(fields[4 * p], east, south, Side, 1, 1, 1);
        var result = new MaterialPlateCohorts(Side, next);
        double[] actual = result.Aggregate()[0];
        for (int i = 0; i < Count; i++)
            if (Math.Abs(actual[i] - expected[i]) > 1e-9)
                throw new ArithmeticException("Origin-aware relaxation changed the baseline material total.");
        return result;
    }

    public MaterialMotion EvaluateMotion(IReadOnlyList<TectonicPlate> plates, double dx, double dz, double smoothingWidth)
    {
        ArgumentNullException.ThrowIfNull(plates);
        if (plates.Count != PlateCount || plates.Where((p, i) => p.Id != i || !double.IsFinite(p.Vx) || !double.IsFinite(p.Vz)).Any() ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 || !double.IsFinite(smoothingWidth) || smoothingWidth < 0 ||
            smoothingWidth > .25 * Math.Min(Side * dx, Side * dz))
            throw new ArgumentException("Invalid material-motion geometry or plate table.");
        var support = new double[PlateCount][];
        double[] vx = new double[Count], vz = new double[Count], sum = new double[Count], purity = new double[Count];
        int[] owners = new int[Count];
        for (int p = 0; p < PlateCount; p++)
        {
            support[p] = new double[Count];
            for (int i = 0; i < Count; i++) support[p][i] = fields[4 * p][i] + fields[4 * p + 1][i];
            support[p] = PlateVelocityCoherence.Apply(support[p], Side, dx, dz, 2 * smoothingWidth);
            for (int i = 0; i < Count; i++)
            {
                double m = support[p][i]; sum[i] += m; vx[i] += m * plates[p].Vx; vz[i] += m * plates[p].Vz;
            }
        }
        for (int i = 0; i < Count; i++)
        {
            if (!(sum[i] > 0) || !double.IsFinite(sum[i])) throw new ArithmeticException("No material velocity support.");
            vx[i] /= sum[i]; vz[i] /= sum[i];
            double rawTotal = 0, maximum = 0;
            for (int p = 0; p < PlateCount; p++)
            {
                double amount = fields[4 * p][i] + fields[4 * p + 1][i];
                rawTotal += amount; maximum = Math.Max(maximum, amount);
            }
            if (!(rawTotal > 0)) throw new ArithmeticException("Empty material column.");
            for (int p = 0; p < PlateCount; p++) support[p][i] /= sum[i];
            // Choose from the global maximum set: tolerant pairwise updates are
            // non-transitive for three or more origins.
            for (int p = 0; p < PlateCount; p++)
                if ((maximum - fields[4 * p][i] - fields[4 * p + 1][i]) / rawTotal <= 1e-12)
                { owners[i] = p; break; }
            purity[i] = (fields[4 * owners[i]][i] + fields[4 * owners[i] + 1][i]) / rawTotal;
        }
        return new MaterialMotion(Side, dx, dz, vx, vz, owners, purity, support);
    }

    private double[][] Copy() => fields.Select(a => (double[])a.Clone()).ToArray();
    private void ValidateState()
    {
        for (int p = 0; p < PlateCount; p++) for (int i = 0; i < Count; i++)
        {
            for (int k = 0; k < 4; k++)
                if (!double.IsFinite(fields[4 * p + k][i]) || fields[4 * p + k][i] < 0)
                    throw new ArithmeticException("Nonfinite or negative extensive material.");
            double o = fields[4 * p + 1][i], a = fields[4 * p + 2][i], inherited = fields[4 * p + 3][i];
            if ((o == 0 && (a != 0 || inherited != 0)) || inherited - o > 1e-10)
                throw new ArithmeticException("Oceanic moment or inherited material lost its carrier.");
        }
    }
}
