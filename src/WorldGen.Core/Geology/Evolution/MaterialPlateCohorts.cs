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
    public const string AlgorithmId = "material-origin-donor-cohorts-v3-carrier-resolved-fluxes";
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
        // Keep the first-order donor law; carry oceanic moments on the actual
        // representable volume packets. No slope reconstruction or height smoothing.
        var next = new double[fields.Length][];
        for (int p = 0; p < PlateCount; p++)
        {
            // This call also validates geometry, velocities and the outgoing CFL.
            next[4 * p] = CrustTransport.Advect(fields[4 * p], east, south, Side, dx, dz, dt);
            var transported = AdvectOceanCarriers(p, east, south, dx, dz, dt);
            next[4 * p + 1] = transported.Ocean;
            next[4 * p + 2] = transported.Moment;
            next[4 * p + 3] = transported.Inherited;
        }
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
                double ocean = fields[4 * p + 1][i];
                double surviving = ocean - removed[i];
                next[4 * p + 1][i] = surviving;
                next[4 * p + 2][i] = surviving * (fields[4 * p + 2][i] / ocean);
                next[4 * p + 3][i] = surviving * (fields[4 * p + 3][i] / ocean);
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

    // Evaluate the same positive first-order donor stencil as actual carrier
    // packets. Independently rounded O and O*age can otherwise leave a positive
    // moment where the subnormal ocean amount rounds to zero. No cutoff, field
    // clamp or higher-order reconstruction is used. The representable ocean
    // packet carries its donor age and inherited fraction, including retention.
    private (double[] Ocean, double[] Moment, double[] Inherited) AdvectOceanCarriers(
        int plate, double[] east, double[] south, double dx, double dz, double dt)
    {
        double[] ocean = fields[4 * plate + 1], moment = fields[4 * plate + 2], inherited = fields[4 * plate + 3];
        double[] ages = new double[Count], fractions = new double[Count];
        for (int i = 0; i < Count; i++) if (ocean[i] > 0)
        {
            ages[i] = moment[i] / ocean[i]; fractions[i] = inherited[i] / ocean[i];
            if (!double.IsFinite(ages[i]) || !double.IsFinite(fractions[i]) || fractions[i] > 1)
                throw new ArithmeticException("Invalid oceanic donor concentration.");
        }
        double[] no = new double[Count], nm = new double[Count], ni = new double[Count];
        double ax = dt / dx, az = dt / dz;
        for (int z = 0; z < Side; z++) for (int x = 0; x < Side; x++)
        {
            int i = z * Side + x, e = z * Side + (x + 1) % Side, w = z * Side + (x + Side - 1) % Side;
            int so = ((z + 1) % Side) * Side + x, n = ((z + Side - 1) % Side) * Side + x;
            double outgoing = ax * (Math.Max(east[i], 0) + Math.Max(-east[w], 0))
                + az * (Math.Max(south[i], 0) + Math.Max(-south[n], 0));
            Add(i, 1 - outgoing); Add(e, ax * Math.Max(-east[i], 0)); Add(w, ax * Math.Max(east[w], 0));
            Add(so, az * Math.Max(-south[i], 0)); Add(n, az * Math.Max(south[n], 0));
            void Add(int donor, double coefficient)
            {
                double amount = ocean[donor] * coefficient;
                no[i] += amount; nm[i] += amount * ages[donor]; ni[i] += amount * fractions[donor];
            }
        }
        CrustTransport.RequireBalance(CrustTransport.Sum(ocean), CrustTransport.Sum(no), "origin ocean carrier transport");
        CrustTransport.RequireBalance(CrustTransport.Sum(moment), CrustTransport.Sum(nm), "origin ocean-age carrier transport");
        CrustTransport.RequireBalance(CrustTransport.Sum(inherited), CrustTransport.Sum(ni), "origin inherited carrier transport");
        return (no, nm, ni);
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
                throw new ArithmeticException($"Oceanic moment or inherited material lost its carrier: origin={p} cell={i} O={o:R} moment={a:R} inherited={inherited:R}.");
        }
    }
}
