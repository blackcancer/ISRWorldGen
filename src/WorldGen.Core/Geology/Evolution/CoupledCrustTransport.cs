namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Unsplit MUSCL reconstruction with a common limiter and SSPRK2 in time.
/// The common reconstruction preserves linear material relations: oceanic mass,
/// its inherited cohort and its age moment are not transported independently.
/// This reduces numerical smearing without removing rock: it is NOT an erosion operation.
/// </summary>
public static class CoupledCrustTransport
{
    public const string AlgorithmId = "coupled-muscl-minmod-ssprk2-v1";
    public const double MaximumOutgoingCfl = .49;

    // Ordered fields: continental thickness, oceanic thickness, age moment,
    // inherited oceanic thickness. New ocean = oceanic - inherited.
    public static double[][] Advect(double[][] fields, double[] east, double[] south,
        int side, double dx, double dz, double dt)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(east); ArgumentNullException.ThrowIfNull(south);
        if (side is < 2 or > 512 || fields.Length != 4 || fields.Any(f => f is null || f.Length != side * side) ||
            east.Length != side * side || south.Length != east.Length ||
            !double.IsFinite(dx) || dx <= 0 || !double.IsFinite(dz) || dz <= 0 || !double.IsFinite(dt) || dt < 0 ||
            east.Any(v => !double.IsFinite(v)) || south.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Invalid coupled transport geometry, fields, velocities or timestep.");
        ValidateState(fields);
        double ax = dt / dx, az = dt / dz;
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x, w = z * side + (x + side - 1) % side, n = ((z + side - 1) % side) * side + x;
            double outgoing = ax * (Math.Max(east[i], 0) + Math.Max(-east[w], 0))
                + az * (Math.Max(south[i], 0) + Math.Max(-south[n], 0));
            if (!double.IsFinite(outgoing) || outgoing > MaximumOutgoingCfl)
                throw new ArgumentOutOfRangeException(nameof(dt), "Coupled reconstruction requires outgoing CFL <= .49.");
        }
        if (dt == 0) return fields.Select(f => (double[])f.Clone()).ToArray();
        double[][] stage = Euler(fields, east, south, side, ax, az);
        double[][] next = Euler(stage, east, south, side, ax, az);
        for (int f = 0; f < 4; f++)
        {
            for (int i = 0; i < east.Length; i++) next[f][i] = .5 * fields[f][i] + .5 * next[f][i];
            CrustTransport.RequireBalance(CrustTransport.Sum(fields[f]), CrustTransport.Sum(next[f]), "coupled material " + f);
        }
        ValidateState(next);
        return next;
    }

    private static double[][] Euler(double[][] fields, double[] east, double[] south, int n, double ax, double az)
    {
        int count = east.Length;
        var sx = Enumerable.Range(0, 4).Select(_ => new double[count]).ToArray();
        var sz = Enumerable.Range(0, 4).Select(_ => new double[count]).ToArray();
        var next = Enumerable.Range(0, 4).Select(_ => new double[count]).ToArray();
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, e = z * n + (x + 1) % n, w = z * n + (x + n - 1) % n;
            int s = ((z + 1) % n) * n + x, p = ((z + n - 1) % n) * n + x;
            double tx = 1, tz = 1;
            for (int f = 0; f < 4; f++)
            {
                tx = Math.Min(tx, Limiter(fields[f][w], fields[f][i], fields[f][e]));
                tz = Math.Min(tz, Limiter(fields[f][p], fields[f][i], fields[f][s]));
            }
            // Enforce the same reconstruction on the complementary cohort too.
            tx = Math.Min(tx, Limiter(fields[1][w] - fields[3][w], fields[1][i] - fields[3][i], fields[1][e] - fields[3][e]));
            tz = Math.Min(tz, Limiter(fields[1][p] - fields[3][p], fields[1][i] - fields[3][i], fields[1][s] - fields[3][s]));
            for (int f = 0; f < 4; f++)
            {
                sx[f][i] = tx * .5 * (fields[f][e] - fields[f][w]);
                sz[f][i] = tz * .5 * (fields[f][s] - fields[f][p]);
            }
        }
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, e = z * n + (x + 1) % n, w = z * n + (x + n - 1) % n;
            int s = ((z + 1) % n) * n + x, p = ((z + n - 1) % n) * n + x;
            for (int f = 0; f < 4; f++)
            {
                var q = fields[f]; var xs = sx[f]; var zs = sz[f];
                double fe = east[i] * (east[i] >= 0 ? q[i] + .5 * xs[i] : q[e] - .5 * xs[e]);
                double fw = east[w] * (east[w] >= 0 ? q[w] + .5 * xs[w] : q[i] - .5 * xs[i]);
                double fs = south[i] * (south[i] >= 0 ? q[i] + .5 * zs[i] : q[s] - .5 * zs[s]);
                double fp = south[p] * (south[p] >= 0 ? q[p] + .5 * zs[p] : q[i] - .5 * zs[i]);
                next[f][i] = q[i] - ax * (fe - fw) - az * (fs - fp);
            }
        }
        ValidateState(next);
        return next;
    }

    private static double Limiter(double left, double center, double right)
    {
        double backward = center - left, forward = right - center;
        if (backward == 0 && forward == 0) return 1;
        if (backward == 0 || forward == 0 || Math.Sign(backward) != Math.Sign(forward)) return 0;
        double centered = .5 * backward + .5 * forward;
        return Math.Min(1, Math.Min(Math.Abs(backward / centered), Math.Abs(forward / centered)));
    }

    private static void ValidateState(double[][] fields)
    {
        foreach (double[] field in fields)
            if (field.Any(v => !double.IsFinite(v) || v < 0)) throw new ArgumentException("Negative or nonfinite transported material.");
        for (int i = 0; i < fields[0].Length; i++)
            if (fields[3][i] > fields[1][i] + 1e-11 * Math.Max(1, fields[1][i]))
                throw new ArgumentException("Inherited cohort exceeds total oceanic material.");
    }
}
