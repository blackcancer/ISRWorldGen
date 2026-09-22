using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISRWorldGen.Core.Geology.Evolution;

/// <summary>
/// Opt-in reduced material-attached kinematics. Voronoi assigns origins ONCE.
/// Velocities follow transported origins, with a positive metric support filter
/// modelling a finite deformation belt. Prescribed plate velocities are not
/// solved forces. First-order diffusion and mixing remain explicit limitations.
/// No height noise, image filtering, water surface or erosion is performed.
/// </summary>
public sealed class MaterialBoundHistory
{
    public const string AlgorithmId = "material-bound-history-v3-carrier-resolved-origin-fluxes";
    public const double ReferenceKmPerUnit = TectonicHistory.ReferenceKmPerUnit;
    public int Seed { get; }
    public double ReferenceWidth { get; }
    public double ReferenceLength { get; }
    public TectonicEvolutionSettings Settings { get; }
    public MaterialBoundSnapshot Initial { get; }
    public MaterialBoundSnapshot Final { get; }
    public ReadOnlyCollection<TectonicPlate> Plates { get; }
    public ReadOnlyCollection<TectonicLedger> Ledger { get; }
    public ReadOnlyCollection<double> InitialContinentalByOrigin { get; }
    public ReadOnlyCollection<double> FinalContinentalByOrigin { get; }
    public ReadOnlyCollection<double> FinalOwnerFraction { get; }
    public long UnresolvedInterfaceFaces { get; }
    public string InitialMaterialChecksum { get; }
    public string FinalMaterialChecksum { get; }
    public string Checksum { get; }
    public string InitialAssemblageChecksum { get; }

    private MaterialBoundHistory(int seed, TectonicScalePlan scale, TectonicEvolutionSettings settings,
        MaterialBoundSnapshot initial, MaterialBoundSnapshot final, TectonicPlate[] plates,
        List<TectonicLedger> ledger, double[] firstOrigin, double[] lastOrigin, double[] ownerFraction, long unresolved, string initialMaterialChecksum, string finalMaterialChecksum, string? assemblageChecksum)
    {
        Seed = seed; ReferenceWidth = scale.ReferenceWidth; ReferenceLength = scale.ReferenceLength; Settings = settings;
        Initial = initial; Final = final; Plates = Array.AsReadOnly(plates); Ledger = ledger.AsReadOnly();
        InitialContinentalByOrigin = Array.AsReadOnly(firstOrigin); FinalContinentalByOrigin = Array.AsReadOnly(lastOrigin);
        FinalOwnerFraction = Array.AsReadOnly(ownerFraction); UnresolvedInterfaceFaces = unresolved;
        InitialMaterialChecksum = initialMaterialChecksum; FinalMaterialChecksum = finalMaterialChecksum;
        InitialAssemblageChecksum = assemblageChecksum ?? "LEGACY_EQUAL_QUOTA_INITIALIZATION";
        string canonical = JsonSerializer.Serialize(new { AlgorithmId, seed, ReferenceWidth, ReferenceLength, settings,
            initial = initial.Checksum, final = final.Checksum, plates, ledger, firstOrigin, lastOrigin, ownerFraction, unresolved, initialMaterialChecksum, finalMaterialChecksum });
        if (assemblageChecksum is not null) canonical += "|initial-assemblage=" + assemblageChecksum;
        Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static MaterialBoundHistory Generate(int seed, TectonicScalePlan scale, TectonicEvolutionSettings settings)
        => GenerateCore(seed, scale, settings, null);

    /// <summary>Explicit alternative initial MATERIALS; the qualified transport and legacy default stay unchanged.</summary>
    public static MaterialBoundHistory GenerateWithAssemblage(int seed, TectonicScalePlan scale,
        TectonicEvolutionSettings settings, ContinentalAssemblage assemblage)
    {
        ArgumentNullException.ThrowIfNull(assemblage);
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(settings);
        assemblage.RequireCompatible(seed, scale, settings.Side);
        return GenerateCore(seed, scale, settings, assemblage);
    }

    /// <summary>Observe actual material trajectories without feeding diagnostics
    /// back into velocities, material exchange, heights or history identity.</summary>
    public static (MaterialBoundHistory History, MaterialStrainSnapshot Strain) GenerateWithStrain(
        int seed, TectonicScalePlan scale, TectonicEvolutionSettings settings,
        ContinentalAssemblage assemblage, MaterialStrainOptions options)
    {
        ArgumentNullException.ThrowIfNull(assemblage);
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(settings);
        assemblage.RequireCompatible(seed, scale, settings.Side);
        var tracker = new MaterialStrainTracker(settings.Side, scale.ReferenceWidth, scale.ReferenceLength,
            assemblage.ContinentalKm, assemblage.OceanicKm, options);
        var history = GenerateCore(seed, scale, settings, assemblage, tracker);
        return (history, tracker.Snapshot());
    }

    private static MaterialBoundHistory GenerateCore(int seed, TectonicScalePlan scale,
        TectonicEvolutionSettings settings, ContinentalAssemblage? assemblage, MaterialStrainTracker? strain = null)
    {
        ArgumentNullException.ThrowIfNull(scale); ArgumentNullException.ThrowIfNull(settings);
        if (settings.Side > 512 || settings.PlateCount > 16 || settings.DeformationWidth > .25 * Math.Min(scale.ReferenceWidth, scale.ReferenceLength))
            throw new ArgumentException("Material-bound candidate budget: side<=512, plates<=16, support<=quarter short axis.");
        int n = settings.Side, count = n * n;
        double dx = scale.ReferenceWidth / n, dz = scale.ReferenceLength / n, area = dx * dz * ReferenceKmPerUnit * ReferenceKmPerUnit;
        // Reuse the current public initializer at duration zero. This avoids
        // copying or replacing the latest atlas construction on main. Historical
        // moving-site logic is used only for the declared t=0 origin partition.
        var initialHistory = TectonicHistory.Generate(seed, scale, new TectonicEvolutionSettings(
            side: n, plateCount: settings.PlateCount, cratonCount: settings.CratonCount, duration: 0,
            speedReferenceUnitsPerTime: settings.SpeedReferenceUnitsPerTime,
            deformationWidth: settings.DeformationWidth, lowerCrustMobility: settings.LowerCrustMobility,
            initialOceanAge: settings.InitialOceanAge, motionSign: settings.MotionSign));
        TectonicPlate[] plates = initialHistory.Plates.ToArray();
        double[] c = (assemblage?.ContinentalKm ?? initialHistory.Initial.ContinentalKm).ToArray();
        double[] o = (assemblage?.OceanicKm ?? initialHistory.Initial.OceanicKm).ToArray();
        int[] initialOwners = initialHistory.Initial.PlateIds.ToArray();
        var state = MaterialPlateCohorts.Create(n, plates.Length, initialOwners, c, o, o.Select(v => v * settings.InitialOceanAge).ToArray(), o);
        double[] compression = new double[count], extension = new double[count], shear = new double[count];
        double[][] fields = state.Aggregate();
        var initial = new MaterialBoundSnapshot(n, 0, fields[0], fields[1], fields[2], fields[3], compression, extension, shear, initialOwners);
        string firstMaterial = state.ComputeChecksum();
        double[] firstOrigin = state.ContinentalInventories();
        double initialC = CrustTransport.Sum(fields[0]), initialO = CrustTransport.Sum(fields[1]);
        double time = 0, created = 0, recycled = 0; long unresolved = 0;
        var ledger = new List<TectonicLedger> { new(0, initialC * area, initialO * area, 0, 0, CrustTransport.Sum(fields[2]) * area, c.Max(), 0, 0) };
        double speed = plates.Max(p => Math.Max(Math.Abs(p.Vx), Math.Abs(p.Vz)));
        double maxDt = Math.Min(1, speed > 0 ? .35 / (speed / dx + speed / dz) : 1);
        if (settings.LowerCrustMobility > 0)
            maxDt = Math.Min(maxDt, .40 / (settings.LowerCrustMobility * (2 / (dx * dx) + 2 / (dz * dz))));
        if (Math.Ceiling(settings.Duration / maxDt) > 4096) throw new ArgumentException("Material history exceeds 4096-step budget.");
        while (time < settings.Duration)
        {
            double dt = Math.Min(maxDt, settings.Duration - time);
            MaterialMotion motion = state.EvaluateMotion(plates, dx, dz, settings.DeformationWidth);
            var faces = motion.Faces();
            strain?.Advance(time, dt, motion.X, motion.Z);
            int[] lower = Enumerable.Repeat(-1, count).ToArray();
            double[] priority = new double[count]; int collisions = 0, subductions = 0;
            BuildSinks(state, motion, plates, lower, priority, dx, dz, settings.DeformationWidth, ref collisions, ref subductions, ref unresolved);
            for (int i = 0; i < count; i++)
            {
                compression[i] += Math.Max(-faces.Divergence[i], 0) * dt;
                extension[i] += Math.Max(faces.Divergence[i], 0) * dt;
                int x = i % n, z = i / n, e = z * n + (x + 1) % n, w = z * n + (x + n - 1) % n;
                int s = ((z + 1) % n) * n + x, north = ((z + n - 1) % n) * n + x;
                shear[i] += Math.Abs((motion.X[s] - motion.X[north]) / (2 * dz) + (motion.Z[e] - motion.Z[w]) / (2 * dx)) * dt / 2;
            }
            double expectedAge = CrustTransport.Sum(fields[2]) + dt * CrustTransport.Sum(fields[1]);
            MaterialPlateCohorts moved = state.Advect(faces.East, faces.South, dx, dz, dt).Age(dt);
            double[][] advected = moved.Aggregate();
            double[] born = new double[count], removed = new double[count], removedMoment = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (faces.Divergence[i] > 0 && advected[0][i] + advected[1][i] < 7)
                    born[i] = 7 - advected[0][i] - advected[1][i];
                if (lower[i] >= 0 && faces.Divergence[i] < 0)
                {
                    double excess = Math.Max(0, advected[1][i] - 7 * Math.Max(0, 1 - advected[0][i] / 35));
                    double ocean = moved.Value(lower[i], 1, i);
                    removed[i] = Math.Min(excess, ocean); // never consumes the overriding origin
                    if (ocean > 0) removedMoment[i] = moved.Value(lower[i], 2, i) * (removed[i] / ocean);
                }
            }
            state = moved.ExchangeOcean(born, lower, removed).RelaxContinental(dx, dz, dt, settings.LowerCrustMobility);
            fields = state.Aggregate(); created += CrustTransport.Sum(born); recycled += CrustTransport.Sum(removed);
            CrustTransport.RequireBalance(initialC, CrustTransport.Sum(fields[0]), "material-bound continental inventory");
            CrustTransport.RequireBalance(initialO + created - recycled, CrustTransport.Sum(fields[1]), "material-bound ocean inventory");
            CrustTransport.RequireBalance(expectedAge - CrustTransport.Sum(removedMoment), CrustTransport.Sum(fields[2]), "material-bound age moment");
            double[] currentOrigin = state.ContinentalInventories();
            for (int p = 0; p < plates.Length; p++) CrustTransport.RequireBalance(firstOrigin[p], currentOrigin[p], "continental origin " + p);
            if (fields[0].Any(v => v > 150)) throw new ArithmeticException("Material-bound crust exceeds rheology domain; no height clamp.");
            time += dt;
            ledger.Add(new(time, CrustTransport.Sum(fields[0]) * area, CrustTransport.Sum(fields[1]) * area,
                created * area, recycled * area, CrustTransport.Sum(fields[2]) * area, fields[0].Max(), collisions, subductions));
        }
        MaterialMotion finalMotion = state.EvaluateMotion(plates, dx, dz, settings.DeformationWidth);
        var final = new MaterialBoundSnapshot(n, time, fields[0], fields[1], fields[2], fields[3], compression, extension, shear, finalMotion.Owners.ToArray());
        return new MaterialBoundHistory(seed, scale, settings, initial, final, plates, ledger, firstOrigin, state.ContinentalInventories(), finalMotion.DominantFraction.ToArray(), unresolved, firstMaterial, state.ComputeChecksum(), assemblage?.Checksum);
    }

    private static void BuildSinks(MaterialPlateCohorts state, MaterialMotion motion, TectonicPlate[] plates,
        int[] lower, double[] priority, double dx, double dz, double width, ref int collisions, ref int subductions, ref long unresolved)
    {
        int n = state.Side, rx = (int)Math.Ceiling(width / dx), rz = (int)Math.Ceiling(width / dz);
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++) for (int axis = 0; axis < 2; axis++)
        {
            int i = z * n + x, j = axis == 0 ? z * n + (x + 1) % n : ((z + 1) % n) * n + x;
            if (motion.Owners[i] == motion.Owners[j]) continue;
            if (!motion.TryContact(i, axis == 0, plates, out var contact)) { unresolved++; continue; }
            if (contact.ClosingSpeed <= 1e-9) continue;
            int a = contact.PlateA, b = contact.PlateB;
            double oa = state.Value(a, 1, i), ob = state.Value(b, 1, j);
            var choice = CrustResponse.Choose(a, state.Value(a, 0, i), oa, oa > 0 ? state.Value(a, 2, i) / oa : 0,
                b, state.Value(b, 0, j), ob, ob > 0 ? state.Value(b, 2, j) / ob : 0);
            if (choice.Kind == TectonicContactKind.ContinentalCollision) { collisions++; continue; }
            subductions++;
            for (int oz = -rz; oz <= rz; oz++) for (int ox = -rx; ox <= rx; ox++)
            {
                if (ox * ox * dx * dx + oz * oz * dz * dz > width * width) continue;
                int t = ((z + oz + n) % n) * n + (x + ox + n) % n;
                if (motion.Owners[t] != choice.SubductingPlate) continue;
                // Stable iteration and pairwise contrast; unresolved near-ties
                // retain the first contact rather than relabelling a whole slab.
                if (contact.ClosingSpeed - priority[t] > 1e-9) { priority[t] = contact.ClosingSpeed; lower[t] = choice.SubductingPlate; }
            }
        }
    }

    public double SampleElevationKm(TectonicScalePlan scale, double x, double z)
    {
        ArgumentNullException.ThrowIfNull(scale);
        if (Math.Abs(scale.ReferenceWidth - ReferenceWidth) > 1e-8 || Math.Abs(scale.ReferenceLength - ReferenceLength) > 1e-8)
            throw new ArgumentException("Changed aspect requires its own complete reference atlas.");
        var p = scale.ToReference(x, z); int n = Settings.Side;
        double gx = p.X / ReferenceWidth * n - .5, gz = p.Z / ReferenceLength * n - .5;
        int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz); double tx = gx - ix, tz = gz - iz;
        int x0 = (ix + n) % n, x1 = (ix + 1 + n) % n, z0 = (iz + n) % n, z1 = (iz + 1 + n) % n;
        return (1 - tz) * ((1 - tx) * Final.ElevationKm[z0 * n + x0] + tx * Final.ElevationKm[z0 * n + x1])
            + tz * ((1 - tx) * Final.ElevationKm[z1 * n + x0] + tx * Final.ElevationKm[z1 * n + x1]);
    }
}
