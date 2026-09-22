using System.Collections.ObjectModel;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// A reduced, transported VISCOUS strain history, not elastic strain, plastic
/// strain, damage, a crack label or a source of ocean. Q=C*kappa is carried by
/// the same positive donor packets as continental volume. Lower-crust transfers
/// carry Q too. Only explicitly computed viscous deformation produces Q.
/// Own parallel carrier is checked against the authoritative material solver;
/// it never replaces that solver, rescales its mass, or repairs its heights.
/// </summary>
public sealed class ContinentalStrainMemory
{
    public const string AlgorithmId = "continental-carrier-viscous-strain-memory-v1";
    private readonly double[] carrier, moment;
    public int Side { get; }
    public ReadOnlyCollection<double> Carrier { get; }
    public ReadOnlyCollection<double> Moment { get; }
    public double ProducedMoment { get; }
    public double InitialMoment { get; }

    public ContinentalStrainMemory(int side, IReadOnlyList<double> continental,
        IReadOnlyList<double>? initialStrain = null)
    {
        ArgumentNullException.ThrowIfNull(continental);
        if (side < 2 || side > 512 || continental.Count != side * side ||
            (initialStrain is not null && initialStrain.Count != continental.Count))
            throw new ArgumentException("Invalid strain-memory geometry.");
        Side = side; carrier = continental.ToArray(); moment = new double[carrier.Length];
        for (int i = 0; i < carrier.Length; i++)
        {
            double s = initialStrain?[i] ?? 0;
            if (!double.IsFinite(carrier[i]) || carrier[i] < 0 || !double.IsFinite(s) || s < 0)
                throw new ArgumentException("Invalid continental carrier or initial strain.");
            moment[i] = carrier[i] * s;
        }
        Validate(); Carrier = Array.AsReadOnly(carrier); Moment = Array.AsReadOnly(moment);
        InitialMoment = CrustTransport.Sum(moment);
        if (!double.IsFinite(InitialMoment)) throw new ArithmeticException("Initial strain inventory overflow.");
    }

    private ContinentalStrainMemory(int side, double[] c, double[] q, double initial, double produced)
    {
        Side = side; carrier = c; moment = q; InitialMoment = initial; ProducedMoment = produced;
        Validate(); Carrier = Array.AsReadOnly(carrier); Moment = Array.AsReadOnly(moment);
        CrustTransport.RequireBalance(initial + produced, CrustTransport.Sum(q), "viscous memory inventory plus production");
    }

    public double Strain(int cell) => carrier[cell] > 0 ? moment[cell] / carrier[cell] : 0;

    /// <summary>Invariant sqrt(0.5*D:D) with Dyy=-(Dxx+Dzz).
    /// Uniform rigid rotation gives zero; simple shear gives abs(gamma)/2.
    /// Shear may weaken viscosity but is NOT an ocean-opening criterion.</summary>
    public static double Rate(double dxx, double dzz, double engineeringShear)
    {
        if (!double.IsFinite(dxx) || !double.IsFinite(dzz) || !double.IsFinite(engineeringShear))
            throw new ArgumentException("Invalid physical strain rate.");
        double sum = dxx * dxx + dzz * dzz + (dxx + dzz) * (dxx + dzz) + .5 * engineeringShear * engineeringShear;
        double rate = Math.Sqrt(.5 * sum);
        if (!double.IsFinite(rate)) throw new ArithmeticException("Strain-rate invariant overflow.");
        return rate;
    }

    public ContinentalStrainMemory Accumulate(IReadOnlyList<double> rates, double dt)
    {
        ArgumentNullException.ThrowIfNull(rates);
        if (rates.Count != carrier.Length || !double.IsFinite(dt) || dt < 0)
            throw new ArgumentException("Invalid strain production interval.");
        double[] next = new double[moment.Length], production = new double[moment.Length];
        for (int i = 0; i < next.Length; i++)
        {
            if (!double.IsFinite(rates[i]) || rates[i] < 0) throw new ArgumentException("Negative/nonfinite viscous production.");
            production[i] = carrier[i] * (rates[i] * dt);
            next[i] = moment[i] + production[i];
        }
        return new(Side, (double[])carrier.Clone(), next, InitialMoment, ProducedMoment + CrustTransport.Sum(production));
    }

    /// <summary>Immutable transport. The reference call checks the established
    /// CFL, geometry and conservation; no arbitrary small-carrier cutoff.</summary>
    public ContinentalStrainMemory Advect(double[] east, double[] south, double dx, double dz, double dt)
    {
        double[] expected = CrustTransport.Advect(carrier, east, south, Side, dx, dz, dt);
        int n = Side; double ax = dt / dx, az = dt / dz;
        double[] c = new double[carrier.Length], q = new double[carrier.Length], strain = new double[carrier.Length];
        for (int i = 0; i < strain.Length; i++) strain[i] = Strain(i);
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, e = z * n + (x + 1) % n, w = z * n + (x + n - 1) % n;
            int s = ((z + 1) % n) * n + x, no = ((z + n - 1) % n) * n + x;
            double outgoing = ax * (Math.Max(east[i], 0) + Math.Max(-east[w], 0))
                + az * (Math.Max(south[i], 0) + Math.Max(-south[no], 0));
            Add(i, 1 - outgoing); Add(e, ax * Math.Max(-east[i], 0)); Add(w, ax * Math.Max(east[w], 0));
            Add(s, az * Math.Max(-south[i], 0)); Add(no, az * Math.Max(south[no], 0));
            void Add(int donor, double coefficient)
            {
                double packet = carrier[donor] * coefficient;
                c[i] += packet; q[i] += packet * strain[donor];
            }
        }
        RequireSameCarrier(c, expected);
        CrustTransport.RequireBalance(CrustTransport.Sum(carrier), CrustTransport.Sum(c), "strain-memory carrier");
        return new(n, c, q, InitialMoment, ProducedMoment);
    }

    public ContinentalStrainMemory Relax(IReadOnlyList<double> authoritativeBefore,
        double dx, double dz, double dt, double mobility)
    {
        RequireCarrier(authoritativeBefore);
        double[] c0 = authoritativeBefore.ToArray();
        double[] expected = CrustTransport.RelaxThickCrust(c0, Side, dx, dz, dt, mobility);
        int n = Side; double ax = mobility * dt / (dx * dx), az = mobility * dt / (dz * dz);
        double[] east = new double[c0.Length], south = new double[c0.Length];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        {
            int i = z * n + x, e = z * n + (x + 1) % n, s = ((z + 1) % n) * n + x;
            double fe = ax * (Math.Max(c0[i] - 45, 0) - Math.Max(c0[e] - 45, 0));
            double fs = az * (Math.Max(c0[i] - 45, 0) - Math.Max(c0[s] - 45, 0));
            east[i] = fe == 0 ? 0 : fe / c0[fe > 0 ? i : e];
            south[i] = fs == 0 ? 0 : fs / c0[fs > 0 ? i : s];
        }
        var result = Advect(east, south, 1, 1, 1); result.RequireCarrier(expected); return result;
    }

    public void RequireCarrier(IReadOnlyList<double> authoritative) => RequireSameCarrier(carrier, authoritative);
    private static void RequireSameCarrier(IReadOnlyList<double> actual, IReadOnlyList<double> reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (actual.Count != reference.Count) throw new ArgumentException("Different material/memory grid.");
        for (int i = 0; i < actual.Count; i++)
            if (!double.IsFinite(reference[i]) || reference[i] < 0 || Math.Abs(actual[i] - reference[i]) > 1e-9)
                throw new ArithmeticException("Memory and continental volume disagree; no carrier replacement.");
    }
    private void Validate()
    {
        if (!double.IsFinite(InitialMoment) || !double.IsFinite(ProducedMoment) || ProducedMoment < 0)
            throw new ArithmeticException("Invalid history inventory.");
        for (int i = 0; i < carrier.Length; i++)
            if (!double.IsFinite(carrier[i]) || carrier[i] < 0 || !double.IsFinite(moment[i]) || moment[i] < 0 ||
                (carrier[i] == 0 && moment[i] != 0) || (carrier[i] > 0 && !double.IsFinite(moment[i] / carrier[i])))
                throw new ArithmeticException("Viscous memory lost its material carrier.");
    }
}
