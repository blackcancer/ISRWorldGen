namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Opt-in minmod spatial reconstruction and SSP-RK2 for NONNEGATIVE carriers.
/// Tracers travel on the same representable volume packets, with piecewise
/// constant donor concentrations. Carrier accuracy is not tracer accuracy.
/// No MUSCL reconstruction of age or independent rounding of age fluxes.
/// Face velocities are frozen for one call. This is not a mechanical law.
/// </summary>
public static class CrustPacketTransport
{
    public const string AlgorithmId = "minmod-carrier-ssprk2-constant-concentration-packets-v1";
    public sealed record Result(double[] Carrier, double[] Moment, double[] Inherited);

    public static double[] AdvectScalar(double[] q, double[] east, double[] south,
        int side, double dx, double dz, double dt)
        => Advect(q, null, null, east, south, side, dx, dz, dt).Carrier;

    public static Result Advect(double[] q, double[]? moment, double[]? inherited,
        double[] east, double[] south, int side, double dx, double dz, double dt)
    {
        ArgumentNullException.ThrowIfNull(q); ArgumentNullException.ThrowIfNull(east);
        ArgumentNullException.ThrowIfNull(south);
        bool tracers = moment is not null || inherited is not null;
        if (side is < 2 or > 512 || q.Length != side * side || east.Length != q.Length || south.Length != q.Length ||
            !double.IsFinite(dx) || !double.IsFinite(dz) || !double.IsFinite(dt) || dx <= 0 || dz <= 0 || dt < 0 ||
            q.Any(v => !double.IsFinite(v) || v < 0) || east.Any(v => !double.IsFinite(v)) || south.Any(v => !double.IsFinite(v)) ||
            (tracers && (moment is null || inherited is null || moment.Length != q.Length || inherited.Length != q.Length)))
            throw new ArgumentException("Invalid reconstructed carrier transport.");
        if (tracers) for (int i = 0; i < q.Length; i++)
            if (!double.IsFinite(moment![i]) || moment[i] < 0 || !double.IsFinite(inherited![i]) || inherited[i] < 0 ||
                inherited[i] > q[i] || (q[i] == 0 && moment[i] != 0) || (q[i] > 0 && !double.IsFinite(moment[i] / q[i])))
                throw new ArgumentException("Unsupported oceanic moment or concentration.");
        double ax = dt / dx, az = dt / dz;
        if (!double.IsFinite(ax) || !double.IsFinite(az)) throw new ArgumentException("Transport metric overflow.");
        for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
        {
            int i = z * side + x, w = z * side + (x + side - 1) % side, n = ((z + side - 1) % side) * side + x;
            double cfl = ax * (Math.Max(east[i], 0) + Math.Max(-east[w], 0)) + az * (Math.Max(south[i], 0) + Math.Max(-south[n], 0));
            // Minmod faces are <=1.5q, hence <=.75q outflow with this gate.
            if (!double.IsFinite(cfl) || cfl > .5) throw new ArgumentOutOfRangeException(nameof(dt), "Reconstructed outgoing CFL exceeds one half.");
        }
        if (dt == 0) return new((double[])q.Clone(), moment?.ToArray() ?? [], inherited?.ToArray() ?? []);
        Result first = Euler(q, moment, inherited);
        Result second = Euler(first.Carrier, tracers ? first.Moment : null, tracers ? first.Inherited : null);
        var result = new Result(new double[q.Length], tracers ? new double[q.Length] : [], tracers ? new double[q.Length] : []);
        for (int i = 0; i < q.Length; i++)
        {
            // Convex time-stage averaging is also performed on carrier packets.
            // Otherwise half an epsilon can vanish while half its age survives.
            double a = .5 * q[i], b = .5 * second.Carrier[i];
            result.Carrier[i] = a + b;
            if (tracers)
            {
                result.Moment[i] = (q[i] > 0 ? a * (moment![i] / q[i]) : 0)
                    + (second.Carrier[i] > 0 ? b * (second.Moment[i] / second.Carrier[i]) : 0);
                result.Inherited[i] = (q[i] > 0 ? a * (inherited![i] / q[i]) : 0)
                    + (second.Carrier[i] > 0 ? b * (second.Inherited[i] / second.Carrier[i]) : 0);
            }
        }
        ValidateResult(q, moment, inherited, result);
        return result;

        Result Euler(double[] volume, double[]? ages, double[]? old)
        {
            var next = new Result(new double[q.Length], tracers ? new double[q.Length] : [], tracers ? new double[q.Length] : []);
            for (int z = 0; z < side; z++) for (int x = 0; x < side; x++)
            {
                int i = z * side + x;
                if (volume[i] == 0) continue;
                int e = z * side + (x + 1) % side, w = z * side + (x + side - 1) % side;
                int s = ((z + 1) % side) * side + x, n = ((z + side - 1) % side) * side + x;
                double sx = Minmod(volume[i] - volume[w], volume[e] - volume[i]) / volume[i];
                double sz = Minmod(volume[i] - volume[n], volume[s] - volume[i]) / volume[i];
                double ce = ax * Math.Max(east[i], 0) * (1 + .5 * sx), cw = ax * Math.Max(-east[w], 0) * (1 - .5 * sx);
                double cs = az * Math.Max(south[i], 0) * (1 + .5 * sz), cn = az * Math.Max(-south[n], 0) * (1 - .5 * sz);
                double keep = 1 - (ce + cw + cs + cn);
                if (!double.IsFinite(keep) || keep < 0) throw new ArithmeticException("Negative carrier coefficient; no clipping.");
                double age = tracers ? ages![i] / volume[i] : 0, fraction = tracers ? old![i] / volume[i] : 0;
                Add(i, keep); Add(e, ce); Add(w, cw); Add(s, cs); Add(n, cn);
                void Add(int to, double coefficient)
                {
                    double amount = volume[i] * coefficient;
                    next.Carrier[to] += amount;
                    if (tracers) { next.Moment[to] += amount * age; next.Inherited[to] += amount * fraction; }
                }
            }
            ValidateResult(volume, ages, old, next);
            return next;
        }
    }

    private static double Minmod(double a, double b)
        => a > 0 && b > 0 ? Math.Min(a, b) : a < 0 && b < 0 ? Math.Max(a, b) : 0;

    private static void ValidateResult(double[] q, double[]? m, double[]? h, Result result)
    {
        for (int i = 0; i < q.Length; i++)
        {
            double carrier = result.Carrier[i];
            if (!double.IsFinite(carrier) || carrier < 0) throw new ArithmeticException("Invalid reconstructed carrier.");
            if (m is not null && (!double.IsFinite(result.Moment[i]) || result.Moment[i] < 0 ||
                !double.IsFinite(result.Inherited[i]) || result.Inherited[i] < 0 || result.Inherited[i] > carrier ||
                (carrier == 0 && result.Moment[i] != 0))) throw new ArithmeticException("Reconstructed tracer lost its carrier.");
        }
        CrustTransport.RequireBalance(CrustTransport.Sum(q), CrustTransport.Sum(result.Carrier), "reconstructed carrier");
        if (m is not null)
        {
            CrustTransport.RequireBalance(CrustTransport.Sum(m), CrustTransport.Sum(result.Moment), "reconstructed age moment");
            CrustTransport.RequireBalance(CrustTransport.Sum(h!), CrustTransport.Sum(result.Inherited), "reconstructed inherited carrier");
        }
    }
}
