namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Positivity-preserving donor-cell finite-volume transport on a periodic planar
/// preparatory atlas. Face velocities, NOT cell labels, determine fluxes. The
/// same operator transports continental/oceanic volume and the ocean-age moment.
/// There is no erosion, noise, height painting or output normalization here.
/// </summary>
public static class CrustTransport
{
    public static double[] Advect(double[] quantity, double[] eastVelocity, double[] southVelocity,
        int side, double dx, double dz, double dt)
    {
        Validate(quantity, eastVelocity, southVelocity, side, dx, dz, dt);
        double ax = dt / dx, az = dt / dz;
        var result = new double[quantity.Length];
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            int i = z * side + x, e = z * side + (x + 1) % side, w = z * side + (x + side - 1) % side;
            int s = ((z + 1) % side) * side + x, n = ((z + side - 1) % side) * side + x;
            double ve = eastVelocity[i], vw = eastVelocity[w], vs = southVelocity[i], vn = southVelocity[n];
            double outgoing = ax * (Math.Max(ve, 0) + Math.Max(-vw, 0)) + az * (Math.Max(vs, 0) + Math.Max(-vn, 0));
            if (outgoing > 1) throw new ArgumentOutOfRangeException(nameof(dt), "Outgoing finite-volume CFL exceeds one.");
            result[i] = quantity[i] * (1 - outgoing)
                + ax * (quantity[e] * Math.Max(-ve, 0) + quantity[w] * Math.Max(vw, 0))
                + az * (quantity[s] * Math.Max(-vs, 0) + quantity[n] * Math.Max(vn, 0));
            if (!double.IsFinite(result[i])) throw new ArithmeticException("Crust transport overflow.");
        }
        RequireBalance(Sum(quantity), Sum(result), "transport");
        return result;
    }

    /// <summary>Conservative lateral flow of overthickened lower crust, not removal of surface sediment.</summary>
    public static double[] RelaxThickCrust(double[] continental, int side, double dx, double dz, double dt, double mobility)
    {
        ArgumentNullException.ThrowIfNull(continental);
        if (side < 2 || side > 512 || continental.Length != side * side || !double.IsFinite(dx) || dx <= 0 ||
            !double.IsFinite(dz) || dz <= 0 || !double.IsFinite(dt) || dt < 0 || !double.IsFinite(mobility) || mobility < 0 ||
            continental.Any(q => !double.IsFinite(q) || q < 0)) throw new ArgumentException("Invalid lower-crust flow input.");
        double ax = mobility * dt / (dx * dx), az = mobility * dt / (dz * dz);
        if (2 * (ax + az) > 1) throw new ArgumentOutOfRangeException(nameof(dt), "Lower-crust flow CFL exceeded.");
        double[] result = (double[])continental.Clone();
        for (int z = 0; z < side; z++)
        for (int x = 0; x < side; x++)
        {
            int i = z * side + x, e = z * side + (x + 1) % side, s = ((z + 1) % side) * side + x;
            double fe = ax * (Math.Max(continental[i] - 45, 0) - Math.Max(continental[e] - 45, 0));
            double fs = az * (Math.Max(continental[i] - 45, 0) - Math.Max(continental[s] - 45, 0));
            result[i] -= fe + fs; result[e] += fe; result[s] += fs;
        }
        if (result.Any(q => !double.IsFinite(q) || q < 0)) throw new ArithmeticException("Invalid relaxed crust thickness.");
        RequireBalance(Sum(continental), Sum(result), "lower-crust redistribution");
        return result;
    }

    public static double Sum(IEnumerable<double> values)
    {
        double sum = 0, correction = 0;
        foreach (double value in values) { double y = value - correction, t = sum + y; correction = (t - sum) - y; sum = t; }
        return sum;
    }

    public static void RequireBalance(double expected, double actual, string name)
    {
        if (!double.IsFinite(expected) || !double.IsFinite(actual) || Math.Abs(expected - actual) > 1e-8 + 2e-12 * Math.Abs(expected))
            throw new ArithmeticException("Nonconservative " + name + ": " + expected.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                + " != " + actual.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void Validate(double[] q, double[] east, double[] south, int n, double dx, double dz, double dt)
    {
        ArgumentNullException.ThrowIfNull(q); ArgumentNullException.ThrowIfNull(east); ArgumentNullException.ThrowIfNull(south);
        if (n < 2 || n > 512 || q.Length != n * n || east.Length != q.Length || south.Length != q.Length ||
            !double.IsFinite(dx) || !double.IsFinite(dz) || dx <= 0 || dz <= 0 || !double.IsFinite(dt) || dt < 0 ||
            q.Any(v => !double.IsFinite(v) || v < 0) || east.Any(v => !double.IsFinite(v)) || south.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Invalid finite-volume geometry, field, velocity or time step.");
    }
}

public enum TectonicContactKind { ContinentalCollision, Subduction }
public readonly record struct SubductionChoice(TectonicContactKind Kind, int SubductingPlate, int OverridingPlate);

/// <summary>Material-dependent polarity. Identical ocean columns use stable plate IDs only as a documented tie-break.</summary>
public static class CrustResponse
{
    public const string PolarityPolicy = "pairwise-material-age-deadband-v2";
    public static SubductionChoice Choose(int plateA, double continentalA, double oceanicA, double ageA,
        int plateB, double continentalB, double oceanicB, double ageB)
    {
        if (plateA == plateB || plateA < 0 || plateB < 0 ||
            new[] { continentalA, oceanicA, ageA, continentalB, oceanicB, ageB }.Any(v => !double.IsFinite(v) || v < 0) ||
            continentalA + oceanicA == 0 || continentalB + oceanicB == 0) throw new ArgumentException("Invalid contact columns.");
        // Compare the CONTRAST, not two independently rounded values. Two equal
        // cohorts can straddle the same half-quantum (74.4140625 Myr was observed)
        // after transport; independent rounding then invents an age contrast.
        // Deadbands are model decision resolutions, never edits to mass/moments.
        bool ca = continentalA - oceanicA > 1e-9, cb = continentalB - oceanicB > 1e-9;
        if (ca && cb) return new SubductionChoice(TectonicContactKind.ContinentalCollision, -1, -1);
        double ageContrast = ageA - ageB;
        int lower = ca ? plateB : cb ? plateA : ageContrast > 1e-6 ? plateA : ageContrast < -1e-6 ? plateB : Math.Max(plateA, plateB);
        return new SubductionChoice(TectonicContactKind.Subduction, lower, lower == plateA ? plateB : plateA);
    }

    /// <summary>
    /// Closing speed at the LOCAL periodic contact. A pair of sites may have
    /// two opposite boundaries; a single shortest site-to-site vector is wrong
    /// for one of them. Lift both sites to the images nearest this face.
    /// </summary>
    public static double ClosingSpeed(TectonicPlate a, TectonicPlate b, double x, double z,
        double width, double length, double time)
    {
        if (a.Id == b.Id || width <= 0 || length <= 0 || time < 0 ||
            new[] { a.X, a.Z, a.Vx, a.Vz, b.X, b.Z, b.Vx, b.Vz, x, z, width, length, time }.Any(v => !double.IsFinite(v)))
            throw new ArgumentException("Invalid local contact kinematics.");
        double nx = Lift(b.X + b.Vx * time - x, width) - Lift(a.X + a.Vx * time - x, width);
        double nz = Lift(b.Z + b.Vz * time - z, length) - Lift(a.Z + a.Vz * time - z, length);
        double norm = Math.Sqrt(nx * nx + nz * nz);
        if (!double.IsFinite(norm) || norm <= 1e-8) throw new ArgumentException("Coincident or overflowing contact sites.");
        return ((a.Vx - b.Vx) * nx + (a.Vz - b.Vz) * nz) / norm;
        static double Lift(double value, double period) => value - Math.Floor(value / period + .5) * period;
    }

    /// <summary>
    /// Airy-style column buoyancy plus ocean thermal subsidence. Parameters are
    /// explicit toy-model values, not a calibrated Earth inversion. The negative
    /// branch includes water loading; its result remains the SOLID seabed height.
    /// </summary>
    public static double ElevationKm(double continentalKm, double oceanicKm, double oceanAge)
    {
        if (!double.IsFinite(continentalKm) || continentalKm < 0 || !double.IsFinite(oceanicKm) || oceanicKm < 0 ||
            !double.IsFinite(oceanAge) || oceanAge < 0 || continentalKm + oceanicKm <= 0)
            throw new ArgumentException("Invalid material column for isostatic response.");
        double oceanFraction = oceanicKm / (continentalKm + oceanicKm);
        double h = continentalKm * (3300d - 2800d) / 3300d + oceanicKm * (3300d - 2900d) / 3300d - 4.95
            + oceanFraction * (1.6 - .25 * Math.Sqrt(oceanAge));
        return h >= 0 ? h : h / (1 - 1030d / 3300d);
    }
}
